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
| `/documents/team` | `Pages/TeamDocuments.razor` | `[Authorize(Policy = "TeamLead")]` | Team Documents (US8) | Employees get Access Denied from the policy, and the service also returns empty. Administrators get a department filter whose choices come from `IDocumentService.GetDepartmentsAsync` (never from `ApplicationDbContext`). |
| `/admin/document-reports` | `Pages/DocumentReports.razor` | `[Authorize(Policy = "Administrator")]` | Reports + activity log (US9) | UTC date range. Three report tables plus a paged activity log. |
| `/tasks/{taskId:int}` | `Pages/TaskDetails.razor` (new) | `[Authorize]` | Task details + Attachments (US6) | Uses `ITaskService.GetTaskByIdAsync` and `ITaskDocumentService`. **Upload to task** and **Attach existing** are shown only when `CanAddDocumentsAsync` is true (project tasks: members, the PM, and Administrators only; FR-030). |
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
| `TaskProjectId` | `int?` | Set by the task page together with `TaskId` when the task belongs to a project. The project selector is then **hidden** and replaced by the read-only text "Project: {name} (from the task)", so the user can't pick a different project. When `TaskId` is set and the task has no project, the selector stays editable. |
| `OnUploaded` | `EventCallback<IReadOnlyList<DocumentOperationResult>>` | Lets the parent refresh |

Behavior contract (research R-07):

- `InputFile multiple` with an `@key` reset.
- At most **10 files per batch** (FR-001). Selecting more is caught (the `GetMultipleFiles(maximumFileCount: 10)` exception is handled, never unhandled), no rows are added, and the "too many files" message is shown.
- Each file gets a row with Title (defaulting to the file name without extension, required, ≤ 200), Category (required select of the 6 values), Description, Tags (comma-separated, ≤ 10 × 50), and Project (from `GetUploadableProjectsAsync`, locked or hidden as described above).
- Client pre-checks for size, empty files, and extension, with messages that list the supported types and the 25 MB limit.
- Files upload sequentially, each with its own progress bar and its own success or error message.
- Each file's content is copied into a `MemoryStream` at the start of its upload processing (on submit, before any other `await` for that file), then the `IBrowserFile` reference is cleared. This is how the constitution's "immediately" is interpreted (plan.md Constitution Check).
- Submit is disabled while required fields are missing, and those fields are highlighted (US1 AS4).

### `Shared/ConfirmDialog.razor`

A modal rendered by Blazor (no Bootstrap JS), with parameters `Visible`, `Title`, `Message`, `ConfirmText`, `OnConfirm`, and `OnCancel`. It's used for delete and for removing a task attachment. On Cancel nothing changes (US4 AS3). It's a shared component created in the Foundational phase.

## User-facing messages (consistent wording)

`DocumentMessages` (tasks.md T019) holds every message below, word for word. `{name}` is the sanitized original file name, and `{title}` is the document title.

### Error codes

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
| `InvalidShareTarget` | "Choose at least one other user, or a team that has members." |
| `Validation` (general) | "Some details are missing or invalid. Check the highlighted fields and try again." |

### Validation details (shown next to the field, with the `Validation` code)

| Rule | Message |
|------|---------|
| Title missing or too long | "Enter a title of 1 to 200 characters." |
| Category missing or invalid | "Choose a category." |
| Description too long | "The description can't be longer than 2,000 characters." |
| Too many tags | "Use at most 10 tags." |
| Tag too long | "Each tag can be at most 50 characters." |
| Search term empty or too long | "Enter a search term of 1 to 100 characters." |
| Too many files in one batch (FR-001) | "You can upload up to 10 files at a time. Select 10 or fewer files." |

### Other messages

| Situation | Message |
|-----------|---------|
| Task not visible (task page, upload or attach to a task the user can't see) | "Task not found or not available." |
| Project task, user isn't a project member, PM, or Administrator (task page note) | "Only members of this task's project can add documents to it." |
| Preview not supported | "Preview isn't available for this file type. Download it instead." |
| Upload succeeded | "'{name}' was uploaded." |
| Details saved | "Document details saved." |
| File replaced | "The file was replaced." |
| Document deleted | "The document was deleted." |
| Shared | "The document was shared." |
| Share removed | "The share was removed." |
| Attached to task | "The document was attached to the task." |
| Removed from task | "The document was removed from the task." |
| Delete confirmation (`ConfirmDialog`) | Title "Delete document?", message "'{title}' and its file will be permanently deleted. This can't be undone.", confirm button "Delete" |
| Remove-from-task confirmation (`ConfirmDialog`) | Title "Remove from task?", message "'{title}' will be removed from this task. The document itself isn't deleted and keeps its project.", confirm button "Remove" |

### Empty states

| Where | Message |
|-------|---------|
| `DocumentList` default, search with no results | "No documents found." |
| My Documents and the dashboard Recent Documents widget | "You haven't uploaded any documents yet." |
| Project Documents card | "No documents have been added to this project yet." |
| Shared with Me | "No documents have been shared with you." |
| Team Documents | "No team documents found." |
| Task attachments | "No documents are attached to this task." |
| Attach-existing picker | "No documents can be attached to this task." |
| Document Reports (any table or the activity log) | "No activity in this date range." |
