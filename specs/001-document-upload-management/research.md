# Phase 0 Research: Document Upload and Management

**Feature**: `001-document-upload-management` | **Date**: 2026-10-16 | **Plan**: [plan.md](plan.md)

Each item below resolves an unknown from the Technical Context, a technology choice, or an integration pattern. Each uses the Decision / Rationale / Alternatives format. No `NEEDS CLARIFICATION` items remain.

---

## R-01 Platform baseline (derived from the codebase)

**Decision**: Build on the existing stack exactly as it is:

| Area | Current setup |
|------|---------------|
| Framework | `net8.0` (nullable and implicit usings on) |
| Web | ASP.NET Core Blazor Server (`AddServerSideBlazor`, `MapBlazorHub`, `_Host.cshtml` fallback) plus Razor Pages (`Login`, `Logout`) |
| Data | EF Core 8.0.0 SqlServer against `(localdb)\mssqllocaldb` / `ContosoDashboard` |
| Auth | Cookie authentication (8 h sliding), four role policies (`Employee`, `TeamLead`, `ProjectManager`, `Administrator`) defined hierarchically in `Program.cs` |
| Services | Scoped, with interface and implementation in one file, taking `ApplicationDbContext` |

No new runtime NuGet packages are needed.

**Rationale**: Constitution Principle IV and the Technology & Platform Constraints require building on the established structure. Everything the feature needs (file I/O, `InputFile`, `FileStreamResult`, EF LINQ) is in the shared framework.

**Alternatives considered**:

- **MVC controllers for file endpoints**: rejected. They would add `AddControllers()` and a new pattern, while the constitution names Razor Pages for HTTP-bound flows.
- **Minimal APIs**: rejected for the same reason.

---

## R-02 Authorization model and the shared access policy

**Decision**: Put all document authorization in `Services/DocumentAccessPolicy.cs`. It's a static class that returns EF-translatable `Expression<Func<Document,bool>>` predicates, built from a `DocumentUserContext` (UserId, Role, Department). Each service call loads that context **from the `Users` table**, not from claims.

- **CanAccess(d)** is true when any of these holds:
  - `d.UploadedByUserId == me`
  - The document's project is visible to me: `d.ProjectId != null` and (`d.Project.ProjectManagerId == me` or `d.Project.ProjectMembers.Any(m => m.UserId == me)`)
  - A share targets me: `d.Shares.Any(s => s.SharedWithUserId == me || (myDept != null && s.SharedWithDepartment == myDept))`
  - I have team oversight: `role >= TeamLead && myDept != null && d.UploadedByUser.Department == myDept && d.UploadedByUser.Role == Employee`
  - `role == Administrator`
- **CanManage(d)** (edit metadata and delete) is true when any of these holds:
  - Uploader
  - Team oversight (as above)
  - `d.Project.ProjectManagerId == me`
  - Administrator
- **Owner-only actions** (replace file, share, remove share): `d.UploadedByUserId == me`. This holds for every role, Administrator included.
- **Upload to a project**: allowed if I'm a project member or the project manager, or for any project if I'm an Administrator.
- **Task visibility**: reuse the existing `ITaskService.GetTaskByIdAsync(taskId, requestingUserId)`. It returns `null` when I'm not the assignee, the creator, a project member, or the project manager. The feature doesn't change the task-visibility rule.
- **Project Manager authority** comes from `Project.ProjectManagerId`, which matches the existing `ProjectService`. It doesn't come from the role name alone. Role inheritance (Project Manager ≥ Team Lead) comes from `UserRole` ordering, so a Project Manager also has team oversight in their department.
- **Response semantics**:
  - If the document doesn't exist **or** isn't accessible, the result is `NotFound`. One message is shown: "Document not found or not available."
  - If the document is accessible but the action isn't permitted (for example, an Employee editing someone else's project document), the result is `Forbidden`.
- **Administrator "Team Documents (all departments)"**: an Administrator's Team Documents view lists documents uploaded by users who hold the Employee role in **any** department, with an optional department filter. Administrators already have access to all documents through their own rule.

**Rationale**:

- Spec FR-022 and FR-034, and the edge cases for department and role changes, require live membership checks. Claims in the cookie (and in a Blazor circuit's cached `AuthenticationState`) go stale for up to 8 hours. The existing Profile page lets users change their own Department, so stale claims would grant or deny the wrong access.
- One predicate composed into the SQL query guarantees that lists, search, the dashboard, project and task panels, and notification recipient selection all agree (FR-033, SC-006). Paging and counts also stay in the database.

**Alternatives considered**:

- **ASP.NET Core resource-based `IAuthorizationHandler`**: it works per object, but it can't filter `IQueryable` lists. You'd still need the predicate, so it adds a second mechanism. Rejected.
- **Checks in Razor components**: UI-only checks violate Principle III. Rejected.
- **Checks from claims**: stale after a department or role change. Rejected.

---

## R-03 Database schema creation and adding new tables

**Decision**:

1. Keep `context.Database.EnsureCreated()`, as the constitution mandates. Add the new `DbSet`s, fluent configuration, and seed data to `ApplicationDbContext`. A fresh database then gets every table, index, check constraint, and seed row automatically.
2. Add a **Development-only schema guard** to `Program.cs` after `EnsureCreated()`:
   1. Probe for the new schema (for example, `await context.Documents.AnyAsync()` inside `try`/`catch (SqlException)`, or a `sys.tables` lookup for `Documents`).
   2. If the probe fails and `app.Environment.IsDevelopment()`, log a **warning** ("Database schema predates the Documents feature; recreating training database"), then call `EnsureDeleted()` and `EnsureCreated()`.
   3. Outside Development, log an **error** with the reset instructions from the README instead, and don't delete anything.
3. Document both the guard and the manual reset in the README: `sqllocaldb stop mssqllocaldb; sqllocaldb delete mssqllocaldb`, or `dotnet ef database drop --force`. Also document that uploaded files under `AppData/uploads` should be deleted together with the database.

**Rationale**:

- `EnsureCreated()` does nothing if the database already exists, so every learner who has run the starter app would get "Invalid object name" errors.
- The training database contains only seed data and practice data, so automatic recreation in Development keeps the Principle I promise (`dotnet run` with no manual setup) at no real cost.
- The existing seed uses `DateTime.UtcNow` in `HasData`. That works with `EnsureCreated()` but would create noisy migrations, which is one more reason not to switch to migrations.

**Alternatives considered**:

- **EF Core migrations**: contradicts the constitution's "created with `EnsureCreated()`" constraint. It needs the `dotnet ef` tooling and can't be mixed with `EnsureCreated()` on the same database. Rejected.
- **Hand-written `CREATE TABLE IF NOT EXISTS` SQL at startup**: duplicates the EF model and is fragile. Rejected.
- **Manual reset only**: violates the zero-setup rule for existing learners. It's kept as documented guidance and isn't the primary path.

---

## R-04 Write ordering, compensation, and transactions

**Decision**: Every operation that touches both files and rows follows a fixed order.

- **Upload**:
  1. Validate the metadata and file rules (R-11).
  2. Authorize the project and task.
  3. Run the malware scan on the in-memory stream (R-08), and reject if the file is flagged or the scan errors.
  4. Generate the storage key `{uploaderId}/{projectId|personal}/{Guid.NewGuid():N}{ext}`.
  5. Call `IFileStorageService.UploadAsync`.
  6. Add the `Document`, its `DocumentTag`s, the optional `TaskDocument`, and the `DocumentActivity` (Upload, plus TaskAttach when relevant), then run **one** `SaveChangesAsync()`.
  7. If step 6 throws, `DeleteAsync(key)` (best effort, logged) and return a failure.
  8. After commit, send notifications. A notification failure is logged and doesn't roll back the upload.
- **Replace (owner only)**:
  1. Validate and scan the new file.
  2. Generate a **new** key and write the new file.
  3. Update `FilePath`, `FileSizeBytes`, `FileType`, `OriginalFileName`, and `LastModifiedDate`, add a `DocumentActivity` (Replace), and save.
  4. On save failure, delete the *new* file and keep the old file and record unchanged.
  5. On success, delete the *old* file (best effort). If that fails, log a warning about an unreachable orphan.
- **Delete (managers)**:
  1. In one `SaveChangesAsync()`, remove the `Document`. Tags, shares, and task attachments go with it through the FK cascade. Add a `DocumentActivity` (Delete).
  2. After commit, `DeleteAsync(oldKey)` (best effort, logged).
- **Metadata edit**: rows only; last write wins (no concurrency token), as the spec's edge case requires.

**Rationale**:

- This satisfies Principle IV ("unique path → write file → save metadata") and FR-011.
- A new key on replace means a failed replacement never corrupts the existing file (spec US4 AS6). Overwriting in place could leave a half-written file.
- Deleting the record before the file means a failure can only leave an *unreachable* file, never a record pointing at a missing file. An unreachable file is never served, because every read goes through a record.

**Alternatives considered**:

- **`TransactionScope` spanning file and DB**: the filesystem isn't transactional. Rejected.
- **Delete the file first, then the record**: can leave a visible record whose download fails. Rejected.
- **Overwrite the file in place on replace**: risks corrupting the content when a write fails partway. Rejected.

---

## R-05 File storage location, key format, and `IFileStorageService` semantics

**Decision**:

- **Root**: configuration `DocumentStorage:RootPath`, default `AppData/uploads`, resolved against `IWebHostEnvironment.ContentRootPath` (that is, `ContosoDashboard/AppData/uploads`). This is outside `wwwroot` and is never mapped by `UseStaticFiles`. Add `ContosoDashboard/AppData/` to `.gitignore`. The directory is created on first write.
- **Key format**: `{uploaderUserId}/{projectId or "personal"}/{guid:N}{.ext}`. The key uses forward slashes, has a lowercase extension taken from the allow-list, and is stored in `Document.FilePath` (`nvarchar(500)`, unique index). The project segment reflects the project **when the file was written** and is informational only. Authorization never reads the key, and keys aren't moved when a document's project changes later (for example, through a task attachment).
- **Interface** (shape required by Principle II and the stakeholder doc):
  - `UploadAsync(Stream content, string storageKey, string contentType)` writes the content at the caller-generated key and returns that key.
  - `DownloadAsync(key)` returns a read-only `Stream`.
  - `DeleteAsync(key)` is idempotent: a missing file isn't an error.
  - `GetUrlAsync(key, expiration)` exists for the future Azure implementation (SAS URL). `LocalFileStorageService` throws `NotSupportedException`, because local files must only be served through the authorization-checked endpoints. No feature code calls it.

  The stakeholder doc's second parameter is called `fileName`. In the contract it's the system-generated **storage key**, never the user's file name (FR-010).
- **Path-safety guard in `LocalFileStorageService`**: reject keys that are rooted or that contain `..`, `\`, or invalid path characters. Combine the key with the root, take `Path.GetFullPath`, and assert that the result starts with the full root path, failing otherwise. Write with `FileMode.CreateNew`, so an existing file is never overwritten.

**Rationale**: This satisfies Principle I (local and outside `wwwroot`), Principle II (relative and portable, so the same string works as an Azure blob name), and Principle III (GUID names and no user-supplied paths). Having the caller generate the key keeps "generate unique path" an explicit, testable step in `DocumentService`.

**Alternatives considered**:

- **Storage service generates the key**: hides the write-order step and makes the key format a storage concern. Rejected.
- **Key without project segment (`{userId}/{guid}`)**: simpler, but the stakeholder guidance recommends the project segment for organization. Kept the recommended format and documented that it's informational.
- **Storing files in the database (`varbinary(max)`)**: violates "local filesystem" in Principle I and the stakeholder constraints. Rejected.

---

## R-06 Download and preview delivery (offline, headers unchanged)

**Decision**:

- **Endpoints**: two thin Razor Pages, `Pages/DocumentDownload.cshtml` at `@page "/documents/{documentId:int}/download"` and `Pages/DocumentPreview.cshtml` at `@page "/documents/{documentId:int}/preview"`.
  - Each PageModel carries `[Authorize]`, reads `NameIdentifier`, and calls `IDocumentService.OpenDocumentContentAsync(documentId, requestingUserId, DocumentContentMode.Download|Preview)`.
  - That call checks access, opens the stream, and records the Download or Preview activity.
  - The endpoint returns a `FileStreamResult`. Both endpoints add `Cache-Control: private, no-store`.
  - **Download**: `File(stream, fileType, fileDownloadName: OriginalFileName)`, which gives `Content-Disposition: attachment` with an RFC 5987-encoded name (FR-017).
  - **Preview**: allowed only for `application/pdf`, `image/jpeg`, and `image/png` (FR-018). It responds with `Content-Disposition: inline` and the **server-derived** MIME (R-11). Requests for other types redirect to `/documents/{id}`, where a Download button is offered.
  - If the document doesn't exist or isn't accessible, both endpoints return 404 with the same generic text.
  - Routing works as for the existing Login and Logout pages: `MapFallbackToPage("/_Host")` registers the Razor Pages data source, and specific page routes win over the fallback. Verify this during implementation. If needed, add `app.MapRazorPages()` explicitly.
- **Preview UX**: open `/documents/{id}/preview` in a **new browser tab** (`target="_blank" rel="noopener"`) for PDFs and images. The browser's built-in PDF viewer and image rendering work offline.
- **Blazor link handling**: Blazor's router intercepts same-origin `<a>` clicks. To avoid that, download links use the `download` attribute (or `NavigationManager.NavigateTo(url, forceLoad: true)`), and preview links use `target="_blank"`. Neither is intercepted.
- **Headers**: no change to the global middleware. `X-Frame-Options: DENY` doesn't affect a top-level tab. CSP `default-src 'self'` allows same-origin content. `nosniff` plus the server-derived MIME stops content-type confusion (for example, HTML renamed to `.png`).

**Rationale**:

- Blazor Server can't stream files over the SignalR circuit efficiently or securely. A normal HTTP GET with the auth cookie is the standard pattern and is required for "protected files served through an authorization-checked endpoint" (Principle III).
- A new tab avoids weakening `X-Frame-Options` or CSP, which an `<iframe>` or `<embed>` preview inside the page would need (Principle III).

**Alternatives considered**:

- **In-page `<iframe>` or `<object>` preview**: needs `X-Frame-Options: SAMEORIGIN` and `frame-src`/`object-src` changes. Rejected.
- **Base64 data URIs through JS interop**: wastes memory, 25 MB through the circuit is slow, and it bypasses the HTTP endpoint pattern. Rejected.
- **PDF.js bundled locally**: a new static dependency for no requirement benefit. Rejected (YAGNI).
- **Inline `<img>` thumbnails on the details page**: would record a Preview activity on every page view and distort the access reports. Rejected.

---

## R-07 Blazor multi-file upload, progress, and memory

**Decision**: Build a `Shared/DocumentUploadForm.razor` component with these behaviors:

- `<InputFile multiple @key="inputKey" OnChange="...">` captures the selected files (`e.GetMultipleFiles(maximumFileCount: 10)`).
- For each file, it first captures `Name`, `Size`, and `ContentType` into a `PendingUpload` row. The row has an editable title (defaulting to the file name without extension, FR-004), category, description, tags, and project.
- It pre-validates on the client side: extension allow-list, `Size == 0`, and `Size > 26,214,400`. Invalid rows are marked immediately and skipped.
- On submit, it processes files **sequentially**:
  1. `OpenReadStream(maxAllowedSize: 26_214_400)`.
  2. Copy into a `MemoryStream` in 81,920-byte chunks, updating a per-file progress bar (`bytesRead / Size`) and calling `StateHasChanged()` about every 5 %.
  3. Set `Position = 0` and clear the `IBrowserFile` reference.
  4. Call `IDocumentService.UploadDocumentAsync(request, memoryStream, userId)`.
- It shows each file's result (success, or a specific error from `DocumentOperationResult`). After a successful batch it increments `inputKey` to reset `InputFile`.
- The service **re-validates** everything, using the stream length rather than the client-reported size, so the UI checks aren't the security boundary.
- The component takes parameters: `FixedProjectId?` (project page), `TaskId?` (task page), and `OnUploaded` callback.

**Rationale**:

- This follows the constitution's Blazor rule and the stakeholder pattern: metadata first, then `MemoryStream`, then clear the reference, then reset `@key`.
- Sequential processing caps server memory at about one file (≤ 25 MB) per circuit.
- Chunked copying produces real progress (FR-007).
- 25 MB over a localhost SignalR connection completes well under 30 s (SC-001).

**Alternatives considered**:

- **Parallel uploads**: up to 10 × 25 MB in memory per user, with no requirement benefit. Rejected.
- **A separate HTTP multipart upload endpoint with JS `fetch` progress**: adds JS and an antiforgery-protected POST endpoint, and departs from the mandated Blazor pattern. Rejected.
- **Stream directly to disk without a `MemoryStream`**: the constitution mandates `MemoryStream`, and the scan must happen before storage. Rejected.

---

## R-08 Simulated malware scanner (EICAR) and fail-closed behavior

**Decision**:

- `IMalwareScanner.ScanAsync(Stream content, string fileName, CancellationToken ct)` returns a `MalwareScanResult` with `Status` (`Clean` | `Infected` | `Error`) and `Detail`. The scanner reads from the current position and restores `Position` afterward.
- `SimulatedEicarMalwareScanner` flags a file as `Infected` when its bytes **contain** the 68-byte standard EICAR test string anywhere. It doesn't require the string to be at offset 0, so a `.txt` file with a leading line is still caught, as the spec's "contains the string" wording requires. Every other file is `Clean`.
- **The signature must not appear as one contiguous literal** in source, binaries, or docs. Antivirus software (Microsoft Defender) quarantines files that contain it, which would break the build or the repo. Build it at runtime from two halves, or from a Base64 constant decoded at startup.
- **Fail closed**: `DocumentService` treats `Infected` *and* `Error`, as well as any exception or timeout (a `CancellationTokenSource` of about 10 s), as a rejection. The user sees "rejected for security reasons" or "could not be scanned, try again later", and nothing is written.
- The scan runs on the in-memory stream **before** `IFileStorageService.UploadAsync`, so flagged content never reaches disk.
- DI registration in `Program.cs`: `AddSingleton<IMalwareScanner, SimulatedEicarMalwareScanner>()`. A real scanner (for example, an AMSI- or ClamAV-based one) can replace it with a one-line DI change (FR-008).
- README "Known Limitations" gains: *simulated scanner detects only the EICAR test file; not a production malware control* (FR-038, Principle V).

**Rationale**: This matches the clarification (session 2026-10-16). It works offline, is deterministic for verification, and is replaceable.

**Alternatives considered**:

- **Microsoft Defender via AMSI or `MpCmdRun`**: not portable, needs Windows-specific interop, and behaves differently between machines. Rejected for the training build, though it's a valid future implementation.
- **ClamAV**: needs an install and signature downloads, which isn't offline-friendly. Rejected.
- **Magic-byte content sniffing**: not malware detection, and type is decided by extension per the spec. Rejected.

**Verification caveat** (carried into quickstart.md and the risks list): Defender real-time protection may quarantine an EICAR test file as soon as it's created on disk. Mitigations, in order:

1. Create the file with a leading text line before the signature. Many engines only flag the canonical file, but the simulated scanner still flags it.
2. Use the automated service test, which feeds the bytes from memory.
3. Use a temporary Defender folder exclusion where policy allows.

---

## R-09 Claims required by the feature

**Decision**: Add `new Claim("Department", user.Department)` in `Pages/Login.cshtml.cs` when `Department` isn't null or empty. Login then issues NameIdentifier, Name, Email, Role, and Department, as Principle III requires. Pages use claims only for **display and UI hints**, for example showing the Team Documents nav item through `AuthorizeView Policy="TeamLead"`. **Services make every authorization decision from the database** (R-02).

**Rationale**: The constitution requires the claim. The spec requires live checks. Because the Profile page can change a user's department mid-session, claims alone would be wrong.

**Alternatives considered**: Refreshing the cookie on profile save. That adds sign-in complexity and still doesn't fix other users' circuits. Rejected.

---

## R-10 Search, filtering, sorting, and paging

**Decision**:

- **Search** happens server-side in `IDocumentService.SearchDocumentsAsync`:
  1. Trim the term and require 1–100 characters.
  2. Apply `DocumentAccessPolicy.CanAccess`, **then** an OR of `Title.Contains(t)`, `Description.Contains(t)`, `Tags.Any(x => x.Tag.Contains(t))`, `UploadedByUser.DisplayName.Contains(t)`, and `Project.Name.Contains(t)`.
  3. EF Core 8 translates `Contains` on a parameter to a SQL Server pattern match that **doesn't** treat user input as wildcards, so no escaping is needed. The default LocalDB collation (`SQL_Latin1_General_CP1_CI_AS`) makes the match case-insensitive.
- **Lists** (My, Project, Team, Shared, Search) all take a `DocumentQuery`:
  - Filters: `Category?`, `ProjectId?`, `FromDate?`, and `ToDate?` (inclusive day range in UTC), combined with AND (FR-014).
  - Sort: `SortBy` ∈ {Title, UploadedDate, Category, FileSize} and `Descending` (FR-013), with a stable `ThenBy(DocumentId)`.
  - Paging: `Page` and `PageSize` (default 25, max 100), returning `PagedResult<DocumentSummary>` with `TotalCount`.
- **Projection**: queries project directly to `DocumentSummary` DTOs (`Select`). That avoids N+1 problems and loading large graphs.
- **Indexes** (data-model.md) cover uploader + date, project, category, share targets, and tags.

**Rationale**: At the target scale (≤ ~500 accessible documents per user), indexed predicates plus `LIKE`/`CHARINDEX` scans comfortably meet the 2 s target (SC-002, SC-003). Composing the access predicate first guarantees that results contain only accessible documents (FR-016).

**Alternatives considered**:

- **SQL Server Full-Text Search**: **not supported by LocalDB**, and it needs catalog setup. Rejected.
- **In-memory filtering**: breaks paging and performance. Rejected.
- **Lucene.NET**: a new dependency and index-sync complexity. Rejected (YAGNI).
- **Tags as one delimited string column**: harder to search exactly, de-duplicate, and enforce the 10-tag and 50-character limits. A `DocumentTag` table was chosen instead.

---

## R-11 File validation rules and MIME handling

**Decision**: `Services/DocumentFileRules.cs` (static) holds these rules:

- **Allow-list and canonical MIME** (case-insensitive extension → stored `FileType`):

  | Extension | MIME |
  |-----------|------|
  | `.pdf` | `application/pdf` |
  | `.doc` | `application/msword` |
  | `.docx` | `application/vnd.openxmlformats-officedocument.wordprocessingml.document` |
  | `.xls` | `application/vnd.ms-excel` |
  | `.xlsx` | `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` |
  | `.ppt` | `application/vnd.ms-powerpoint` |
  | `.pptx` | `application/vnd.openxmlformats-officedocument.presentationml.presentation` |
  | `.txt` | `text/plain` |
  | `.jpg`, `.jpeg` | `image/jpeg` |
  | `.png` | `image/png` |

- **Previewable**: `application/pdf`, `image/jpeg`, `image/png`.
- **Size**: `MaxFileSizeBytes = 25 * 1024 * 1024 = 26,214,400`. Exactly 26,214,400 bytes is accepted, and 26,214,401 is rejected. Zero bytes is rejected.
- **Extension check**: use `Path.GetExtension` of the sanitized name. A missing extension is rejected. `report.pdf.exe` has extension `.exe` and is rejected.
- **`SanitizeFileName`**:
  1. Take `Path.GetFileName` (drops path segments).
  2. Replace `Path.GetInvalidFileNameChars()` and control characters with `_`.
  3. Collapse whitespace and trim.
  4. Truncate to 255 characters while keeping the extension.
  5. Fall back to `document{ext}` if the result is empty.

  The result is used **only** for display and download (`OriginalFileName`).
- **Metadata limits** (FR-004, FR-005):
  - Title: required, 1–200 characters after trimming.
  - Description: ≤ 2,000 characters.
  - Category: must be one of the six fixed names.
  - Tags: trimmed, empty ones dropped, de-duplicated with `OrdinalIgnoreCase`, at most 10, each ≤ 50 characters.

**Rationale**:

- The stored `FileType` is derived from the allow-listed extension, **not** the browser-reported `ContentType`. Browsers send inconsistent or empty types, and serving a client-controlled type inline could enable content-type confusion. This also gives the reports consistent type groupings.
- The column stays `nvarchar(255)`, as the constitution requires.

**Alternatives considered**:

- **Trust the browser `ContentType`**: unreliable and unsafe for inline preview. Rejected.
- **Magic-number validation**: the spec decides type by extension. It would be an additive hardening for later. Rejected (YAGNI).

---

## R-12 Notifications

**Decision**:

- **Model changes**: append `DocumentShared` and `DocumentAdded` to `NotificationType`. Appending keeps the existing integer values. Add a nullable `DocumentId int?` to `Notification` **without** a foreign key, so the notification survives document deletion.
- **`INotificationService.CreateNotificationsAsync(IEnumerable<Notification>)`**: batch insert that **skips recipients whose `InAppNotificationsEnabled == false`** (FR-028). The existing single-create method stays unchanged.
- **Share notifications**: send to the target user, or to the department's current members, excluding the sharer. No notification goes to a user who already had an identical share (US5 AS6), or to the sharer for a self-share or own-team share.
- **Project-added notifications**: when a document is newly associated with a project (an upload with a project, or a task attachment that sets the project), send to the project manager and all project members except the actor.
- **Content**: `Title` = "Document shared with you" or "New project document". `Message` names the document title, the project or sharer, and their display name. Notifications only go to users who can access the document at send time.
- **`Notifications.razor`**: shows an "Open document" link to `/documents/{DocumentId}` when one is set. The details page re-checks access and shows "Document not found or no longer available" after deletion or revocation.

**Rationale**:

- This meets FR-026, FR-027, and FR-028 and the spec's deletion edge case.
- Because `DocumentId` has no FK, it never blocks deletes or cascades.
- Notification text is a snapshot sent to an authorized recipient. The link re-authorizes on every use.

**Alternatives considered**:

- **A separate `DocumentNotification` table**: duplicates the existing notification UI. Rejected.
- **Storing a URL string**: less type-safe, and it can't be validated. Rejected.

---

## R-13 Activity log and Administrator reports

**Decision**:

- **`DocumentActivity` row** for each successful action (FR-035): Upload, Download, Preview, MetadataEdit, Replace, Delete, Share, ShareRemoved, TaskAttach, TaskDetach.
  - Fields: `UserId` (FK, Restrict), `DocumentId` (**no FK**, so records survive deletion, FR-036), `DocumentTitle` snapshot, `FileType` snapshot, `TaskId?`, `Details` (for example, "Shared with department Marketing"), and `OccurredAt` (UTC).
  - The row is written in the **same `SaveChangesAsync()`** as the action it records. Download and Preview are written just before the stream is returned.
- **`IDocumentReportService`** is Administrator-only. It checks the live role from the database and returns `Forbidden` otherwise. The page also uses `Policy = "Administrator"`. It provides:
  - **Most uploaded document types**: `GroupBy(FileType)` over Upload activities in the range, ordered by count. Display names come from `DocumentFileRules`, for example "PDF" or "Word".
  - **Most active uploaders**: `GroupBy(UserId)` over Upload activities, joined to `Users.DisplayName`, ordered by count.
  - **Access patterns**: the top 10 documents by Download plus Preview count in the range (title from the latest snapshot), and access counts per UTC day split by Download and Preview.
  - **Activity log**: a paged, filterable list for audit, restricted to Administrators.
- **Date range**: inclusive UTC dates, labeled "(UTC)" in the UI. The existing app stores and shows UTC.

**Rationale**: Snapshots and the FK-less `DocumentId` keep audit history after deletion. Writing in the same transaction guarantees SC-008 (100% of actions are recorded).

**Alternatives considered**:

- **Logging to `ILogger` or files**: not queryable for reports. Rejected.
- **Temporal tables or CDC**: heavyweight, and LocalDB support varies. Rejected.

---

## R-14 Seed data needed for verification

**Decision**: Extend `SeedData` in `ApplicationDbContext`, keeping the existing IDs 1–4 unchanged.

| Id | User | Role | Department | Purpose |
|----|------|------|------------|---------|
| 5 | Dana Whitfield (`dana.whitfield@contoso.com`) | Employee | Engineering | Same department as Ni Kang and Floris, **not** on any project. Covers "different Employee is denied", the same-department team-share recipient, and non-member project denial. |
| 6 | Mateo Alvarez (`mateo.alvarez@contoso.com`) | Employee | Marketing | Other-department Employee and member of Project 2. Covers the cross-department share and Team Lead out-of-department denial. |
| 7 | Hana Sato (`hana.sato@contoso.com`) | TeamLead | Marketing | Second department's Team Lead. Covers the department-move oversight transfer (US8 AS7). |
| 8 | Rafael Costa (`rafael.costa@contoso.com`) | ProjectManager | Marketing | Manages Project 2. Covers "Project Manager of A can't manage B" (Camille vs. Project 2). |

- **Project 2**: "Marketing Website Refresh", PM = 8, Active. **ProjectMember 3**: Project 2 / user 6.
- **Task 4**: "Prepare onboarding checklist". No project, assigned to 4, created by 3 (covers the no-project task, US6 AS5).
- **Task 5**: "Draft launch copy". Project 2, assigned to 6, created by 8.
- **No documents are seeded.** The quickstart creates them through the UI, which exercises the real write path.
- README "Sample Data" is updated to match.

**Rationale**: The existing seed (one non-IT department, one project, no Employee outside projects) can't express the required negative cases (SC-006) or US8 AS7. Constitution Principle IV puts seed data in `Data/`.

**Alternatives considered**: Creating users manually in the quickstart. The app has no user-creation UI, so this was rejected.

---

## R-15 Task detail view

**Decision**: Add `Pages/TaskDetails.razor` at `/tasks/{taskId:int}` with `[Authorize]`.

- It loads the task through `ITaskService.GetTaskByIdAsync(taskId, userId)`, which already authorizes. If that returns `null`, the page shows "Task not found or not available".
- It shows the task fields and an **Attachments** panel. The panel lists attachments through `ITaskDocumentService.GetTaskDocumentsAsync`, filtered to documents the viewer can access (FR-029). It has three actions:
  - **Upload to task**: `DocumentUploadForm` with `TaskId`.
  - **Attach existing**: a picker of accessible documents from `GetAttachableDocumentsAsync`, which pre-filters by the FR-030 rules.
  - **Remove**: shown only when `CanDetach` is true.
- `Tasks.razor`'s `ViewTaskDetails` TODO navigates to `/tasks/{id}`, and Project Details task rows link there.

**Rationale**: The spec requires a task detail view, and none exists. Reusing `GetTaskByIdAsync` keeps the task-visibility rule in one place.

**Alternatives considered**: A modal on `Tasks.razor`. It only lists tasks assigned to the user, so project members couldn't reach other members' tasks. Rejected.

---

## R-16 Testing strategy

**Decision**:

- **Required**: quickstart.md manual scenarios using the seeded users, with at least one negative authorization case per story (constitution verification gate).
- **Planned, optional**: `ContosoDashboard.Tests` (xUnit), with these characteristics:
  - A `LocalDbFixture` creates `ContosoDashboard_Tests_{guid}` on `(localdb)\mssqllocaldb` with `EnsureCreated()` and drops it on dispose.
  - Services are constructed directly with real `LocalFileStorageService` (temp root) and `SimulatedEicarMalwareScanner`, plus a fake `IMalwareScanner` that throws to test fail-closed behavior.
  - The suites cover: (a) a data-driven role × operation authorization matrix (SC-006); (b) write-order and compensation rules (SC-007, FR-011); (c) 500-document list and search timing under 2 s (SC-002, SC-003).
- **Build gate**: `dotnet build` with no new warnings (constitution).

**Rationale**: Faithful LocalDB semantics (collation, filtered unique indexes, check constraints, cascades) matter for the rules being tested.

**Alternatives considered**:

- **EF InMemory provider**: no relational constraints, and case-sensitive `Contains`. Rejected.
- **SQLite**: different collation and `CHARINDEX` behavior, and SQL Server-specific index filters. Rejected.
- **bUnit UI tests**: low value compared with service tests, plus a new dependency. Rejected.

---

## R-17 Smaller decisions

| Topic | Decision | Rationale |
|-------|----------|-----------|
| Concurrency | Last write wins, with no rowversion | Spec edge case explicitly accepts it. YAGNI. |
| Timestamps | `DateTime.UtcNow` stored as `datetime2`. UI shows them in the existing app's format. | Consistent with existing entities. |
| Category storage | `nvarchar(50)` text, validated against `DocumentCategories.All` | Stakeholder constraint and Principle IV. |
| Delete confirmation | `Shared/ConfirmDialog.razor`, rendered by Blazor (no Bootstrap JS dependency) | Works offline. Matches existing pages, which use no JS interop. |
| Page size | 25 rows, with server-side paging | Meets "longer lists split into pages" and keeps renders fast. |
| Department list for team sharing | Distinct non-empty `Users.Department` values | No team entity, per spec assumption. |
| Users without a department | Can't be targeted by a team share. A Team Lead without a department sees an empty Team Documents view. | Spec edge case. |
| Attach rules for documents with no project | Owner or `CanManage` only. A document in a different project is refused. For a task with no project, the document's project is unchanged. | Clarification of 2026-10-16 and FR-030. |
| Detach rules | The attacher or `CanManage`. The document keeps its project. Detach is logged. | Clarification of 2026-10-16 and FR-030a. |
