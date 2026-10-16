# Contract: UI Routes, Policies, and Components

**Feature**: `001-document-upload-management` | **Framework**: Blazor Server (`App.razor` → `AuthorizeRouteView`) | **Layout**: existing `MainLayout` / `NavMenu`, Bootstrap 5.3, Bootstrap Icons

Every page below calls services only, never `ApplicationDbContext` (Principle IV). It gets the user ID from the `NameIdentifier` claim, and it shows the existing "Access Denied" or "not found" patterns. Page attributes are the first layer of defense. Services enforce the same rules again (Principle III).

## Routes

| Route | Component | Attribute | Purpose / Stories | Notes |
|-------|-----------|-----------|-------------------|-------|
| `/documents` | `Pages/Documents.razor` | `[Authorize]` | My Documents + Upload (US1, US3, US4) | Upload panel (`DocumentUploadForm`) is reachable from one "Upload" button (SC-005). `DocumentList` has sort, filter, and paging. |
| `/documents/{documentId:int}` | `Pages/DocumentDetails.razor` | `[Authorize]` | Details, preview and download links, edit metadata, replace file, delete (confirm), shares (owner) (US4, US5) | Action buttons follow the `DocumentDetails.Can*` flags. If the service returns `null`, the page shows "Document not found or not available." |
| `/documents/shared` | `Pages/SharedDocuments.razor` | `[Authorize]` | Shared with Me (US5) | Shows "Shared by" and the share date. View, preview, and download only. |
| `/documents/search` | `Pages/DocumentSearch.razor` | `[Authorize]` | Search all accessible documents (US3) | Query string `?q=`, plus filters and sort. Shows "No documents found" when empty. |
| `/documents/team` | `Pages/TeamDocuments.razor` | `[Authorize(Policy = "TeamLead")]` | Team Documents (US8) | Employees get Access Denied from the policy, and the service also returns empty. Administrators get a department filter. |
| `/admin/document-reports` | `Pages/DocumentReports.razor` | `[Authorize(Policy = "Administrator")]` | Reports + activity log (US9) | UTC date range. Three report tables plus a paged activity log. |
| `/tasks/{taskId:int}` | `Pages/TaskDetails.razor` (new) | `[Authorize]` | Task details + Attachments (US6) | Uses `ITaskService.GetTaskByIdAsync` and `ITaskDocumentService` |
| `/projects/{projectId:int}` | `Pages/ProjectDetails.razor` (modified) | `[Authorize]` (existing) | + "Project Documents" card (US2) | Lists `GetProjectDocumentsAsync`. Upload form with `FixedProjectId`. |
| `/` | `Pages/Index.razor` (modified) | `[Authorize]` (existing) | + Recent Documents widget, + "My Documents" summary card (US7) | The empty state links to `/documents`. |
| `/tasks` | `Pages/Tasks.razor` (modified) | existing | The eye button navigates to `/tasks/{id}` | Replaces the TODO |
| `/notifications` | `Pages/Notifications.razor` (modified) | existing | "Open document" link when `DocumentId` is set | The target page re-checks access |
| `/documents/{id:int}/download`, `/documents/{id:int}/preview` | Razor Pages | `[Authorize]` | File delivery | See [http-endpoints.md](http-endpoints.md) |

## NavMenu additions (`Shared/NavMenu.razor`)

| Item | Icon | Visibility |
|------|------|------------|
| Documents → `/documents` | `bi-file-earmark-text` | All signed-in users |
| Shared with Me → `/documents/shared` | `bi-share` | All signed-in users |
| Search Documents → `/documents/search` | `bi-search` | All signed-in users |
| Team Documents → `/documents/team` | `bi-people-fill` | `<AuthorizeView Policy="TeamLead">` |
| Document Reports → `/admin/document-reports` | `bi-bar-chart` | `<AuthorizeView Policy="Administrator">` |

Menu visibility is cosmetic. The page policies and services are the enforcement layers.

## Shared components

### `Shared/DocumentList.razor`

| Parameter | Type | Meaning |
|-----------|------|---------|
| `Items` | `PagedResult<DocumentSummary>?` | Data (null shows "Loading…") |
| `Query` | `DocumentQuery` | Current filter, sort, and page |
| `OnQueryChanged` | `EventCallback<DocumentQuery>` | Raised on sort header click, filter apply, or page change |
| `ShowProjectFilter` / `ShowUploader` | `bool` | Column and filter toggles per page |
| `ProjectOptions` | `IReadOnlyList<ProjectOption>` | Project filter choices |
| `EmptyMessage` | `string` | Default "No documents found." |

It renders title (linking to details), category, upload date, size (human-readable), project, and optionally the uploader. Preview and download links follow the link rules in http-endpoints.md.

### `Shared/DocumentUploadForm.razor`

| Parameter | Type | Meaning |
|-----------|------|---------|
| `FixedProjectId` | `int?` | Pre-selects a project and locks it (project page) |
| `TaskId` | `int?` | Upload-from-task. The service forces the task's project. |
| `OnUploaded` | `EventCallback<IReadOnlyList<DocumentOperationResult>>` | Lets the parent refresh |

Behavior contract (research R-07):

- `InputFile multiple` with an `@key` reset.
- Each file gets a row with Title (defaulting to the file name without extension, required, ≤ 200), Category (required select of the 6 values), Description, Tags (comma-separated, ≤ 10 × 50), and Project (from `GetUploadableProjectsAsync`).
- Client pre-checks for size, empty files, and extension, with messages that list the supported types and the 25 MB limit.
- Files upload sequentially, each with its own progress bar and its own success or error message.
- Submit is disabled while required fields are missing, and those fields are highlighted (US1 AS4).

### `Shared/ConfirmDialog.razor`

A modal rendered by Blazor (no Bootstrap JS), with parameters `Title`, `Message`, `ConfirmText`, `OnConfirm`, and `OnCancel`. It's used for delete. On Cancel nothing changes (US4 AS3).

## User-facing messages (consistent wording)

| Code | Message |
|------|---------|
| `FileTooLarge` | "'{name}' is larger than the 25 MB limit." |
| `FileEmpty` | "'{name}' is empty. Choose a file that contains data." |
| `UnsupportedFileType` | "'{name}' is not a supported file type. Supported types: PDF, Word (.doc, .docx), Excel (.xls, .xlsx), PowerPoint (.ppt, .pptx), text (.txt), JPEG, PNG." |
| `MalwareDetected` | "'{name}' was rejected for security reasons." |
| `ScanFailed` | "'{name}' could not be checked for malware. Please try again later." |
| `NotFound` | "Document not found or not available." |
| `Forbidden` | "You don't have permission to perform this action." |
| `CrossProject` | "This document belongs to a different project and can't be attached to this task." |
| `StorageFailed` | "The file could not be saved. Please try again." |
