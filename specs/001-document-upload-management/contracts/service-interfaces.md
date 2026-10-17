# Contract: Business Service Interfaces

**Feature**: `001-document-upload-management` | **Layer**: `ContosoDashboard/Services/` | **Lifetime**: Scoped (matches existing services)

All methods are `async` and take the **requesting user's ID** (Constitution Principle III, IDOR prevention). On every call, services load the requester's live `Role` and `Department` from `Users` and apply `DocumentAccessPolicy` (research R-02). Pages get the user ID from the `NameIdentifier` claim, exactly as the existing pages do.

## Shared semantics

| Situation | Result |
|-----------|--------|
| Document doesn't exist **or** the requester can't access it | `NotFound` (read methods return `null` or an empty list). The UI message is always "Document not found or not available." |
| Requester can access the document but the operation isn't permitted | `Forbidden`. The UI also hides the action. |
| Input fails a business rule | Specific `DocumentErrorCode` plus a user-facing `Message` |
| Unexpected exception | Logged. Returns `StorageFailed` or a generic failure without internal details. |

## Shared types (`Services/DocumentModels.cs`)

```csharp
public enum DocumentErrorCode
{
    None, NotFound, Forbidden, Validation,
    FileEmpty, FileTooLarge, UnsupportedFileType,
    MalwareDetected, ScanFailed, StorageFailed,
    CrossProject, InvalidShareTarget
}

public sealed record DocumentOperationResult(
    bool Succeeded, DocumentErrorCode Error, string? Message, int? DocumentId = null)
{
    public static DocumentOperationResult Ok(int? documentId = null, string? message = null);
    public static DocumentOperationResult Fail(DocumentErrorCode error, string message);
}

public enum DocumentSortField { UploadedDate, Title, Category, FileSize }

public sealed class DocumentQuery
{
    public string? Category { get; init; }          // must be in DocumentCategories.All when set
    public int? ProjectId { get; init; }
    public DateOnly? FromDate { get; init; }        // inclusive, UTC day
    public DateOnly? ToDate { get; init; }          // inclusive, UTC day
    public string? Department { get; init; }        // Team Documents (Administrator only); ignored elsewhere
    public DocumentSortField SortBy { get; init; } = DocumentSortField.UploadedDate;
    public bool Descending { get; init; } = true;
    public int Page { get; init; } = 1;             // 1-based
    public int PageSize { get; init; } = 25;        // clamped to 1..100
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

public sealed record DocumentSummary(
    int DocumentId, string Title, string Category, DateTime UploadedDate, long FileSizeBytes,
    string FileType, bool IsPreviewable, int? ProjectId, string? ProjectName,
    int UploadedByUserId, string UploadedByName);

public sealed record SharedDocumentSummary(
    DocumentSummary Document, string SharedByName, DateTime SharedDate, bool ViaTeam);

public sealed record DocumentDetails(
    DocumentSummary Summary, string? Description, IReadOnlyList<string> Tags,
    string OriginalFileName, DateTime LastModifiedDate,
    bool CanEditMetadata, bool CanDelete, bool CanReplaceFile, bool CanShare);

public sealed record DocumentUploadRequest(
    string OriginalFileName, string Title, string Category, string? Description,
    int? ProjectId, IReadOnlyList<string> Tags, int? TaskId = null);

public sealed record DocumentMetadataUpdate(
    string Title, string Category, string? Description, IReadOnlyList<string> Tags);

public enum DocumentContentMode { Download, Preview }

public sealed record DocumentContentResult(
    bool Succeeded, DocumentErrorCode Error, Stream? Content, string? ContentType,
    string? DownloadFileName, bool Inline);    // caller disposes Content

public sealed record ProjectOption(int ProjectId, string Name);
```

---

## IDocumentService

```csharp
public interface IDocumentService
{
    // Upload (FR-001..FR-011, US1, US2, US6 AS2)
    Task<DocumentOperationResult> UploadDocumentAsync(
        DocumentUploadRequest request, Stream content, int requestingUserId, CancellationToken ct = default);
    Task<List<ProjectOption>> GetUploadableProjectsAsync(int requestingUserId);

    // Lists and search (FR-012..FR-016, FR-024, FR-031, FR-034)
    Task<PagedResult<DocumentSummary>> GetMyDocumentsAsync(int requestingUserId, DocumentQuery query);
    Task<PagedResult<DocumentSummary>> GetProjectDocumentsAsync(int projectId, int requestingUserId, DocumentQuery query);
    Task<PagedResult<DocumentSummary>> GetTeamDocumentsAsync(int requestingUserId, DocumentQuery query);
    Task<PagedResult<SharedDocumentSummary>> GetSharedWithMeAsync(int requestingUserId, DocumentQuery query);
    Task<PagedResult<DocumentSummary>> SearchDocumentsAsync(int requestingUserId, string searchText, DocumentQuery query);
    Task<List<DocumentSummary>> GetRecentDocumentsAsync(int requestingUserId, int count = 5);
    Task<List<string>> GetDepartmentsAsync(int requestingUserId);   // Team Documents department filter (FR-034)

    // Single document (FR-017, FR-018)
    Task<DocumentDetails?> GetDocumentAsync(int documentId, int requestingUserId);
    Task<DocumentContentResult> OpenDocumentContentAsync(int documentId, int requestingUserId, DocumentContentMode mode);

    // Management (FR-019..FR-021)
    Task<DocumentOperationResult> UpdateMetadataAsync(int documentId, DocumentMetadataUpdate update, int requestingUserId);
    Task<DocumentOperationResult> ReplaceFileAsync(
        int documentId, string originalFileName, Stream content, int requestingUserId, CancellationToken ct = default);
    Task<DocumentOperationResult> DeleteDocumentAsync(int documentId, int requestingUserId);
}
```

| Method | Authorization (live) | Behavior and side effects | Errors |
|--------|----------------------|---------------------------|--------|
| `UploadDocumentAsync` | Any authenticated user. If `ProjectId` is set: project member or manager, or Administrator. If `TaskId` is set: the task must be visible (`ITaskService.GetTaskByIdAsync`), otherwise `NotFound`. When the task has a project, the project is **forced** to it and the requester must be a member or the manager of that project, or an Administrator (FR-030). A task viewer who isn't (for example, a non-member assignee or creator) gets `Forbidden`, and nothing is stored. | Validate (R-11) → scan (R-08) → key → `UploadAsync` → insert Document, Tags, optional TaskDocument, and Activity (Upload, + TaskAttach) in one save → on failure delete the file. After commit, notify project members except the uploader (R-12). | `Validation`, `FileEmpty`, `FileTooLarge`, `UnsupportedFileType`, `MalwareDetected`, `ScanFailed`, `Forbidden` (project, including a task's project the requester isn't a member of), `NotFound` (task), `StorageFailed` |
| `GetUploadableProjectsAsync` | Self | Projects where the requester is a member or manager. All projects for an Administrator. | none |
| `GetMyDocumentsAsync` | Self | `UploadedByUserId == me`, plus filters, sort, and paging | none |
| `GetProjectDocumentsAsync` | Project member or manager, or Administrator | All documents with `ProjectId == projectId`. **Empty** result if not authorized. | none (empty) |
| `GetTeamDocumentsAsync` | Role ≥ TeamLead (live) | TeamLead/PM: uploads by users in **my department** with `Role == Employee`. Administrator: Employee uploads in all departments (optional `Department` filter). Employee: empty. | none (empty) |
| `GetSharedWithMeAsync` | Self | Documents with a share where `SharedWithUserId == me` or `SharedWithDepartment == myDept`, excluding documents I uploaded. Shows the most recent applicable share's sharer and date. | none |
| `SearchDocumentsAsync` | Self | `CanAccess` ∧ (Title ∨ Description ∨ Tag ∨ Uploader name ∨ Project name contains the term), plus filters, sort, and paging. The term is trimmed, 1–100 characters. | `Validation` (as an empty result with a message in the UI) |
| `GetRecentDocumentsAsync` | Self | My 5 most recent uploads, newest first | none |
| `GetDepartmentsAsync` | Live role | Choices for the Team Documents department filter. **Administrator**: all distinct non-empty `User.Department` values, ordered by name. **TeamLead or ProjectManager**: a one-item list with their own department, or empty if they have none. **Employee** or unknown user: empty. Never filters in memory. | none (empty) |
| `GetDocumentAsync` | `CanAccess` | Details plus capability flags: `CanEditMetadata = CanDelete = CanManage`, `CanReplaceFile = CanShare = IsOwner`. **No** activity is recorded. | `null` |
| `OpenDocumentContentAsync` | `CanAccess` | Download: attachment with `OriginalFileName`. Preview: only if `IsPreviewable`, inline with the server MIME. Records a Download or Preview activity, then returns the stream. | `NotFound`, `UnsupportedFileType` (preview of a non-previewable type), `StorageFailed` (missing file) |
| `UpdateMetadataAsync` | `CanManage` | Updates Title, Description, Category, and Tags (not the project), and `LastModifiedDate`. Records a MetadataEdit activity with a change summary. Last write wins. | `NotFound`, `Forbidden`, `Validation` |
| `ReplaceFileAsync` | `IsOwner` only (every role) | Validate → scan → **new** key → write → update record and Activity (Replace) → delete the old file. On any failure, the existing file and record are unchanged. | `NotFound`, `Forbidden`, file errors as in upload, `StorageFailed` |
| `DeleteDocumentAsync` | `CanManage` | One save: remove Document (cascade Tags, Shares, TaskDocuments) and add Activity (Delete). Then delete the file (best effort). Confirmation is a UI responsibility. | `NotFound`, `Forbidden` |

---

## IDocumentShareService

```csharp
public sealed record ShareTargets(IReadOnlyList<ShareUserOption> Users, IReadOnlyList<string> Departments);
public sealed record ShareUserOption(int UserId, string DisplayName, string? Department);
public sealed record DocumentShareInfo(
    int DocumentShareId, int? SharedWithUserId, string? SharedWithUserName,
    string? SharedWithDepartment, DateTime SharedDate);

public interface IDocumentShareService
{
    Task<ShareTargets?> GetShareTargetsAsync(int documentId, int requestingUserId);
    Task<DocumentOperationResult> ShareWithUsersAsync(int documentId, IReadOnlyCollection<int> userIds, int requestingUserId);
    Task<DocumentOperationResult> ShareWithDepartmentsAsync(int documentId, IReadOnlyCollection<string> departments, int requestingUserId);
    Task<List<DocumentShareInfo>?> GetSharesAsync(int documentId, int requestingUserId);
    Task<DocumentOperationResult> RemoveShareAsync(int documentShareId, int requestingUserId);
}
```

| Method | Authorization | Behavior | Errors |
|--------|---------------|----------|--------|
| `GetShareTargetsAsync` | `IsOwner` | All users except the owner, plus distinct non-empty departments | `null` if the document isn't accessible or the requester isn't the owner |
| `ShareWithUsersAsync` | `IsOwner` (every role, Administrator included) | Skips the owner and users who already have a share for this document. Inserts new shares and Activity (Share) rows. Notifies **newly added** recipients only (respecting their preference). | `NotFound`, `Forbidden`, `InvalidShareTarget` (unknown user, or only self) |
| `ShareWithDepartmentsAsync` | `IsOwner` | Skips departments already shared. The department must have at least one user. Inserts the share and Activity rows. Notifies the department's **current** members except the owner. | `NotFound`, `Forbidden`, `InvalidShareTarget` |
| `GetSharesAsync` | `IsOwner` | Current shares for the document | `null` if not owner or not accessible |
| `RemoveShareAsync` | `IsOwner` of the share's document | Deletes the share row, so access is revoked immediately. Records an Activity (ShareRemoved). | `NotFound`, `Forbidden` |

---

## ITaskDocumentService

```csharp
public sealed record TaskDocumentInfo(
    DocumentSummary Document, string AttachedByName, DateTime AttachedDate, bool CanDetach);

public interface ITaskDocumentService
{
    Task<List<TaskDocumentInfo>?> GetTaskDocumentsAsync(int taskId, int requestingUserId);
    Task<bool> CanAddDocumentsAsync(int taskId, int requestingUserId);
    Task<List<DocumentSummary>> GetAttachableDocumentsAsync(int taskId, int requestingUserId, string? searchText = null);
    Task<DocumentOperationResult> AttachDocumentAsync(int taskId, int documentId, int requestingUserId);
    Task<DocumentOperationResult> DetachDocumentAsync(int taskId, int documentId, int requestingUserId);
}
// Upload-from-task uses IDocumentService.UploadDocumentAsync with DocumentUploadRequest.TaskId set.
```

| Method | Authorization | Behavior | Errors |
|--------|---------------|----------|--------|
| `GetTaskDocumentsAsync` | Task visible | Attachments filtered by `CanAccess` (FR-029). `CanDetach = AttachedByUserId == me ∨ CanManage`. | `null` if the task isn't visible |
| `CanAddDocumentsAsync` | Task visible | `true` when the task has no project, or when the requester is a member or the manager of the task's project, or an Administrator (`DocumentAccessPolicy.CanUploadToProject`). The task page uses it to show or hide **Upload to task** and **Attach existing** (FR-030). | `false` (also when the task isn't visible) |
| `GetAttachableDocumentsAsync` | Task visible ∧ `CanAddDocumentsAsync` | `CanAccess` documents that pass the FR-030 rules for this task (same project; or no project and (owner ∨ CanManage); any document for a task with no project), not already attached. Limit 50, optional text filter. | empty (including for a non-member on a project task) |
| `AttachDocumentAsync` | Task visible ∧ `CanAccess(doc)` ∧ project-membership rule (a project task requires member, manager, or Administrator) ∧ FR-030 rules (data-model.md "Attach rules") | Inserts the TaskDocument (idempotent) and Activity (TaskAttach, with TaskId). If the document had no project and the task has one, sets `ProjectId`, records it in the activity `Details`, and notifies the project members (R-12). | `NotFound` (task not visible or document not accessible), `Forbidden` (non-member on a project task, or a no-project document the requester can't own or manage), `CrossProject` |
| `DetachDocumentAsync` | Attacher ∨ `CanManage` (and the task is visible) | Deletes the TaskDocument row only. The document's project is unchanged. Records an Activity (TaskDetach, with TaskId). | `NotFound`, `Forbidden` |

---

## IDocumentReportService (Administrator only)

```csharp
public sealed record DocumentTypeCount(string FileType, string DisplayName, int Uploads);
public sealed record UploaderCount(int UserId, string DisplayName, int Uploads);
public sealed record DocumentAccessCount(int DocumentId, string Title, int Downloads, int Previews);
public sealed record DailyAccessCount(DateOnly Day, int Downloads, int Previews);
public sealed record AccessPatternReport(IReadOnlyList<DocumentAccessCount> TopDocuments, IReadOnlyList<DailyAccessCount> PerDay);
public sealed record ActivityLogEntry(
    int DocumentActivityId, DateTime OccurredAt, string UserName, DocumentActivityAction Action,
    int DocumentId, string DocumentTitle, int? TaskId, string? Details);

public interface IDocumentReportService
{
    Task<List<DocumentTypeCount>?> GetMostUploadedTypesAsync(DateOnly from, DateOnly to, int requestingUserId);
    Task<List<UploaderCount>?> GetMostActiveUploadersAsync(DateOnly from, DateOnly to, int requestingUserId);
    Task<AccessPatternReport?> GetAccessPatternsAsync(DateOnly from, DateOnly to, int requestingUserId);
    Task<PagedResult<ActivityLogEntry>?> GetActivityLogAsync(
        DateOnly from, DateOnly to, DocumentActivityAction? action, int page, int pageSize, int requestingUserId);
}
```

| Rule | Detail |
|------|--------|
| Authorization | The live `Role == Administrator`. Otherwise every method returns `null`, and the page shows Access Denied. The page also uses `[Authorize(Policy = "Administrator")]`. |
| Range | Inclusive UTC days, `from ≤ to`, max 366 days. An invalid range returns an empty result. |
| Data source | `DocumentActivity` only, so deleted documents are included (FR-036, US9 AS5) |

---

## Changes to existing services

```csharp
public interface INotificationService
{
    // existing members unchanged …
    /// Inserts notifications in one SaveChanges, skipping recipients whose InAppNotificationsEnabled is false (FR-028).
    Task<int> CreateNotificationsAsync(IEnumerable<Notification> notifications);
}

public class DashboardSummary
{
    // existing properties unchanged …
    public int TotalDocuments { get; set; }   // documents uploaded by the user (FR-032)
}
```

`IDashboardService.GetDashboardSummaryAsync(userId)` fills in `TotalDocuments`. `ITaskService` and `IProjectService` are unchanged. `ITaskService.GetTaskByIdAsync` is reused as the task-visibility check.

## DI registration (`Program.cs`)

```csharp
builder.Services.Configure<DocumentStorageOptions>(builder.Configuration.GetSection("DocumentStorage"));
builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
builder.Services.AddSingleton<IMalwareScanner, SimulatedEicarMalwareScanner>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IDocumentShareService, DocumentShareService>();
builder.Services.AddScoped<ITaskDocumentService, TaskDocumentService>();
builder.Services.AddScoped<IDocumentReportService, DocumentReportService>();
```
