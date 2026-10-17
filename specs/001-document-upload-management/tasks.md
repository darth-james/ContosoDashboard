---

description: "Task list for Document Upload and Management"
---

# Tasks: Document Upload and Management

**Input**: Design documents from `specs\001-document-upload-management\`

**Prerequisites**: plan.md, spec.md (9 user stories, P1–P3, clarified 2026-10-16), research.md (R-01–R-17), data-model.md, contracts\ (infrastructure-interfaces.md, service-interfaces.md, http-endpoints.md, ui-routes.md), quickstart.md, `.specify\memory\constitution.md`

**Tests**: **INCLUDED** (the user asked for them). This adds the optional `ContosoDashboard.Tests` xUnit project from plan.md and research R-16. Tests run against a throwaway LocalDB database, not EF InMemory or SQLite. They cover:

- The per-role authorization denial matrix (SC-006): one boundary denial test per restricted Permissions-table cell, and a positive test per full-grant cell
- File-then-record write order and compensation (FR-011, SC-007)
- Malware-scan rejection using **in-memory** EICAR bytes (FR-008)
- Timing for 500-document lists and search (SC-002, SC-003), a 25 MB upload (SC-001), and opening a preview (SC-004)

**Stub-first rule (mandatory, constitution build gate)**: before writing a story's tests, add the interface and service members those tests call, with `throw new NotImplementedException()` bodies, so the solution builds. Each phase has an explicit stub task for this, listed first in its Tests section (T132 for Foundational, then one per story). Tests that call stubbed members then fail **at runtime** (red), never at compile time. Tests that exercise behavior already implemented by earlier tasks or stories (for example visibility rules, finished US4 methods, or unsupported-type checks) may pass as soon as they are written; that is expected and does not block the task. A test task is done only when the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`, which also builds `ContosoDashboard`) with no new errors or warnings, and every new test that calls a stubbed member fails at runtime. The implementation tasks then replace the stub bodies and turn the tests green.

**Story traits**: every test method carries `[Trait("Story", "<id>")]`, where `<id>` is `Foundation`, `US1`–`US9`, or `Polish`. Put the trait on each **method** (not the class) in classes that span stories: the partial classes `DocumentAuthorizationMatrixTests` and `DocumentWriteOrderTests`, and `DocumentPerformanceTests` (extended in Polish). A class-level trait is fine for single-story classes. Verification tasks filter by trait (`dotnet test ContosoDashboard.Tests --filter "Story=US2"`). They run the story's own trait plus `Foundation` and the traits of the story's hard dependencies, so red tests of a story still in progress never block the verification of another story. Only T130 runs the unfiltered suite.

**Task IDs**: IDs are stable identifiers, not a sequence. Tasks added after the first draft (T132 onward) and moved tasks (T083) keep their IDs, so execution order follows **document order**, not numeric order.

**Organization**: Tasks are grouped by user story, in priority order. Every path is relative to the repository root `C:\TrainingProjects\ContosoDashboard\`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task). Test tasks marked [P] can run in parallel with each other once their phase's stub task is done.
- **[Story]**: The user story the task belongs to (US1–US9). Setup, Foundational, and Polish tasks have no story label.

## Path Conventions

- Web app (single existing project): `ContosoDashboard\Models\`, `ContosoDashboard\Data\`, `ContosoDashboard\Services\`, `ContosoDashboard\Pages\`, `ContosoDashboard\Shared\`
- Test project (sibling): `ContosoDashboard.Tests\`
- New services keep the existing convention of putting the interface and implementation in one file (for example `TaskService.cs`). Infrastructure interfaces get their own files.
- `IDocumentService` grows **story by story**. Each story adds only its own members to the interface and the implementation in `ContosoDashboard\Services\DocumentService.cs`.

## Cross-cutting rules (apply to every task)

- **Authorization**: every service method takes `requestingUserId` and loads the requester's **live** `Role` and `Department` from `Users` (R-02). Never use claims for authorization decisions. If a document doesn't exist or isn't accessible, the result is `NotFound` with the message "Document not found or not available." If it's accessible but the action isn't permitted, the result is `Forbidden`.
- **Access predicates**: lists, search, the dashboard, project and task panels, and notification recipients MUST compose `DocumentAccessPolicy` predicates into the SQL query. Never filter in memory.
- **Pages**: pages call `I*Service` interfaces only and never inject `ApplicationDbContext` (Principle IV). Every new page has `[Authorize]` or a policy attribute.
- **Messages**: use the user-facing wording from contracts\ui-routes.md "User-facing messages" (error codes, validation details, other messages, and empty states), word for word, through `DocumentMessages` (T019). Don't invent new wording in pages or services.
- **Write order**: generate a unique key, write the file, then save the record. Compensate on failure (R-04). If a best-effort file cleanup fails, log a warning with the key; the leftover file is never served (spec FR-011).
- **Headers**: don't change the security-header middleware in `Program.cs`.
- **EICAR string**: never write it as one contiguous literal in source, tests, or docs. Build it at runtime from two halves (R-08).
- **Commits**: don't commit as part of these tasks unless the team decides to.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Repository configuration and the empty test project

- [ ] T001 Add the line `ContosoDashboard/AppData/` to `.gitignore` (repository root), under a `# Document uploads (runtime only)` comment, so uploaded files are never committed (R-05)
- [ ] T002 [P] Add `"DocumentStorage": { "RootPath": "AppData/uploads" }` as a top-level section in `ContosoDashboard\appsettings.json` (contracts\infrastructure-interfaces.md, "Configuration contract")
- [ ] T003 [P] Create the xUnit test project `ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`:
  - `net8.0`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<IsPackable>false</IsPackable>`
  - Test-time-only `PackageReference`s: `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`
  - `<ProjectReference Include="..\ContosoDashboard\ContosoDashboard.csproj" />`
  - Don't change `ContosoDashboard\ContosoDashboard.csproj`. No new runtime packages.
- [ ] T004 Add `ContosoDashboard.Tests\xunit.runner.json` with `{ "parallelizeTestCollections": false }` so timing tests don't compete with other tests. Include it in `ContosoDashboard.Tests\ContosoDashboard.Tests.csproj` as `<None Update="xunit.runner.json" CopyToOutputDirectory="PreserveNewest" />`. (Depends on T003.)
- [ ] T005 Run `dotnet build ContosoDashboard\ContosoDashboard.csproj` and `dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`. Both must succeed. Note the current warning count for `ContosoDashboard` as the "no new warnings" baseline that the constitution build gate checks in T129.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Everything every story depends on:

- The full schema. All five entities are created here because `EnsureCreated()` builds the whole schema in one step, and the Development schema guard recreates the database only once.
- Seed users
- The Department claim
- Infrastructure interfaces and their implementations
- File rules
- Result types
- The shared access policy
- Batch notifications
- DI wiring
- Razor Pages routing
- The shared `ConfirmDialog` component (used by US4 and US6)
- The LocalDB test harness

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### 2a. Data model and schema

- [ ] T006 [P] Create entity `ContosoDashboard\Models\Document.cs` (data-model.md "Document"), using `[Key]`, `[Required]`, and `[MaxLength]` annotations like the existing models:
  - **Fields** (constraints quoted from data-model.md):
    - `DocumentId` (`int` PK, identity)
    - `Title` (`nvarchar(200)` required, "Trimmed, 1–200 characters")
    - `Description` (`nvarchar(2000)` null, "≤ 2,000 characters")
    - `Category` (`nvarchar(50)` required, "One of `DocumentCategories.All`, stored as the readable text")
    - `ProjectId` (`int` null, FK → `Projects`)
    - `UploadedByUserId` (`int` required, FK → `Users`, "Ownership never transfers")
    - `UploadedDate` (`datetime2` required, "Set by the server, UTC")
    - `LastModifiedDate` (`datetime2` required)
    - `OriginalFileName` (`nvarchar(255)` required, "Never used for storage paths")
    - `FileSizeBytes` (`bigint` required, "1 to 26,214,400")
    - `FileType` (`nvarchar(255)` required, "Canonical MIME derived from the allow-listed extension")
    - `FilePath` (`nvarchar(500)` required, **unique**, "Relative, portable storage key `{uploaderId}/{projectId|personal}/{guid:N}{ext}`")
  - **Virtual navigations**: `UploadedByUser`, `Project?`, `Tags`, `Shares`, `TaskAttachments`
- [ ] T007 [P] Create entity `ContosoDashboard\Models\DocumentTag.cs`:
  - Fields: `DocumentTagId` (`int` PK), `DocumentId` (`int` FK), `Tag` (`[Required][MaxLength(50)]`, "Trimmed and non-empty. Duplicates on the same document are ignored, compared case-insensitively")
  - Virtual navigation: `Document`
- [ ] T008 [P] Create entity `ContosoDashboard\Models\DocumentShare.cs`:
  - Fields: `DocumentShareId` (`int` PK), `DocumentId` (`int` FK), `SharedByUserId` (`int` FK, "Always the document owner"), `SharedWithUserId` (`int?`, "Set for a user share"), `SharedWithDepartment` (`[MaxLength(100)]` nullable, "Set for a team share. Matches `User.Department`, which is also 100"), `SharedDate` (`DateTime`, UTC)
  - Virtual navigations: `Document`, `SharedByUser`, `SharedWithUser?`
- [ ] T009 [P] Create entity `ContosoDashboard\Models\TaskDocument.cs`:
  - Fields: `TaskDocumentId` (`int` PK), `TaskId` (`int` FK → `Tasks`), `DocumentId` (`int` FK → `Documents`), `AttachedByUserId` (`int` FK → `Users`, "Used by the detach rule (FR-030a)"), `AttachedDate` (`DateTime`, UTC)
  - Virtual navigations: `Task` (`TaskItem`), `Document`, `AttachedByUser`
- [ ] T010 [P] Create `ContosoDashboard\Models\DocumentActivity.cs`:
  - Entity `DocumentActivity` fields:
    - `DocumentActivityId` (`int` PK)
    - `DocumentId` (`int` required, **no FK**, "Survives document deletion (FR-036)")
    - `DocumentTitle` (`[Required][MaxLength(200)]`, "Title snapshot at the time of the action")
    - `FileType` (`[MaxLength(255)]` nullable, "MIME snapshot (Upload and Replace)")
    - `UserId` (`int` FK → `Users`)
    - `Action` (`DocumentActivityAction`)
    - `TaskId` (`int?`, no FK, "Required for TaskAttach and TaskDetach")
    - `Details` (`[MaxLength(500)]` nullable)
    - `OccurredAt` (`DateTime`, UTC)
    - Virtual navigation `User`
  - Enum `DocumentActivityAction { Upload = 0, Download = 1, Preview = 2, MetadataEdit = 3, Replace = 4, Delete = 5, Share = 6, ShareRemoved = 7, TaskAttach = 8, TaskDetach = 9 }`
- [ ] T011 [P] Create static class `ContosoDashboard\Models\DocumentCategories.cs` with `public static readonly IReadOnlyList<string> All = ["Project Documents", "Team Resources", "Personal Files", "Reports", "Presentations", "Other"];` and `public static bool IsValid(string? value)`, which compares by ordinal (data-model.md "DocumentCategories")
- [ ] T012 [P] Modify `ContosoDashboard\Models\Notification.cs`:
  - Add `public int? DocumentId { get; set; }` (no FK, no navigation).
  - **Append** `DocumentShared` and `DocumentAdded` to the end of `NotificationType`. Existing integer values must not change (R-12).
- [ ] T013 [P] Add optional inverse navigations `public virtual ICollection<Document> Documents { get; set; } = new List<Document>();` to `ContosoDashboard\Models\Project.cs`, and `public virtual ICollection<TaskDocument> DocumentAttachments { get; set; } = new List<TaskDocument>();` to `ContosoDashboard\Models\TaskItem.cs`. Make no column changes.
- [ ] T014 Configure the new entities in `ContosoDashboard\Data\ApplicationDbContext.cs` (data-model.md). Depends on T006–T013.
  - **DbSets**: `Documents`, `DocumentTags`, `DocumentShares`, `TaskDocuments`, `DocumentActivities`
  - **Relationships and delete behavior**:
    - `Restrict`: `User→Document` (UploadedByUserId), `Project→Document` (ProjectId), `User→DocumentShare` (SharedByUserId and SharedWithUserId), `User→TaskDocument` (AttachedByUserId), `User→DocumentActivity` (UserId)
    - `Cascade`: `Document→DocumentTag`, `Document→DocumentShare`, `Document→TaskDocument`, `TaskItem→TaskDocument`
    - **No relationship** for `DocumentActivity.DocumentId`, `DocumentActivity.TaskId`, or `Notification.DocumentId`
  - **Indexes**:
    - `Documents`:
      - `IX_Documents_UploadedByUserId_UploadedDate` on (`UploadedByUserId`, `UploadedDate` descending)
      - `IX_Documents_ProjectId`
      - `IX_Documents_Category`
      - `IX_Documents_UploadedDate`
      - Unique `IX_Documents_FilePath`
    - `DocumentTags`: unique (`DocumentId`, `Tag`) and `Tag`
    - `DocumentShares`:
      - Unique filtered (`DocumentId`, `SharedWithUserId`) `WHERE [SharedWithUserId] IS NOT NULL`
      - Unique filtered (`DocumentId`, `SharedWithDepartment`) `WHERE [SharedWithDepartment] IS NOT NULL`
      - `SharedWithUserId`
      - `SharedWithDepartment`
    - `TaskDocuments`: unique (`TaskId`, `DocumentId`) and `DocumentId`
    - `DocumentActivities`: `OccurredAt`, (`Action`, `OccurredAt`), `DocumentId`, `UserId`
  - **Check constraint** `CK_DocumentShares_SingleTarget`, verbatim: `([SharedWithUserId] IS NOT NULL AND [SharedWithDepartment] IS NULL) OR ([SharedWithUserId] IS NULL AND [SharedWithDepartment] IS NOT NULL)`
- [ ] T015 Extend `SeedData` in `ContosoDashboard\Data\ApplicationDbContext.cs` (R-14; keep IDs 1–4 unchanged). Depends on T014 (same file).
  - **Users** (all with `InAppNotificationsEnabled = true`):
    - 5 Dana Whitfield (`dana.whitfield@contoso.com`, Employee, Engineering)
    - 6 Mateo Alvarez (`mateo.alvarez@contoso.com`, Employee, Marketing)
    - 7 Hana Sato (`hana.sato@contoso.com`, TeamLead, Marketing)
    - 8 Rafael Costa (`rafael.costa@contoso.com`, ProjectManager, Marketing)
  - **Project** 2: "Marketing Website Refresh" (ProjectManagerId 8, Active)
  - **ProjectMember** 3: Project 2, User 6, "Developer"
  - **TaskItem** 4: "Prepare onboarding checklist" (no project, assignee 4, creator 3)
  - **TaskItem** 5: "Draft launch copy" (Project 2, assignee 6, creator 8)
  - Don't seed any documents.
- [ ] T016 Add the **Development-only schema guard** in `ContosoDashboard\Program.cs`, inside the existing database-initialization `try` block, right after `context.Database.EnsureCreated()` (R-03):
  1. Probe the new schema with a query that touches each new table and the new `Notifications.DocumentId` column (for example, `context.Documents.Any()`, `context.DocumentTags.Any()`, `context.DocumentShares.Any()`, `context.TaskDocuments.Any()`, `context.DocumentActivities.Any()`, `context.Notifications.Select(n => n.DocumentId).Any()`), wrapped in `catch (SqlException)`.
  2. On failure **in Development**, log the warning "Database schema predates the Documents feature; recreating training database", then call `EnsureDeleted()` followed by `EnsureCreated()`. Then **clear the uploads folder**, because no `Documents` row survives the recreate: resolve `builder.Configuration["DocumentStorage:RootPath"]` (default `AppData/uploads`) against `app.Environment.ContentRootPath`, and only if the folder exists, is inside the content root, and isn't under `wwwroot`, delete it recursively and log the warning "Cleared uploaded files in {path} because the training database was recreated". A failure to clear is logged as a warning and doesn't stop startup.
  3. **Outside Development**, log an error that points to the README reset instructions, and don't delete anything (neither the database nor any files).

### 2b. Infrastructure and service contracts

- [ ] T017 [P] Create `ContosoDashboard\Services\IFileStorageService.cs` exactly as in contracts\infrastructure-interfaces.md: `UploadAsync(Stream fileStream, string storageKey, string contentType)` returns `Task<string>`, `DeleteAsync(string storageKey)`, `DownloadAsync(string storageKey)` returns `Task<Stream>`, and `GetUrlAsync(string storageKey, TimeSpan expiration)` returns `Task<string>`. Include XML docs stating that keys are caller-generated, forward-slash, relative, and never derived from user file names.
- [ ] T018 [P] Create `ContosoDashboard\Services\IMalwareScanner.cs` with `enum MalwareScanStatus { Clean, Infected, Error }`, `sealed record MalwareScanResult(MalwareScanStatus Status, string? Detail = null)`, and `IMalwareScanner.ScanAsync(Stream content, string fileName, CancellationToken cancellationToken = default)`. XML docs must say that the scanner reads from the current position, MUST restore `Position`, and MUST NOT throw for infected content.
- [ ] T019 [P] Create `ContosoDashboard\Services\DocumentModels.cs` with the shared types from contracts\service-interfaces.md, "Shared types":
  - `DocumentErrorCode`
  - `DocumentOperationResult` with the `Ok` and `Fail` factories
  - `DocumentSortField`
  - `DocumentQuery`: `PageSize` clamped to 1..100, `Page` ≥ 1
  - `PagedResult<T>`
  - `DocumentSummary`
  - `SharedDocumentSummary`
  - `DocumentDetails`
  - `DocumentUploadRequest`
  - `DocumentMetadataUpdate`
  - `DocumentContentMode`
  - `DocumentContentResult`
  - `ProjectOption`
  - A static `DocumentMessages` class with one method or constant for **every** entry in the contracts\ui-routes.md "User-facing messages" tables, word for word:
    - **Error codes**: `FileTooLarge(name)` → "'{name}' is larger than the 25 MB limit.", `FileEmpty(name)`, `UnsupportedFileType(name)`, `MalwareDetected(name)`, `ScanFailed(name)`, `NotFound`, `Forbidden`, `CrossProject`, `StorageFailed`, `InvalidShareTarget`, and the general `Validation` message
    - **Validation details**: title, category, description, too many tags, tag too long, search term, and too many files in one batch ("You can upload up to 10 files at a time. Select 10 or fewer files.")
    - **Other messages**: "Task not found or not available.", the non-member task note, the preview-not-available note, the success messages (uploaded, details saved, file replaced, deleted, shared, share removed, attached to task, removed from task), and the delete and remove-from-task confirmation titles, messages, and button texts
    - **Empty states**: every entry in the "Empty states" table
    - A `ForCode(DocumentErrorCode code, string? name)` helper that maps each error code to its message

  Share, task, and report records go in their own service files later.

### 2c. Test harness and foundational tests (write first; stub-dependent tests fail at runtime before 2d)

- [ ] T132 Add compile-only stubs under `ContosoDashboard\Services\` for every 2d member that the 2c tests call, so the solution builds before any test is written (stub-first rule). Every method body is `throw new NotImplementedException()`; constants and records are real.
  - `ContosoDashboard\Services\LocalFileStorageService.cs`: `DocumentStorageOptions` (real) and `LocalFileStorageService : IFileStorageService` with its constructors and stubbed members
  - `ContosoDashboard\Services\SimulatedEicarMalwareScanner.cs`: `SimulatedEicarMalwareScanner : IMalwareScanner` with a stubbed `ScanAsync`
  - `ContosoDashboard\Services\DocumentFileRules.cs`: the static class with `MaxFileSizeBytes` (real) and stubbed `IsPreviewable`, `TryGetFileType`, `ValidateFile`, `SanitizeFileName`, `ValidateMetadata`, `NormalizeTags`, `GetDisplayName`, and `SupportedTypesDescription`
  - `ContosoDashboard\Services\DocumentAccessPolicy.cs`: `DocumentUserContext` (real) and stubbed `LoadUserContextAsync`, `CanAccess`, `CanManage`, `IsOwner`, `IsTeamOversight`, and `CanUploadToProject`
  - `ContosoDashboard\Services\NotificationService.cs`: `CreateNotificationsAsync` added to `INotificationService` and stubbed in `NotificationService`

  **Done when** `dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj` succeeds with no new errors or warnings. Depends on T006–T019. T031–T035 replace these bodies.
- [ ] T020 Create `ContosoDashboard.Tests\LocalDbFixture.cs` and an xUnit `[CollectionDefinition("LocalDb")]` in the same file.
  - The fixture creates a uniquely named database `ContosoDashboard_Tests_{Guid:N}` on `(localdb)\mssqllocaldb` with `EnsureCreated()`, so the seed users 1–8, projects 1–2, and tasks 1–5 exist.
  - It exposes `ApplicationDbContext CreateContext(params IInterceptor[] interceptors)` and a per-fixture temp storage root `Path.Combine(Path.GetTempPath(), "ContosoDashboardTests", Guid)`.
  - On dispose, it calls `EnsureDeleted()` and deletes the temp root.
  - Tests MUST NOT assume an empty `Documents` table. They assert on IDs or unique titles they created themselves.
  - Depends on T014 and T015.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings.
- [ ] T021 [P] Create `ContosoDashboard.Tests\EicarTestBytes.cs`. It builds the 68-byte EICAR test string **at runtime** by joining `"X5O!P%@AP[4\\PZX54(P^)7CC)7}"` and `"$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"`, and never stores them as one literal. Expose `byte[] Canonical()` and `byte[] WithLeadingLine()`, which prefixes "Training malware test\r\n". The bytes exist only in memory and are never written to disk.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings.
- [ ] T022 [P] Create `ContosoDashboard.Tests\DocumentTestData.cs` with arrange helpers that insert rows **directly** through `ApplicationDbContext`, so a story's tests don't depend on another story's service:
  - `AddDocumentAsync(uploaderId, projectId?, title, category, tags, fileType, bytes?)` writes the bytes through `LocalFileStorageService` under a generated key when `bytes` is provided.
  - `AddUserShareAsync`
  - `AddDepartmentShareAsync`
  - `AddTaskAttachmentAsync`
  - `AddUserAsync(role, department)`, so tests can create throwaway users for department and role changes instead of mutating seed users
  - `AddTaskAsync(projectId?, assignedUserId, createdByUserId, title)`, so tests can create throwaway tasks (for example, a P1 task assigned to a non-member) without changing the seed tasks
  - `SeedBulkDocumentsAsync(ownerId, count: 500)` with mixed categories, projects, dates, and tags
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings.
- [ ] T023 [P] Create `ContosoDashboard.Tests\TestServiceFactory.cs` with factory methods that build real infrastructure against a `LocalDbFixture`:
  - `CreateFileStorage()`: `LocalFileStorageService` with `RootPath` = fixture temp root
  - `CreateScanner()`: `SimulatedEicarMalwareScanner`
  - `CreateNotificationService(ctx)`
  - `CreateTaskService(ctx)`

  Use `NullLogger<T>`. Later stories add document-service factory methods to this file.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings.
- [ ] T024 [P] Create the base partial class `ContosoDashboard.Tests\DocumentAuthorizationMatrixTests.cs` (`[Collection("LocalDb")]`, constructor takes `LocalDbFixture`). Include the seeded-user constants `Admin = 1, Camille = 2, Floris = 3, NiKang = 4, Dana = 5, Mateo = 6, Hana = 7, Rafael = 8` and `Project1 = 1, Project2 = 2`. Add a header comment table that maps each of the 44 cells of the spec's Permissions table (11 operations × 4 roles) either to the boundary denial test method(s) that cover it, or, for a full-grant cell, to "N/A for denial" plus the positive test method that covers the grant (SC-006). Fill it in story by story, and complete it in T126.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings.
- [ ] T025 [P] Write `ContosoDashboard.Tests\SchemaTests.cs` (LocalDb collection). Assert:
  - All five tables exist, along with `Notifications.DocumentId`.
  - Seed users 5–8, project 2, project member 3, and tasks 4–5 have the R-14 values.
  - Inserting a share with both targets null, or both set, throws `DbUpdateException` (`CK_DocumentShares_SingleTarget`).
  - A duplicate user share and a duplicate department share throw (unique filtered indexes).
  - Deleting a `Document` cascades to its tags, shares, and task attachments, but leaves `DocumentActivities` rows in place.
  - A duplicate `FilePath` throws.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings. These schema tests exercise real code from T014–T015, so they may already pass. Trait: `[Trait("Story", "Foundation")]` on the class or each method.
- [ ] T026 [P] Write `ContosoDashboard.Tests\LocalFileStorageServiceTests.cs`:
  - `UploadAsync` rejects (`ArgumentException`) keys that are null or empty, rooted (`C:\x`, `/x`), contain `..`, or contain `\`.
  - A key that resolves outside the root is rejected.
  - A round trip of upload and then download returns identical bytes.
  - Uploading to an existing key throws (`FileMode.CreateNew`, no overwrite).
  - `DeleteAsync` on a missing key doesn't throw.
  - `DownloadAsync` on a missing key throws `FileNotFoundException`.
  - `GetUrlAsync` throws `NotSupportedException`.
  - The resolved root is never under `wwwroot`.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings, and never with a compile error. New tests that call stubbed members fail at runtime (red) until the implementation tasks replace the stubs; new tests of behavior that is already implemented may pass. Trait: `[Trait("Story", "Foundation")]` on the class or each method.
- [ ] T027 [P] Write `ContosoDashboard.Tests\SimulatedEicarMalwareScannerTests.cs`, using `EicarTestBytes` in a `MemoryStream`:
  - Canonical bytes and bytes with a leading line both return `Infected`.
  - Ordinary text and PNG-like bytes return `Clean`.
  - `Position` is restored to its pre-scan value after every scan.
  - A signature split across the scanner's internal read-buffer boundary is still detected.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings, and never with a compile error. New tests that call stubbed members fail at runtime (red) until the implementation tasks replace the stubs; new tests of behavior that is already implemented may pass. Trait: `[Trait("Story", "Foundation")]` on the class or each method.
- [ ] T028 [P] Write `ContosoDashboard.Tests\DocumentFileRulesTests.cs`:
  - **Size**: 26,214,400 bytes is accepted, 26,214,401 is rejected, and 0 is rejected.
  - **Extensions**: `.PDF` is accepted (case-insensitive). `report.pdf.exe`, `bundle.zip`, `tool.exe`, and a name with no extension are rejected.
  - **MIME**: every R-11 extension maps to its canonical MIME. Only `application/pdf`, `image/jpeg`, and `image/png` are previewable.
  - **`SanitizeFileName`**:
    - Drops path segments (`..\..\evil.pdf` becomes `evil.pdf`).
    - Replaces invalid and control characters with `_`.
    - Truncates to 255 characters while keeping the extension.
    - Falls back to `document{ext}`.
  - **Metadata**:
    - Title is required, 1–200 characters after trimming.
    - Description is ≤ 2,000 characters.
    - Category must be in `DocumentCategories.All`.
    - Tags are trimmed, empty tags are dropped, duplicates are removed (`OrdinalIgnoreCase`), there are at most 10, and each is ≤ 50 characters.
  - **Display names**: `GetDisplayName(mime)` returns "PDF", "Word", "Excel", "PowerPoint", "Text", "JPEG", or "PNG".
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings, and never with a compile error. New tests that call stubbed members fail at runtime (red) until the implementation tasks replace the stubs; new tests of behavior that is already implemented may pass. Trait: `[Trait("Story", "Foundation")]` on the class or each method.
- [ ] T029 [P] Write `ContosoDashboard.Tests\DocumentAccessPolicyTests.cs` (LocalDb collection). Use `DocumentTestData` and run each predicate **as an EF query** against LocalDB, so a client-evaluation exception fails the test. Assert the R-02 truths for `CanAccess`, `CanManage`, `IsOwner`, and `CanUploadToProject`:
  - **Owner**: the uploader can access and manage their own document.
  - **Project**: a member of the document's project and that project's manager can access it.
  - **Shares**: a user share grants access, and a department share grants access to whoever is in that department **now**.
  - **Team oversight**: requires role ≥ TeamLead, the same non-empty department, and an uploader whose role is Employee.
  - **Project Manager authority** comes from `Project.ProjectManagerId`, not from the role name alone.
  - **Administrator** can access and manage everything.
  - **Upload to a project**: allowed for a project member or the project manager, or for any project if the requester is an Administrator.
  - **Unknown user**: `LoadUserContextAsync` for a nonexistent user returns null.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings, and never with a compile error. New tests that call stubbed members fail at runtime (red) until the implementation tasks replace the stubs; new tests of behavior that is already implemented may pass. Trait: `[Trait("Story", "Foundation")]` on the class or each method.
- [ ] T030 [P] Write `ContosoDashboard.Tests\NotificationServiceTests.cs`. `CreateNotificationsAsync` inserts all notifications in one save, returns the inserted count, and **skips** recipients whose `InAppNotificationsEnabled == false` (FR-028). It persists `DocumentId` and the new `NotificationType` values. The existing `CreateNotificationAsync` behavior is unchanged.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings, and never with a compile error. New tests that call stubbed members fail at runtime (red) until the implementation tasks replace the stubs; new tests of behavior that is already implemented may pass. Trait: `[Trait("Story", "Foundation")]` on the class or each method.

### 2d. Foundational implementation

- [ ] T031 [P] Implement `ContosoDashboard\Services\LocalFileStorageService.cs` (R-05) with `public sealed class DocumentStorageOptions { public string RootPath { get; set; } = "AppData/uploads"; }` in the same file. The constructor takes `IOptions<DocumentStorageOptions>` and `IWebHostEnvironment` (or an explicit root for tests) and resolves the root against `ContentRootPath`.
  - **Key validation**: reject keys that are null or empty, rooted, contain `..`, contain `\`, or contain invalid path characters.
  - **Containment**: `Path.GetFullPath(Path.Combine(root, key))` must start with the full root, or the call throws.
  - **Writing**: create directories on demand. Write with `FileMode.CreateNew`. If a write fails partway, delete the partial file and rethrow.
  - **`DeleteAsync`**: idempotent.
  - **`DownloadAsync`**: returns a read-only `FileStream` (`FileShare.Read`, `useAsync: true`).
  - **`GetUrlAsync`**: throws `NotSupportedException`.
  - Makes T026 pass.
- [ ] T032 [P] Implement `ContosoDashboard\Services\SimulatedEicarMalwareScanner.cs` (R-08). Build the signature at runtime from two halves (or decode it from Base64) so it never appears as one literal. Search the stream for the signature anywhere, using a buffer that overlaps by 67 bytes. Restore `Position` afterward. Return `Infected` with the detail "EICAR test signature", otherwise `Clean`. Add an XML doc comment that marks it as a training-only limitation. Makes T027 pass.
- [ ] T033 [P] Implement the static class `ContosoDashboard\Services\DocumentFileRules.cs` (R-11):
  - `MaxFileSizeBytes = 26_214_400`
  - The extension → MIME allow-list from the R-11 table, case-insensitive
  - `IsPreviewable(mime)`
  - `TryGetFileType(fileName, out ext, out mime)`
  - `ValidateFile(fileName, length)`, which returns `DocumentErrorCode.FileEmpty`, `FileTooLarge`, or `UnsupportedFileType`
  - `SanitizeFileName`
  - `ValidateMetadata(title, category, description)`
  - `NormalizeTags(IEnumerable<string>)`, which returns a normalized list or a validation error
  - `GetDisplayName(mime)`
  - `SupportedTypesDescription`

  Makes T028 pass.
- [ ] T034 [P] Implement the static class `ContosoDashboard\Services\DocumentAccessPolicy.cs` (R-02):
  - `sealed record DocumentUserContext(int UserId, UserRole Role, string? Department)`
  - `static Task<DocumentUserContext?> LoadUserContextAsync(ApplicationDbContext, int userId)`, which reads `Users` live
  - EF-translatable `Expression<Func<Document,bool>>` builders: `CanAccess(ctx)`, `CanManage(ctx)`, `IsOwner(ctx)`, `IsTeamOversight(ctx)` (role ≥ TeamLead ∧ non-empty department ∧ `d.UploadedByUser.Department == dept` ∧ `d.UploadedByUser.Role == UserRole.Employee`)
  - `Expression<Func<Project,bool>> CanUploadToProject(ctx)`

  Treat an empty or whitespace department as "no department". Makes T029 pass.
- [ ] T035 [P] Add `Task<int> CreateNotificationsAsync(IEnumerable<Notification> notifications)` to `INotificationService` and `NotificationService` in `ContosoDashboard\Services\NotificationService.cs`. It loads recipients' `InAppNotificationsEnabled` in one query, skips disabled recipients, sets `CreatedDate` to UTC, and does one `SaveChangesAsync()`. Leave existing members unchanged. Makes T030 pass.
- [ ] T036 [P] Add `new Claim("Department", user.Department)` to the claims list in `ContosoDashboard\Pages\Login.cshtml.cs` when `!string.IsNullOrEmpty(user.Department)` (R-09). Claims are for display and UI hints only.
- [ ] T083 [P] Create `ContosoDashboard\Shared\ConfirmDialog.razor`, a Blazor-rendered modal (Bootstrap classes, no Bootstrap JS, no JS interop) with the parameters `Visible`, `Title`, `Message`, `ConfirmText` (default "Delete"), `OnConfirm`, and `OnCancel` (R-17, contracts\ui-routes.md "ConfirmDialog"). It's shared by US4 (delete, T084) and US6 (remove from task, T102), so it's built here once. Moved from US4; the ID is unchanged.
- [ ] T037 Register infrastructure in `ContosoDashboard\Program.cs` after the existing service registrations: `builder.Services.Configure<DocumentStorageOptions>(builder.Configuration.GetSection("DocumentStorage"));`, `builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();`, and `builder.Services.AddSingleton<IMalwareScanner, SimulatedEicarMalwareScanner>();`. Depends on T031, T032, and T016 (same file).
- [ ] T038 Verify Razor Pages endpoint routing in `ContosoDashboard\Program.cs` (R-06, http-endpoints.md "Routing notes"):
  1. Run the app. Confirm `/login` and `/logout` are served by their Razor Pages, which shows that `MapFallbackToPage("/_Host")` registered the Razor Pages data source.
  2. Add `app.MapRazorPages();` right before `app.MapBlazorHub();`. It's harmless with the fallback, and it makes the routing of parameterized pages such as `/documents/{id:int}/download` explicit.
  3. Leave the security-header middleware and the CSP string untouched.
  4. T056 re-verifies routing once a real document endpoint exists.
- [ ] T039 Foundation checkpoint for `ContosoDashboard\ContosoDashboard.csproj` and `ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`:
  - `dotnet build` both projects with no new warnings.
  - `dotnet test ContosoDashboard.Tests --filter "Story=Foundation"` passes T025–T030.
  - Run `dotnet run --launch-profile https` against (a) no database and (b) a pre-feature `ContosoDashboard` database with a dummy file placed in `ContosoDashboard\AppData\uploads` first. In both cases the app starts and has the new tables. In case (b), both guard warnings are logged and the dummy file is gone.
  - `ConfirmDialog` (T083) compiles; it's exercised by T084 and T102.
  - After logging in as Ni Kang, the cookie includes the `Department` claim.

**Checkpoint**: Foundation ready. User story work can begin.

---

## Phase 3: User Story 1 - Upload and Access My Own Documents (Priority: P1) 🎯 MVP

**Goal**: An employee uploads one or more allow-listed files (≤ 25 MB, scanned) with a title, category, and optional description, project, and tags. The employee sees per-file progress and results, lists their uploads in My Documents, opens a details page, and downloads the original file through an authorization-checked endpoint. Rejected files leave nothing behind.

**Independent Test** (quickstart §1): As Ni Kang:

- Upload a 2 MB PDF ("Q3 Budget Notes", Reports). It appears in My Documents with the correct metadata, and its download is byte-identical with the original name.
- `exact-25mb.pdf` is accepted.
- Oversized, empty, `.exe`, `.zip`, `report.pdf.exe`, and EICAR files are each rejected with their specific message, and no file or row is created.
- A mixed multi-file batch reports a result for each file.

As Dana Whitfield, `/documents/{D1}` and `/documents/{D1}/download` return "Document not found or not available." and a 404 identical to `/documents/999999/download`.

### Tests for User Story 1 ⚠️ (stubs first, then tests; stub-dependent tests fail at runtime)

- [ ] T133 [US1] Create `ContosoDashboard\Services\DocumentService.cs` with compile-only stubs (stub-first rule):
  - **Interface** `IDocumentService` with the US1 members from contracts\service-interfaces.md: `UploadDocumentAsync`, `GetUploadableProjectsAsync`, `GetMyDocumentsAsync`, `GetDocumentAsync`, and `OpenDocumentContentAsync`
  - **Class** `DocumentService` with constructor parameters `ApplicationDbContext`, `IFileStorageService`, `IMalwareScanner`, `INotificationService`, `ITaskService`, and `ILogger<DocumentService>` (stored in fields), and every member body `throw new NotImplementedException()`

  **Done when** `dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj` succeeds with no new errors or warnings. T045–T048 replace the bodies.
- [ ] T040 [P] [US1] Create the test doubles in `ContosoDashboard.Tests\Fakes\TestDoubles.cs`:
  - `ErrorMalwareScanner`: returns `MalwareScanStatus.Error`
  - `ThrowingMalwareScanner`: throws `InvalidOperationException`
  - `CancelingMalwareScanner`: throws `OperationCanceledException`, simulating the about-10-second timeout
  - `RecordingFileStorageService`: wraps a real `LocalFileStorageService` and records every Upload, Delete, and Download key
  - `FailingFileStorageService`: wraps a real `LocalFileStorageService`; `UploadAsync` and `DeleteAsync` can each be configured to throw `IOException` (upload failure for T043, delete failure for T077)
  - `ThrowingSaveChangesInterceptor : SaveChangesInterceptor`: throws `DbUpdateException` from `SavingChangesAsync` when armed
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings.
- [ ] T041 [P] [US1] Add `CreateDocumentService(ApplicationDbContext ctx, IFileStorageService? storage = null, IMalwareScanner? scanner = null)` to `ContosoDashboard.Tests\TestServiceFactory.cs`. It wires `DocumentService` with the real notification service and task service, plus `NullLogger<DocumentService>`.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings.
- [ ] T042 [P] [US1] Write `ContosoDashboard.Tests\DocumentUploadTests.cs` (LocalDb collection) against `IDocumentService`:
  - **Successful upload**: a successful personal upload by Ni Kang (4) stores:
    - `Title` trimmed
    - `Category`
    - `UploadedByUserId = 4`
    - UTC `UploadedDate` and `LastModifiedDate`
    - `FileSizeBytes` equal to the **stream length**, not a client value
    - `FileType` equal to the canonical MIME from the extension, not the client MIME
    - `OriginalFileName` sanitized
    - `FilePath` matching `^4/personal/[0-9a-f]{32}\.pdf$`, which never contains the original file name
  - **Activity**: exactly one `DocumentActivity` (Upload) row is written with the title and MIME snapshots.
  - **Validation errors**, each returning its specific `DocumentErrorCode` (and `Validation` for metadata):
    - Missing or whitespace title
    - Title of 201 characters
    - Category outside `DocumentCategories.All`
    - Description of 2,001 characters
    - 11 tags
    - A tag of 51 characters
  - **Tags**: `"Onboarding, onboarding ,  "` is stored as one tag.
  - **Boundary**: exactly 26,214,400 bytes is accepted.
  - **Download**: `OpenDocumentContentAsync(id, 4, Download)` returns identical bytes, `DownloadFileName` = the original name, `Inline = false`, the stored MIME, and writes one Download activity.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings, and never with a compile error. New tests that call stubbed members fail at runtime (red) until the implementation tasks replace the stubs; new tests of behavior that is already implemented may pass. Trait: `[Trait("Story", "US1")]` on the class or each method.
- [ ] T043 [P] [US1] Write `ContosoDashboard.Tests\DocumentWriteOrderTests.cs` (LocalDb collection; partial class, so US4 adds a second file). For each rejection, assert the specific error code, **zero new files** under the fixture storage root, **zero new `Documents` rows**, and no Upload activity (SC-007, FR-011):
  - **Size and type**: 26,214,401 bytes → `FileTooLarge`; 0 bytes → `FileEmpty`; `.exe`, `.zip`, `report.pdf.exe`, and no extension → `UnsupportedFileType`.
  - **EICAR**: `EicarTestBytes.WithLeadingLine()` as `eicar-test.txt` → `MalwareDetected`, and the scanner ran **before** any `UploadAsync` call (`RecordingFileStorageService` has no Upload).
  - **Scanner failure** (`ErrorMalwareScanner`, `ThrowingMalwareScanner`, `CancelingMalwareScanner`) → `ScanFailed` (fail closed).
  - **Storage failure** (`FailingFileStorageService`) → `StorageFailed`, and no row.
  - **Save failure** (armed `ThrowingSaveChangesInterceptor`) → `StorageFailed`, the file written in step 5 has been **deleted** (compensation), and no row exists.
  - **Ordering**: on success, `UploadAsync` completed before the row became visible.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings, and never with a compile error. New tests that call stubbed members fail at runtime (red) until the implementation tasks replace the stubs; new tests of behavior that is already implemented may pass. Trait: `[Trait("Story", "US1")]` on each test method.
- [ ] T044 [P] [US1] Write `ContosoDashboard.Tests\DocumentAuthorizationMatrixTests.OwnDocuments.cs` (partial class). Every denial case:
  - **Same-department Employee**: Dana (5) calling `GetDocumentAsync` on Ni Kang's personal document → `null`.
  - **Same response as nonexistent**: `OpenDocumentContentAsync` in Download mode for Dana → `NotFound`, the **same** result shape and message as for document 999999.
  - **Other departments and roles**: Mateo (6), Hana (7), and Rafael (8) → `NotFound` for the same document.
  - **My Documents**: `GetMyDocumentsAsync(5)` never contains documents uploaded by others.
  - **Upload to another project**: Ni Kang uploading with `ProjectId = 2` (not a member) → `Forbidden` and nothing stored.
  - **Unknown requester**: a user ID that doesn't exist → `NotFound` or `Forbidden`, with nothing stored.
  - **Uploadable projects**: `GetUploadableProjectsAsync` returns only P1 for Ni Kang, only P2 for Mateo, and P1 and P2 for Admin.
  - **Owner flags**: the owner's `GetDocumentAsync` flags are `CanEditMetadata = CanDelete = CanReplaceFile = CanShare = true`.
  - Update the header comment table (T024) for the cells "Access a document (Employee)" and "Upload a document to a project (Employee)".
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings, and never with a compile error. New tests that call stubbed members fail at runtime (red) until the implementation tasks replace the stubs; new tests of behavior that is already implemented may pass. Trait: `[Trait("Story", "US1")]` on each test method.
- [ ] T134 [P] [US1] Write `ContosoDashboard.Tests\DocumentUploadTimingTests.cs` (its own `IClassFixture<LocalDbFixture>`, so it gets a separate database) for SC-001, using the real `LocalFileStorageService` and `SimulatedEicarMalwareScanner` from `TestServiceFactory`:
  - **Warm-up**: one small personal upload first, not timed.
  - **Arrange**: a `MemoryStream` of exactly 26,214,400 bytes named `big.pdf` (a `%PDF-1.4` header followed by filler bytes; no EICAR content).
  - **Assert** (`Stopwatch` around `UploadDocumentAsync` as Ni Kang): the call succeeds and completes in **≤ 30 s** on localhost (SC-001). This measures validation, scanning, storage, and the save; the browser-to-server transfer is checked by hand in quickstart.md §1 step 3.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings, and never with a compile error. New tests that call stubbed members fail at runtime (red) until the implementation tasks replace the stubs; new tests of behavior that is already implemented may pass. Trait: `[Trait("Story", "US1")]` on the class or each method.

### Implementation for User Story 1

- [ ] T045 [US1] Complete `ContosoDashboard\Services\DocumentService.cs` (created with stubs in T133) with the private helpers:
  - A `ToSummary` projection `Expression<Func<Document, DocumentSummary>>` that sets `IsPreviewable` from `DocumentFileRules`
  - `ApplyPaging(IQueryable<Document>, DocumentQuery)`: default sort `UploadedDate` descending, then `DocumentId`; returns `PagedResult<DocumentSummary>` with `TotalCount`
  - `BuildStorageKey(uploaderId, projectId?, ext)` → `{uploaderId}/{projectId|personal}/{Guid.NewGuid():N}{ext}`
- [ ] T046 [US1] Implement `UploadDocumentAsync` in `ContosoDashboard\Services\DocumentService.cs` using the R-04 upload order:
  1. Load `DocumentUserContext` (unknown user → `NotFound`).
  2. Validate the file name, extension, and **stream length**, plus the metadata and tags (`DocumentFileRules`).
  3. If `ProjectId` is set, require `CanUploadToProject`, otherwise return `Forbidden`.
  4. If `TaskId` is set, return `Validation` until T101 adds task support.
  5. Scan with a 10-second `CancellationTokenSource` linked to `ct`. Treat `Infected` as `MalwareDetected`. Treat `Error`, any exception, or cancellation as `ScanFailed`.
  6. Build the key, then `UploadAsync` (an exception → `StorageFailed`).
  7. Add the `Document`, its `DocumentTag`s, and the Upload `DocumentActivity`, then call **one** `SaveChangesAsync()`.
  8. If the save throws, run `DeleteAsync(key)` as best effort (if that also fails, log a warning with the key; the leftover file is never served, spec FR-011), log a warning, and return `StorageFailed`.

  Return `DocumentOperationResult.Ok(documentId)`. Log the outcome at Information and rejections at Warning, with the user ID, the sanitized name, and the reason, but never the file contents. Makes T042, T043, T134, and part of T044 pass.
- [ ] T047 [US1] Implement `GetUploadableProjectsAsync` and `GetMyDocumentsAsync` in `ContosoDashboard\Services\DocumentService.cs`:
  - **`GetUploadableProjectsAsync`**: returns projects where the requester is a member or the manager, or all projects for an Administrator, as `ProjectOption`s ordered by name.
  - **`GetMyDocumentsAsync`**: `UploadedByUserId == me`, then `ApplyPaging`. The projection goes to SQL, with no N+1 queries.
- [ ] T048 [US1] Implement `GetDocumentAsync` and `OpenDocumentContentAsync` (Download mode) in `ContosoDashboard\Services\DocumentService.cs`:
  - **`GetDocumentAsync`**:
    - `null` unless `CanAccess`
    - Returns the summary, description, tags, `OriginalFileName`, and `LastModifiedDate`
    - Flags: `CanEditMetadata = CanDelete = CanManage`, `CanReplaceFile = CanShare = IsOwner`
    - Records **no** activity
  - **`OpenDocumentContentAsync`** in Download mode:
    - `NotFound` unless `CanAccess`
    - Opens the stream with `IFileStorageService.DownloadAsync`. If the file is missing, log an error and return `StorageFailed`; the endpoint maps that to the same 404.
    - Writes a Download `DocumentActivity` and saves, then returns `Inline = false` and `DownloadFileName = OriginalFileName`.
  - Preview mode returns `UnsupportedFileType` until T069.
- [ ] T049 [US1] Register `builder.Services.AddScoped<IDocumentService, DocumentService>();` in `ContosoDashboard\Program.cs`, after the T037 registrations.
- [ ] T050 [P] [US1] Create the Razor Page endpoint `ContosoDashboard\Pages\DocumentDownload.cshtml` (`@page "/documents/{documentId:int}/download"`, `@model DocumentDownloadModel`) and `ContosoDashboard\Pages\DocumentDownload.cshtml.cs` (http-endpoints.md):
  - `[Authorize]` on the PageModel.
  - Parse `ClaimTypes.NameIdentifier`. If it's missing or invalid, return 404.
  - Call `OpenDocumentContentAsync(id, userId, Download)`.
  - On success, set `Response.Headers.CacheControl = "private, no-store"` and return `File(stream, contentType, fileDownloadName)`.
  - On any failure, return `NotFound("Document not found or not available.")` as text/plain, with the identical body for every case.
- [ ] T051 [P] [US1] Create `ContosoDashboard\Shared\DocumentList.razor` (ui-routes.md "DocumentList"):
  - **Parameters**: `Items`, `Query`, `OnQueryChanged`, `ShowProjectFilter`, `ShowUploader`, `ProjectOptions`, and `EmptyMessage` (default "No documents found.")
  - **Table**:
    - Title, linking to `/documents/{id}`
    - Category
    - Upload date (UTC, in the existing format)
    - Human-readable size
    - Project ("—" when none)
    - Optionally, the uploader
    - A Download link: `<a href="/documents/{id}/download" download>`
  - **Paging**: Previous and Next controls that raise `OnQueryChanged`. "Loading…" is shown while `Items` is null.
  - Sort and filter controls come in T071.
- [ ] T052 [P] [US1] Create `ContosoDashboard\Shared\DocumentUploadForm.razor` (R-07, ui-routes.md "DocumentUploadForm"):
  - **Parameters**: `FixedProjectId`, `TaskId`, `TaskProjectId`, `OnUploaded`
  - **File selection**: `<InputFile multiple @key="inputKey">` with `e.GetMultipleFiles(maximumFileCount: 10)`. Capture `Name`, `Size`, and `ContentType` into one `PendingUpload` row per file.
  - **Batch limit (FR-001)**: catch the `InvalidOperationException` that `GetMultipleFiles` throws when more than 10 files are selected. Add no rows, show `DocumentMessages` "You can upload up to 10 files at a time. Select 10 or fewer files.", and let the user select again. The exception must never surface as an unhandled circuit error.
  - **Row fields**:
    - Title, defaulting to the file name without its extension; required, ≤ 200
    - Category, a required select of the 6 values
    - Description, ≤ 2,000
    - Tags, comma-separated, ≤ 10 × 50
    - Project, from `GetUploadableProjectsAsync`:
      - Locked to `FixedProjectId` when that's set (project page).
      - **Hidden** when `TaskId` is set and `TaskProjectId` has a value (a project task). It's replaced by the read-only text "Project: {name} (from the task)", so the user can't choose a project the server would override (T101).
      - Editable when `TaskId` is set and `TaskProjectId` is null (a task with no project).
  - **Client pre-checks**: empty file, more than 26,214,400 bytes, and an extension outside the allow-list, each with the exact `DocumentMessages` text.
  - **Required fields**: Submit is disabled while any valid row lacks a title or category, and those fields are highlighted.
  - **Processing**: rows upload **sequentially**. For each row, steps 1–3 run at the very start of that row's processing on submit, before any other `await` for the row. That's the recorded interpretation of the constitution's "immediately" (plan.md Constitution Check, Principle IV). Before submit, the component holds only the `IBrowserFile` reference and the captured metadata, and opens no stream.
    1. `OpenReadStream(26_214_400)`.
    2. Copy into a `MemoryStream` in 81,920-byte chunks, updating a per-row Bootstrap progress bar about every 5%.
    3. Set `Position = 0` and clear the `IBrowserFile` reference.
    4. Call `UploadDocumentAsync` with `userId` from the `NameIdentifier` claim, and `TaskId` when set.
  - **Results**: show each row's success message ("'{name}' was uploaded.") or error message from `DocumentMessages`. Increment `inputKey` after the batch, then invoke `OnUploaded`.
- [ ] T053 [US1] Create `ContosoDashboard\Pages\Documents.razor` (`@page "/documents"`, `@attribute [Authorize]`):
  - Header "My Documents", with one **Upload** button that toggles the `DocumentUploadForm`. That's 3 clicks: Upload, choose file, Submit (SC-005).
  - A `DocumentList` bound to `GetMyDocumentsAsync` with paging, and `EmptyMessage` = "You haven't uploaded any documents yet."
  - Reload the list in `OnUploaded`.
  - Get the user ID from the `NameIdentifier` claim, as existing pages do.
  - Depends on T051 and T052.
- [ ] T054 [P] [US1] Create `ContosoDashboard\Pages\DocumentDetails.razor` (`@page "/documents/{DocumentId:int}"`, `@attribute [Authorize]`):
  - Calls `GetDocumentAsync`.
  - If the result is `null`, shows "Document not found or not available."
  - Otherwise shows the title, category, description, tags, project, uploader, upload and last-modified dates, original file name, size, and type, plus a Download button (`<a href="/documents/{id}/download" download>`).
  - Leave placeholder sections for the actions that later stories add, gated by the `Can*` flags.
- [ ] T055 [P] [US1] Add a **Documents** item (`/documents`, icon `bi-file-earmark-text`, visible to all signed-in users) to `ContosoDashboard\Shared\NavMenu.razor` (ui-routes.md "NavMenu additions").
- [ ] T056 [US1] Verify US1 end to end with `ContosoDashboard.Tests\` and `specs\001-document-upload-management\quickstart.md` §1:
  - `dotnet test ContosoDashboard.Tests --filter "Story=US1"` passes (including the SC-001 timing test T134), and `--filter "Story=Foundation"` still passes.
  - **Routing (re-verifies T038)**:
    - `/documents/{id}/download` returns the file, not Blazor's "Not found".
    - Unauthenticated requests get a 302 to `/login`.
    - Dana gets the identical 404 for D1 and 999999.
  - Run quickstart.md §1, including the batch-limit check (11 files) and the storage check: files are under `AppData\uploads\4\personal\{guid}.{ext}` only, and none are under `wwwroot`.

**Checkpoint**: User Story 1 is fully functional and independently testable. This is the MVP.

---

## Phase 4: User Story 2 - Share Documents Within a Project (Priority: P2)

**Goal**: Documents associated with a project are visible and downloadable by every project member and the project manager on the project page. The other members and the PM get an in-app notification. The PM can view, download, edit metadata of, and delete any document in their project, but can't replace or share it.

**Independent Test** (quickstart §2):

- Ni Kang uploads `notes.txt` to P1. Floris and Camille are notified, and Ni Kang isn't. D3 is listed on `/projects/1` and downloads.
- Camille's flags show Edit and Delete, but not Replace or Share.
- Mateo (not on P1) and Rafael (PM of P2) get "not found" and a 404 for D3.
- P2 is never offered to Ni Kang.

### Tests for User Story 2 ⚠️ (stubs first, then tests; stub-dependent tests fail at runtime)

- [ ] T135 [US2] Add `GetProjectDocumentsAsync(int projectId, int requestingUserId, DocumentQuery query)` to `IDocumentService` and a `throw new NotImplementedException()` body to `DocumentService` in `ContosoDashboard\Services\DocumentService.cs` (stub-first rule). **Done when** `dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj` succeeds with no new errors or warnings.
- [ ] T057 [P] [US2] Write `ContosoDashboard.Tests\DocumentAuthorizationMatrixTests.ProjectDocuments.cs` (partial class). Arrange the data with `DocumentTestData`.
  - **Visibility**:
    - P1 members Floris (3) and Ni Kang (4), and PM Camille (2), can access a P1 document uploaded by Ni Kang.
    - `GetProjectDocumentsAsync(1, x)` returns it for 2, 3, 4, and 1 (Admin).
    - `GetProjectDocumentsAsync(1, 6)` (Mateo) and `GetProjectDocumentsAsync(1, 5)` (Dana) are **empty**.
  - **Denials**:
    - Mateo (6) and Rafael (8, PM of P2): `GetDocumentAsync` → `null` and Download → `NotFound`.
    - Rafael doesn't get `CanManage` on P1 documents.
  - **Camille's flags** on Ni Kang's P1 document: `CanEditMetadata` and `CanDelete` are true; `CanReplaceFile` and `CanShare` are false.
  - **Removed member** (a throwaway member removed from P1):
    - Loses access to others' P1 documents.
    - Keeps access to their own upload.
  - **Admin upload**: an Admin uploading to P2 is allowed.
  - Update the header table for "Access a document (Project Manager)" and "Upload a document to a project".
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings, and never with a compile error. New tests that call stubbed members fail at runtime (red) until the implementation tasks replace the stubs; new tests of behavior that is already implemented may pass. Trait: `[Trait("Story", "US2")]` on each test method.
- [ ] T058 [P] [US2] Write `ContosoDashboard.Tests\ProjectDocumentNotificationTests.cs` (LocalDb collection):
  - **Recipients**: after Ni Kang uploads to P1, `Notifications` contains exactly one `DocumentAdded` row for each of Floris (3) and Camille (2), and none for Ni Kang (4).
  - **Content**: each row has `DocumentId` set, and `Title` = "New project document" with a message that names the document title and "ContosoDashboard Development".
  - **Preference**: a recipient with `InAppNotificationsEnabled = false` gets nothing.
  - **Personal uploads** create no notifications.
  - **Failure isolation**: a notification failure doesn't roll back the upload. Arrange this with a recipient query that fails, or a subclassed `INotificationService` that throws, and assert that the document row still exists.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings, and never with a compile error. New tests that call stubbed members fail at runtime (red) until the implementation tasks replace the stubs; new tests of behavior that is already implemented may pass. Trait: `[Trait("Story", "US2")]` on the class or each method.

### Implementation for User Story 2

- [ ] T059 [US2] Implement `GetProjectDocumentsAsync` (stubbed in T135) in `ContosoDashboard\Services\DocumentService.cs`. The requester must be a project member or the project manager, or an Administrator. Otherwise return an **empty** `PagedResult`. When allowed, query `d.ProjectId == projectId`, then `ApplyPaging`.
- [ ] T060 [US2] Add an `internal static Task NotifyProjectDocumentAddedAsync(ApplicationDbContext ctx, INotificationService notifications, int documentId, string documentTitle, int projectId, int actorUserId, ILogger logger)` helper to `ContosoDashboard\Services\DocumentService.cs` (R-12). T099 reuses it.
  - **Recipients**: the project manager and all project members, minus the actor, de-duplicated.
  - **Content**: `Type = NotificationType.DocumentAdded`, `Title` = "New project document", a `Message` that names the document and project, and `DocumentId`.
  - **Sending**: `CreateNotificationsAsync`. Catch and log any exception.

  Call it **after** the upload commit in `UploadDocumentAsync` when `ProjectId` is set.
- [ ] T061 [P] [US2] Add a "Project Documents" card to `ContosoDashboard\Pages\ProjectDetails.razor`. It contains a `DocumentList` (with `ShowUploader = true`, `EmptyMessage` = "No documents have been added to this project yet.") bound to `GetProjectDocumentsAsync(ProjectId, userId, query)` and an Upload button that opens `DocumentUploadForm` with `FixedProjectId = ProjectId`. Reload the card in `OnUploaded`. Don't change the existing project data loading.
- [ ] T062 [P] [US2] In `ContosoDashboard\Pages\Notifications.razor`, show an "Open document" link to `/documents/{DocumentId}` when `notification.DocumentId` has a value (R-12). The details page re-checks access, so a revoked or deleted document shows "Document not found or not available." The notification's own title and message stay as sent (historical record, spec FR-028); don't rewrite or hide them.
- [ ] T063 [US2] Verify US2 with `ContosoDashboard.Tests\` and `specs\001-document-upload-management\quickstart.md` §2:
  - `dotnet test ContosoDashboard.Tests --filter "Story=US2"` passes, and `--filter "Story=Foundation|Story=US1"` still passes.
  - Run quickstart.md §2, including both negative cases (Mateo, Rafael) and the case that P2 isn't offered to Ni Kang.

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - Find and Preview Documents (Priority: P2)

**Goal**: Users sort lists by title, upload date, category, and size in both directions. They filter by category, project, and date range, alone or combined. They search every document they can access by title, description, tag, uploader name, or project name, within 2 s. They preview PDF, JPEG, and PNG files in a new browser tab within 3 s, and get a download-only option for other types.

**Independent Test** (quickstart §3):

- Each sort direction and each filter combination returns the expected documents.
- Search finds documents by each attribute and shows "No documents found" for a nonsense term.
- As Dana, searching "onboarding" returns neither Ni Kang's P1 document nor Mateo's personal document.
- PDF and PNG previews render inline. Requesting a preview of a `.docx` redirects to its details page.

### Tests for User Story 3 ⚠️ (stubs first, then tests; stub-dependent tests fail at runtime)

- [ ] T136 [US3] Add `SearchDocumentsAsync(int requestingUserId, string searchText, DocumentQuery query)` to `IDocumentService` and a `throw new NotImplementedException()` body to `DocumentService` in `ContosoDashboard\Services\DocumentService.cs` (stub-first rule). Preview mode already returns `UnsupportedFileType` (T048), so the preview tests compile and fail at runtime without another stub. **Done when** `dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj` succeeds with no new errors or warnings.
- [ ] T064 [P] [US3] Write `ContosoDashboard.Tests\DocumentSearchAndQueryTests.cs` (LocalDb collection). Use unique per-test title prefixes, and arrange the data with `DocumentTestData`.
  - **Search attributes**: `SearchDocumentsAsync` matches title, description, tag, uploader `DisplayName` ("Ni Kang"), and project name ("ContosoDashboard Development"), case-insensitively.
  - **Access only (US3 AS3)**: when Dana searches "onboarding", her own accessible tagged document is returned, but Ni Kang's P1 document and Mateo's personal document with the same tag aren't.
  - **Term rules**: an empty term, a whitespace-only term, or a term over 100 characters gives an empty result.
  - **No wildcards**: the terms `%` and `_` match literally and aren't treated as SQL wildcards.
  - **Sorting**: `GetMyDocumentsAsync` with each `DocumentSortField` × {ascending, descending} orders correctly, with a stable tie-break on `DocumentId`.
  - **Filters**: `Category`, `ProjectId`, and `FromDate`/`ToDate` (inclusive UTC days), alone and combined, return only documents that match all of them.
  - **Paging**: `PageSize` 0 is clamped to 1, and 500 is clamped to 100.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings, and never with a compile error. New tests that call stubbed members fail at runtime (red) until the implementation tasks replace the stubs; new tests of behavior that is already implemented may pass. Trait: `[Trait("Story", "US3")]` on the class or each method.
- [ ] T065 [P] [US3] Write `ContosoDashboard.Tests\DocumentPreviewTests.cs` (LocalDb collection):
  - **Previewable types**: `OpenDocumentContentAsync(id, owner, Preview)` for a PDF, a JPEG, and a PNG returns `Inline = true` with the **stored** canonical MIME, and writes one Preview activity.
  - **Non-previewable types**: for `.docx` and `.txt`, it returns `UnsupportedFileType` and writes **no** activity.
  - **No access**: Preview by a user without access → `NotFound`.
  - **Stored MIME wins**: a PNG whose client-reported type was `text/html` is still served as `image/png`.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings, and never with a compile error. New tests that call stubbed members fail at runtime (red) until the implementation tasks replace the stubs; new tests of behavior that is already implemented may pass. Trait: `[Trait("Story", "US3")]` on the class or each method.
- [ ] T066 [P] [US3] Write `ContosoDashboard.Tests\DocumentPerformanceTests.cs` (its own `IClassFixture<LocalDbFixture>`, so it gets a separate database):
  - **Seed**: `DocumentTestData.SeedBulkDocumentsAsync` creates 500 documents accessible to one user (a mix of owned, P1, and shared), with tags.
  - **Warm-up**: run each query once before timing.
  - **Assertions** (`Stopwatch`):
    - `SearchDocumentsAsync` for a common tag term and for an uploader name each completes in **< 2 s** and returns the expected `TotalCount` (SC-003).
    - `GetMyDocumentsAsync`, sorted by Title and filtered by category and date range, completes in **< 2 s** (SC-002).
  - **Preview timing (SC-004)**: arrange a PDF-like file of 10,000,000 bytes (under 10 MB) with `DocumentTestData.AddDocumentAsync(..., fileType: "application/pdf", bytes)`. After a warm-up call, time `OpenDocumentContentAsync(id, owner, Preview)` **plus** reading the returned stream to the end. It succeeds with `Inline = true` and completes in **≤ 3 s** on localhost. Rendering in the browser is checked by hand in quickstart.md §3 step 4.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings, and never with a compile error. New tests that call stubbed members fail at runtime (red) until the implementation tasks replace the stubs; new tests of behavior that is already implemented may pass. Trait: `[Trait("Story", "US3")]` on each test method.

### Implementation for User Story 3

- [ ] T067 [US3] Extend the private query pipeline in `ContosoDashboard\Services\DocumentService.cs` with an `ApplyFilters` step, plus sorting in `ApplyPaging`:
  - **Filters**:
    - `Category` (ignored if not in `DocumentCategories.All`)
    - `ProjectId`
    - `FromDate` (≥ `FromDate` 00:00 UTC)
    - `ToDate` (< `ToDate`+1 day 00:00 UTC)
  - **Sort**: `SortBy` (Title, UploadedDate, Category, FileSize) with `Descending`, then `ThenBy(DocumentId)`.
  - Apply both to every existing list method.
- [ ] T068 [US3] Implement `SearchDocumentsAsync` (stubbed in T136) in `ContosoDashboard\Services\DocumentService.cs` (R-10):
  1. Trim the term. If it isn't 1–100 characters, return an empty result.
  2. Apply `DocumentAccessPolicy.CanAccess(ctx)` **first**.
  3. Then match `Title.Contains(t) || Description.Contains(t) || Tags.Any(x => x.Tag.Contains(t)) || UploadedByUser.DisplayName.Contains(t) || Project.Name.Contains(t)`.
  4. Apply filters, sort, and paging.
- [ ] T069 [US3] Implement Preview mode in `OpenDocumentContentAsync` in `ContosoDashboard\Services\DocumentService.cs`:
  - If the document isn't accessible → `NotFound`.
  - If it isn't `DocumentFileRules.IsPreviewable(FileType)` → `UnsupportedFileType`, with no activity.
  - Otherwise open the stream, write a Preview activity, and return `Inline = true` with `ContentType` = the stored `FileType`.
- [ ] T070 [P] [US3] Create the Razor Page endpoint `ContosoDashboard\Pages\DocumentPreview.cshtml` (`@page "/documents/{documentId:int}/preview"`) and `ContosoDashboard\Pages\DocumentPreview.cshtml.cs` (http-endpoints.md):
  - `[Authorize]` on the PageModel.
  - On success: `Cache-Control: private, no-store`, `Content-Disposition: inline; filename="…"`, return `File(stream, contentType)` without a download name.
  - On `UnsupportedFileType`: `Redirect($"/documents/{documentId}")`.
  - Otherwise: the same 404 text as the download endpoint.
- [ ] T071 [P] [US3] Add sort and filter controls to `ContosoDashboard\Shared\DocumentList.razor`:
  - Clickable headers for Title, Upload date, Category, and Size that toggle the direction and show an arrow indicator.
  - A filter bar with a category select, a project select (when `ShowProjectFilter`), From and To date inputs (labelled "(UTC)"), and Apply and Clear buttons.
  - Each control raises `OnQueryChanged` with `Page = 1`.
  - A Preview link `<a href="/documents/{id}/preview" target="_blank" rel="noopener">` for previewable rows only.
- [ ] T072 [P] [US3] Create `ContosoDashboard\Pages\DocumentSearch.razor` (`@page "/documents/search"`, `@attribute [Authorize]`):
  - A search box bound to the `?q=` query string (`[SupplyParameterFromQuery]`).
  - A `DocumentList` (`ShowUploader = true`, `ShowProjectFilter = true`) bound to `SearchDocumentsAsync`.
  - "No documents found." when the result is empty. The validation hint "Enter a search term of 1 to 100 characters." when the term is empty or longer than 100 characters.
- [ ] T073 [US3] Wire the query and filter state into `ContosoDashboard\Pages\Documents.razor`. Hold a `DocumentQuery` and handle `OnQueryChanged` by reloading `GetMyDocumentsAsync`. Pass `ShowProjectFilter = true` and `ProjectOptions` from `GetUploadableProjectsAsync`. Depends on T071.
- [ ] T074 [P] [US3] In `ContosoDashboard\Pages\DocumentDetails.razor`, add a **Preview** button (`target="_blank" rel="noopener"`) when `Summary.IsPreviewable`. Otherwise show the note "Preview isn't available for this file type. Download it instead." next to Download (US3 AS6).
- [ ] T075 [P] [US3] Add a **Search Documents** item (`/documents/search`, icon `bi-search`, visible to all signed-in users) to `ContosoDashboard\Shared\NavMenu.razor`.
- [ ] T076 [US3] Verify US3 with `ContosoDashboard.Tests\` and `specs\001-document-upload-management\quickstart.md` §3:
  - `dotnet test ContosoDashboard.Tests --filter "Story=US3"` passes (including the SC-004 preview timing in T066), and `--filter "Story=Foundation|Story=US1"` still passes.
  - Run quickstart.md §3.
  - In DevTools, the preview response shows `Content-Type` = the stored MIME, `Cache-Control: private, no-store`, `X-Frame-Options: DENY`, and `nosniff`.

**Checkpoint**: User Stories 1–3 work independently.

---

## Phase 6: User Story 4 - Edit, Replace, and Delete Documents (Priority: P2)

**Goal**:

- The uploader edits a document's metadata, replaces its file (new key, old file discarded, failures keep the old file), and deletes it after confirmation (record first, then file).
- Managers (Team Lead for team Employees' uploads, PM for their project, Administrator for everything) can edit metadata and delete, but **only the owner** can replace the file.

**Independent Test** (quickstart §4):

- **Edit**: as Ni Kang, edit D1 and see the change in lists and search.
- **Replace**:
  - Replacing with `diagram.png` updates the type and size, the new file is served, and the old GUID file is gone.
  - An oversized or EICAR replacement is rejected, and the current file is unchanged.
- **Delete**: cancelling changes nothing. Confirming removes D2 everywhere, and its download returns 404.
- **Managers**:
  - Floris can't edit or delete Camille's P1 document.
  - Camille can edit and delete Ni Kang's D3, but can't replace it.

### Tests for User Story 4 ⚠️ (stubs first, then tests; stub-dependent tests fail at runtime)

- [ ] T137 [US4] Add `UpdateMetadataAsync`, `ReplaceFileAsync`, and `DeleteDocumentAsync` (signatures from contracts\service-interfaces.md) to `IDocumentService`, with `throw new NotImplementedException()` bodies in `DocumentService` in `ContosoDashboard\Services\DocumentService.cs` (stub-first rule). **Done when** `dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj` succeeds with no new errors or warnings.
- [ ] T077 [P] [US4] Write `ContosoDashboard.Tests\DocumentWriteOrderTests.ReplaceDelete.cs` (partial class from T043):
  - **Successful replace**: `ReplaceFileAsync` by the owner writes a **new** key and updates `FilePath`, `FileSizeBytes`, `FileType`, `OriginalFileName`, and `LastModifiedDate`. The **old file is deleted**, and one Replace activity carries the new MIME.
  - **Rejected replace**: 26,214,401 bytes, an unsupported type, `EicarTestBytes.Canonical()`, and `ErrorMalwareScanner` each give the specific error. The **old file and record are unchanged**, and no new file exists.
  - **Save failure on replace** (armed `ThrowingSaveChangesInterceptor`): returns `StorageFailed`, the **new** file is deleted, and the old file and record are intact.
  - **Delete**: `DeleteDocumentAsync` removes the row, along with its tags, shares, and task attachments through the cascade. It then deletes the file and keeps a Delete activity whose `DocumentId` and `DocumentTitle` snapshot are still queryable.
  - **Delete when file removal fails** (`FailingFileStorageService.DeleteAsync` throws): the record is still deleted and the call succeeds. The leftover file is only logged and is never served, because no record refers to it (spec FR-011's accepted training limitation).
  - **Replace when old-file removal fails** (`FailingFileStorageService.DeleteAsync` throws after a successful replace): the call succeeds, the record points at the new file, and downloads return the new content.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings, and never with a compile error. New tests that call stubbed members fail at runtime (red) until the implementation tasks replace the stubs; new tests of behavior that is already implemented may pass. Trait: `[Trait("Story", "US4")]` on each test method.
- [ ] T078 [P] [US4] Write `ContosoDashboard.Tests\DocumentAuthorizationMatrixTests.Management.cs` (partial class). For each case, assert both the service result and the `GetDocumentAsync` flags:
  - **Employee, not the owner, document accessible**: Ni Kang (4) on Floris's P1 document → `UpdateMetadataAsync`, `DeleteDocumentAsync`, and `ReplaceFileAsync` are all `Forbidden`.
  - **Team Lead on a PM's document**: Floris (3) on Camille's P1 document (Camille isn't an Employee) → Update and Delete are `Forbidden`.
  - **PM on their project**: Camille (2) on Ni Kang's P1 document → Update and Delete are `Ok`, and Replace is `Forbidden`.
  - **Administrator**: Admin (1) on any document → Update and Delete are `Ok`, and Replace is `Forbidden`.
  - **PM on another project**: Rafael (8) on a P1 document → `NotFound`.
  - **No access**: Dana (5) on Ni Kang's personal document → `NotFound` for all three operations.
  - **Nonexistent document** (ID 999999) → `NotFound`.
  - Update the header table for the Edit metadata, Replace file, and Delete rows.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings, and never with a compile error. New tests that call stubbed members fail at runtime (red) until the implementation tasks replace the stubs; new tests of behavior that is already implemented may pass. Trait: `[Trait("Story", "US4")]` on each test method.
- [ ] T079 [P] [US4] Write `ContosoDashboard.Tests\DocumentMetadataUpdateTests.cs`. For `UpdateMetadataAsync`:
  - **Validation**: the same rules as upload apply (title 1–200, category in the list, description ≤ 2,000, ≤ 10 tags of ≤ 50 characters), each returning `Validation`.
  - **Tags**: tags are **replaced as a set**. Removed tags are deleted, new tags are added, and duplicates that differ only in case collapse.
  - **Project**: `ProjectId` is never changed.
  - **Timestamp**: `LastModifiedDate` advances.
  - **Activity**: one MetadataEdit activity is written, with a change summary in `Details` (for example "Category: Reports → Other").
  - **Concurrency**: last write wins (two sequential updates from different managers leave the second one's values).
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings, and never with a compile error. New tests that call stubbed members fail at runtime (red) until the implementation tasks replace the stubs; new tests of behavior that is already implemented may pass. Trait: `[Trait("Story", "US4")]` on the class or each method.

### Implementation for User Story 4

- [ ] T080 [US4] Implement `UpdateMetadataAsync` (stubbed in T137) in `ContosoDashboard\Services\DocumentService.cs`:
  - Load the document with its tags. `NotFound` unless `CanAccess`; `Forbidden` unless `CanManage`.
  - Validate with `DocumentFileRules`.
  - Update `Title`, `Description`, `Category`, and the tag set (diff), plus `LastModifiedDate`. Don't touch the project.
  - Add a MetadataEdit activity with a change summary of ≤ 500 characters, and do one save.
- [ ] T081 [US4] Implement `ReplaceFileAsync` (stubbed in T137) in `ContosoDashboard\Services\DocumentService.cs` (R-04 replace order):
  1. `NotFound` unless `CanAccess`. `Forbidden` unless `IsOwner`, for **every** role, Administrator included.
  2. Validate the file name and stream length.
  3. Scan, failing closed with the 10-second timeout.
  4. Build a **new** key, then `UploadAsync`.
  5. Update the record fields and add a Replace activity in one save. If the save fails, delete the new file and return `StorageFailed`.
  6. On success, delete the old key as best effort, logging a warning with the key if that fails (the leftover file is never served, spec FR-011).
- [ ] T082 [US4] Implement `DeleteDocumentAsync` (stubbed in T137) in `ContosoDashboard\Services\DocumentService.cs` (R-04 delete order):
  1. `NotFound` unless `CanAccess`. `Forbidden` unless `CanManage`.
  2. Capture the key, title, and MIME.
  3. Remove the `Document` (the cascade removes its tags, shares, and attachments) and add a Delete activity in **one** `SaveChangesAsync()`.
  4. After the commit, `DeleteAsync(key)` as best effort, logging a warning with the key if it fails (spec FR-011).
- [ ] T084 [US4] Add the management actions to `ContosoDashboard\Pages\DocumentDetails.razor`, each shown only when its flag is true:
  - **Edit details** (`CanEditMetadata`): an inline `EditForm` for title, category, description, and comma-separated tags. Show the `DocumentMessages` validation details. Refresh on save and show "Document details saved."
  - **Replace file** (`CanReplaceFile`): a single-file `InputFile`, using the same `MemoryStream` pattern and client pre-checks as T052, with a progress bar. Show "The file was replaced." or the error message.
  - **Delete** (`CanDelete`): opens `ConfirmDialog` (T083) with the delete-confirmation title, message, and "Delete" button from `DocumentMessages`. Cancel changes nothing. Confirm calls `DeleteDocumentAsync`, then navigates to `/documents` and shows "The document was deleted."

  Show `Forbidden` or `NotFound` results with the standard messages. Depends on T080–T082 and T083 (Phase 2).
- [ ] T085 [US4] Verify US4 with `ContosoDashboard.Tests\` and `specs\001-document-upload-management\quickstart.md` §4:
  - `dotnet test ContosoDashboard.Tests --filter "Story=US4"` passes, and `--filter "Story=Foundation|Story=US1"` still passes.
  - Run quickstart.md §4: the old GUID file is gone after a replace, nothing changes after a rejected replace, and the deleted document returns 404.

**Checkpoint**: User Stories 1–4 work independently.

---

## Phase 7: User Story 5 - Share Documents with People and Teams (Priority: P3)

**Goal**:

- The owner shares a document with specific users or with departments ("teams"), checked live.
- Recipients are notified, excluding the sharer, and with no duplicates.
- Shared documents appear in Shared with Me with the sharer's name and the share date, with view, preview, and download access only.
- The owner sees the current shares and removes them, which revokes access immediately.

**Independent Test** (quickstart §5):

- Ni Kang shares D4 with Dana. Dana is notified and sees D4 in Shared with Me with view and download only. Re-sharing with Dana creates no duplicate.
- Sharing with Marketing notifies Mateo, Hana, and Rafael. Removing that share makes Mateo get "not found" immediately.
- Sharing with Engineering doesn't notify Ni Kang.
- Dana (a recipient) and the Admin (not the owner) can't share D4.

### Tests for User Story 5 ⚠️ (stubs first, then tests; stub-dependent tests fail at runtime)

- [ ] T138 [US5] Add compile-only stubs in `ContosoDashboard\Services\DocumentShareService.cs` and `ContosoDashboard\Services\DocumentService.cs` (stub-first rule):
  - Create `ContosoDashboard\Services\DocumentShareService.cs` with the records `ShareTargets`, `ShareUserOption`, and `DocumentShareInfo` (real), the interface `IDocumentShareService` (from contracts\service-interfaces.md), and the class `DocumentShareService` with its constructor (`ApplicationDbContext`, `INotificationService`, `ILogger<DocumentShareService>`) and every member body `throw new NotImplementedException()`.
  - Add `GetSharedWithMeAsync(int requestingUserId, DocumentQuery query)` to `IDocumentService`, with a stub body in `ContosoDashboard\Services\DocumentService.cs`.

  **Done when** `dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj` succeeds with no new errors or warnings.
- [ ] T086 [P] [US5] Add `CreateShareService(ApplicationDbContext ctx)` to `ContosoDashboard.Tests\TestServiceFactory.cs`. It returns `DocumentShareService` with the real notification service and `NullLogger`.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings.
- [ ] T087 [P] [US5] Write `ContosoDashboard.Tests\DocumentShareServiceTests.cs` (LocalDb collection):
  - **User share**: `ShareWithUsersAsync(d, [Dana], owner)`:
    - Creates one `DocumentShare`, one Share activity (`Details` "Shared with user Dana Whitfield"), and one `DocumentShared` notification naming the document and the sharer.
    - Dana's `GetSharedWithMeAsync` includes it with `SharedByName` = "Ni Kang", the date, and `ViaTeam = false`.
  - **Repeat share**: sharing with Dana again creates **no** new row and **no** new notification (US5 AS6).
  - **Self-share**: sharing only with the owner → `InvalidShareTarget`.
  - **Unknown user** → `InvalidShareTarget`.
  - **Department share**: `ShareWithDepartmentsAsync(d, ["Marketing"], owner)` notifies users 6, 7, and 8, and each sees the document with `ViaTeam = true`.
  - **Own department**: sharing with "Engineering" doesn't notify the owner.
  - **Empty department**: a department with no users → `InvalidShareTarget`.
  - **Remove share**: `RemoveShareAsync` deletes the row, writes a ShareRemoved activity, and the recipient's `GetDocumentAsync` immediately returns `null`.
  - **Live department membership**: a throwaway user who **joins** Marketing after the share gains access; one who **leaves** loses it.
  - **Shared with Me excludes own uploads**: `GetSharedWithMeAsync` excludes documents the requester uploaded.
  - **Share targets**: `GetShareTargetsAsync` lists all users except the owner, plus the distinct non-empty departments.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings, and never with a compile error. New tests that call stubbed members fail at runtime (red) until the implementation tasks replace the stubs; new tests of behavior that is already implemented may pass. Trait: `[Trait("Story", "US5")]` on the class or each method.
- [ ] T088 [P] [US5] Write `ContosoDashboard.Tests\DocumentAuthorizationMatrixTests.Sharing.cs` (partial class):
  - **Non-owners can't share or manage shares**: for Dana (a recipient), Camille (PM of the document's project), Floris (Team Lead with oversight), and Admin (1), each of `ShareWithUsersAsync`, `ShareWithDepartmentsAsync`, `RemoveShareAsync`, `GetSharesAsync`, and `GetShareTargetsAsync` → `Forbidden` (or `null` for the getters). If the user can't access the document at all → `NotFound`.
  - **Recipient flags**: a recipient's `GetDocumentAsync` returns the document with `CanEditMetadata`, `CanDelete`, `CanReplaceFile`, and `CanShare` all false (FR-023). The service-level recipient denials for `UpdateMetadataAsync`, `DeleteDocumentAsync`, and `ReplaceFileAsync` call US4 members, so they're in T126 (Polish), which keeps US5 dependent only on US1.
  - **Bystander denial**: a user who is neither a recipient nor otherwise authorized → `NotFound`.
  - Update the header table for the "Share or remove shares" row.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings, and never with a compile error. New tests that call stubbed members fail at runtime (red) until the implementation tasks replace the stubs; new tests of behavior that is already implemented may pass. Trait: `[Trait("Story", "US5")]` on each test method.

### Implementation for User Story 5

- [ ] T089 [US5] Implement `DocumentShareService` in `ContosoDashboard\Services\DocumentShareService.cs` (stubbed in T138), per contracts\service-interfaces.md:
  - **Owner check**: every method returns `NotFound` (or `null`) when the requester can't access the document, and `Forbidden` (or `null`) when the requester isn't the owner.
  - **`ShareWithUsersAsync`**: skips the owner and existing shares, and rejects unknown IDs. Inserts the shares plus Share activities in one save. After the commit, notifies only **newly added** recipients through `CreateNotificationsAsync` (`Type = DocumentShared`, `Title` "Document shared with you", `DocumentId`).
  - **`ShareWithDepartmentsAsync`**: requires a department that is non-empty and has at least one user, and skips departments already shared. After the commit, notifies the department's **current** members except the owner.
  - **`RemoveShareAsync`**: deletes the row and adds a ShareRemoved activity in one save.
- [ ] T090 [US5] Register `builder.Services.AddScoped<IDocumentShareService, DocumentShareService>();` in `ContosoDashboard\Program.cs`.
- [ ] T091 [US5] Implement `GetSharedWithMeAsync` (stubbed in T138) in `ContosoDashboard\Services\DocumentService.cs`:
  - Include documents that have a share where `SharedWithUserId == me` or (`myDept` is non-empty and `SharedWithDepartment == myDept`), and that I didn't upload.
  - Project to `SharedDocumentSummary`, taking `SharedByName`, `SharedDate`, and `ViaTeam` from the most recent applicable share.
  - Apply filters, sort, and paging.
- [ ] T092 [P] [US5] Create `ContosoDashboard\Pages\SharedDocuments.razor` (`@page "/documents/shared"`, `@attribute [Authorize]`):
  - A table of `GetSharedWithMeAsync` results: Title (linking to details), Category, "Shared by", Shared date, "via team" badge, Size, Preview (previewable rows only, new tab), and Download.
  - The same sort, filter, and paging controls as `DocumentList`.
  - The empty state "No documents have been shared with you."
- [ ] T093 [US5] Add a **Sharing** panel to `ContosoDashboard\Pages\DocumentDetails.razor`, shown only when `CanShare`:
  - A multi-select of users and departments from `GetShareTargetsAsync`, with a Share button.
  - A list of the current shares from `GetSharesAsync`, each with a Remove button (calls `RemoveShareAsync`, then refreshes).
  - Result messages from `DocumentMessages`: "The document was shared.", "The share was removed.", and the `InvalidShareTarget`, `Forbidden`, and `NotFound` messages.

  Recipients see no sharing controls.
- [ ] T094 [P] [US5] Add a **Shared with Me** item (`/documents/shared`, icon `bi-share`, visible to all signed-in users) to `ContosoDashboard\Shared\NavMenu.razor`.
- [ ] T095 [US5] Verify US5 with `ContosoDashboard.Tests\` and `specs\001-document-upload-management\quickstart.md` §5:
  - `dotnet test ContosoDashboard.Tests --filter "Story=US5"` passes, and `--filter "Story=Foundation|Story=US1"` still passes.
  - Run quickstart.md §5, including the negative cases for Dana and the Admin.

**Checkpoint**: User Stories 1–5 work independently.

---

## Phase 8: User Story 6 - Attach Documents to Tasks (Priority: P3)

**Goal**: A task detail page (`/tasks/{taskId}`) lists the attachments the viewer can access. From it, users upload new documents (which take the task's project), attach existing documents under the FR-030 rules, and remove attachments under the FR-030a rules. On a project task, only project members, the project's PM, and Administrators can upload or attach; other task viewers (a non-member assignee or creator) are refused. Removal never changes the document's project, and attaching and removing are both audited.

**Independent Test** (quickstart §6):

- **Upload from task**: uploading from task 2 associates the document with P1.
- **Attach own document**: attaching Ni Kang's personal D4 to task 2 sets its project to P1.
- **No-project task**: attaching D1 to task 4 leaves D1's project unchanged.
- **Cross-project**: attaching Rafael's P2 document to task 2 is refused with the cross-project message.
- **Shared no-project document**: Camille's D6, shared with Ni Kang, can't be attached by Ni Kang, and its project is unchanged.
- **Non-member assignee** (automated, T097 and T098): an assignee of a P1 task who isn't a P1 member is refused for both upload-from-task and attaching their own no-project document.
- **Removal**: removing D4 from task 2 leaves it on P1, and TaskAttach and TaskDetach entries exist.
- **Task not visible**: Mateo sees "Task not found or not available." for `/tasks/2`.

### Tests for User Story 6 ⚠️ (stubs first, then tests; stub-dependent tests fail at runtime)

- [ ] T139 [US6] Create `ContosoDashboard\Services\TaskDocumentService.cs` with compile-only stubs (stub-first rule): the record `TaskDocumentInfo` (real), the interface `ITaskDocumentService` from contracts\service-interfaces.md (`GetTaskDocumentsAsync`, `CanAddDocumentsAsync`, `GetAttachableDocumentsAsync`, `AttachDocumentAsync`, `DetachDocumentAsync`), and the class `TaskDocumentService` with its constructor (`ApplicationDbContext`, `ITaskService`, `INotificationService`, `ILogger<TaskDocumentService>`) and every member body `throw new NotImplementedException()`. Upload with `TaskId` already returns `Validation` (T046), so the upload-from-task tests compile and fail at runtime. **Done when** `dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj` succeeds with no new errors or warnings.
- [ ] T096 [P] [US6] Add `CreateTaskDocumentService(ApplicationDbContext ctx)` to `ContosoDashboard.Tests\TestServiceFactory.cs`. It returns `TaskDocumentService` with the real `TaskService`, notification service, and `NullLogger`.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings.
- [ ] T097 [P] [US6] Write `ContosoDashboard.Tests\TaskDocumentServiceTests.cs` (LocalDb collection):
  - **Upload from task**: `UploadDocumentAsync` with `TaskId = 2` (P1) and `ProjectId = null`, as Ni Kang:
    - Creates the `TaskDocument`, `Document.ProjectId = 1`, and both Upload and TaskAttach activities (`TaskId = 2`).
    - Notifies the P1 members.
  - **Task not visible**: `TaskId` of a task the user can't see (Mateo, task 2) → `NotFound`, with nothing stored.
  - **Non-member assignee upload (FR-030, US6 AS10)**: arrange a throwaway P1 task with `DocumentTestData.AddTaskAsync(1, assignedUserId: Dana, createdByUserId: Camille, …)`. Dana can view it but isn't a P1 member. `UploadDocumentAsync` with that `TaskId` as Dana → `Forbidden`, with no new file under the storage root, no `Documents` row, no `TaskDocument` row, no activity, and no notification.
  - **`CanAddDocumentsAsync`**: `true` for Ni Kang (member) and Camille (PM) on task 2, and for Ni Kang on task 4 (no project). `false` for Dana on the throwaway P1 task, and for Mateo on task 2 (not visible).
  - **Attach own no-project document** to task 2:
    - Sets `ProjectId = 1` and updates `LastModifiedDate`.
    - Writes a TaskAttach activity whose `Details` mention the project.
    - Sends `DocumentAdded` notifications.
  - **Cross-project**: attaching a P2 document to task 2 → `CrossProject`.
  - **No-project task**: attaching to task 4 leaves `ProjectId` unchanged.
  - **Idempotent**: attaching the same document twice returns `Ok` and leaves one row.
  - **Detach by the attacher**: deletes only the link, leaves `ProjectId` unchanged, and writes a TaskDetach activity with `TaskId`.
  - **Attachment list**: `GetTaskDocumentsAsync` hides attachments the viewer can't access and sets `CanDetach` correctly.
  - **Attachable list**: `GetAttachableDocumentsAsync` excludes cross-project documents, no-project documents the requester can't own or manage, and documents already attached. It returns at most 50, and it's **empty** for Dana on the throwaway P1 task (non-member).
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings, and never with a compile error. New tests that call stubbed members fail at runtime (red) until the implementation tasks replace the stubs; new tests of behavior that is already implemented may pass. Trait: `[Trait("Story", "US6")]` on the class or each method.
- [ ] T098 [P] [US6] Write `ContosoDashboard.Tests\DocumentAuthorizationMatrixTests.TaskAttachments.cs` (partial class):
  - **Shared no-project document (US6 AS6)**: Ni Kang (Employee) attaching Camille's no-project document that was shared with her, to task 2:
    - Returns `Forbidden`.
    - Leaves `ProjectId` null.
    - Floris still gets `NotFound` for that document.
  - **Team Lead with manage rights**: Floris (TeamLead), who can manage Ni Kang's no-project upload through oversight, **can** attach it to task 2.
  - **Task not visible**: Mateo calling `GetTaskDocumentsAsync(2)` → `null`, and `AttachDocumentAsync(2, …)` → `NotFound`.
  - **Remove someone else's attachment (US6 AS9)**: Ni Kang detaching an attachment Camille made of Camille's own P1 document → `Forbidden`.
  - **Inaccessible document**: attaching a document the requester can't access → `NotFound`.
  - **Non-member assignee attach (FR-030, US6 AS10)**: on a throwaway P1 task assigned to Dana (`DocumentTestData.AddTaskAsync`), Dana (not a P1 member):
    - Attaching **her own no-project document** → `Forbidden`. Its `ProjectId` stays null, and no `TaskDocument` row, activity, or notification is created.
    - Attaching a P1 document she can access through a user share → `Forbidden`, with no `TaskDocument` row.
    - By contrast, Camille (PM of P1) and Ni Kang (P1 member) each attaching **their own** no-project document to that task succeed, which shows the boundary.
  - Update the header table for the "Attach a document to a task" and "Remove a document from a task" rows, including the Employee cell's project-membership limit.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings, and never with a compile error. New tests that call stubbed members fail at runtime (red) until the implementation tasks replace the stubs; new tests of behavior that is already implemented may pass. Trait: `[Trait("Story", "US6")]` on each test method.

### Implementation for User Story 6

- [ ] T099 [US6] Implement `TaskDocumentService` in `ContosoDashboard\Services\TaskDocumentService.cs` (stubbed in T139), per contracts\service-interfaces.md and data-model.md "Attach rules" and "Detach rules":
  - **Task visibility**: always use `ITaskService.GetTaskByIdAsync(taskId, requestingUserId) != null`.
  - **Project membership (FR-030)**: for a task with project P, adding documents requires `DocumentAccessPolicy.CanUploadToProject(ctx)` on P (project member, P's manager, or Administrator). A task viewer who isn't (for example, a non-member assignee or creator) can see permitted attachments but can't add any.
  - **`CanAddDocumentsAsync`**: `false` if the task isn't visible. `true` if the task has no project. Otherwise the membership rule above.
  - **`AttachDocumentAsync`**:
    1. `NotFound` unless the task is visible and the user can access the document.
    2. If the task has project P:
       - Requester fails the membership rule → `Forbidden`, with nothing changed (checked before the document rules below).
       - Document already in P → allowed.
       - Document in another project → `CrossProject`.
       - Document with no project → allowed only for the owner or a user who `CanManage`, otherwise `Forbidden` with nothing changed. When allowed, set `ProjectId = P` and `LastModifiedDate`.
    3. If the task has no project → allowed, and the project is unchanged.
    4. Insert the `TaskDocument` (a no-op if it already exists) and a TaskAttach activity with `TaskId`, in one save.
    5. After the commit, if the project was newly set, call `DocumentService.NotifyProjectDocumentAddedAsync` (T060).
  - **`DetachDocumentAsync`**: allowed for the attacher or a user who `CanManage`. Delete only the link and add a TaskDetach activity.
  - **`GetTaskDocumentsAsync`** and **`GetAttachableDocumentsAsync`** compose `DocumentAccessPolicy` in SQL. `GetAttachableDocumentsAsync` returns an empty list when `CanAddDocumentsAsync` would be `false`.
- [ ] T100 [US6] Register `builder.Services.AddScoped<ITaskDocumentService, TaskDocumentService>();` in `ContosoDashboard\Program.cs`.
- [ ] T101 [US6] Add `TaskId` support to `UploadDocumentAsync` in `ContosoDashboard\Services\DocumentService.cs`, replacing the T046 placeholder:
  - Load the task through `ITaskService.GetTaskByIdAsync`. If it's not visible → `NotFound`.
  - If the task has a project, **force** `ProjectId` to that project and authorize the upload against it with `CanUploadToProject` (project member, its manager, or Administrator). A task viewer who isn't (for example, a non-member assignee or creator) → `Forbidden`. Check this **before** scanning or storing, so nothing is written.
  - Add the `TaskDocument` (`AttachedByUserId = me`) and a TaskAttach activity in the **same** save as the Document.
- [ ] T102 [US6] Create `ContosoDashboard\Pages\TaskDetails.razor` (`@page "/tasks/{TaskId:int}"`, `@attribute [Authorize]`; R-15):
  - Load the task with `ITaskService.GetTaskByIdAsync`. If it's `null`, show "Task not found or not available."
  - Show the task fields: title, description, status, priority, due date, project, assignee.
  - An **Attachments** card that lists `GetTaskDocumentsAsync` with Preview and Download links, and Remove (through `ConfirmDialog` from T083, with the remove-from-task confirmation text) only when `CanDetach`. Empty state: "No documents are attached to this task."
  - Call `CanAddDocumentsAsync` once on load. Only when it's `true`, show:
    - An **Upload to task** button that opens `DocumentUploadForm` with `TaskId` and `TaskProjectId = task.ProjectId`, so the form hides the project selector for a project task (T052).
    - An **Attach existing** picker from `GetAttachableDocumentsAsync`, with a text filter and the empty state "No documents can be attached to this task."
  - When it's `false` (a non-member viewer of a project task), show the note "Only members of this task's project can add documents to it." instead.
  - Results use `DocumentMessages`: "The document was attached to the task.", "The document was removed from the task.", and the error messages, including `CrossProject` and `Forbidden`.
- [ ] T103 [P] [US6] Replace the `ViewTaskDetails` TODO in `ContosoDashboard\Pages\Tasks.razor` with `Navigation.NavigateTo($"/tasks/{taskId}")`.
- [ ] T104 [P] [US6] Make each task row in `ContosoDashboard\Pages\ProjectDetails.razor` link to `/tasks/{task.TaskId}`.
- [ ] T105 [US6] Verify US6 with `ContosoDashboard.Tests\` and `specs\001-document-upload-management\quickstart.md` §6:
  - `dotnet test ContosoDashboard.Tests --filter "Story=US6"` passes, and `--filter "Story=Foundation|Story=US1|Story=US2"` still passes.
  - Run quickstart.md §6, including all four negative cases (D6 refused, Mateo and task 2, Ni Kang and D7, and the non-member assignee covered by T097 and T098).

**Checkpoint**: User Stories 1–6 work independently.

---

## Phase 9: User Story 7 - Documents on the Dashboard (Priority: P3)

**Goal**: The dashboard home page shows a **Recent Documents** widget with the user's 5 newest uploads (newest first, each linked), and a **My Documents** summary card with the user's upload count.

**Independent Test** (quickstart §7):

- After 6 or more uploads by Ni Kang, the widget shows the 5 newest uploads and the card shows the total.
- Hana, with no uploads, sees the empty state with an upload link and a count of 0.
- Documents shared with Dana never appear in her widget.

### Tests for User Story 7 ⚠️ (stubs first, then tests; stub-dependent tests fail at runtime)

- [ ] T140 [US7] Add compile-only stubs (stub-first rule): `GetRecentDocumentsAsync(int requestingUserId, int count = 5)` on `IDocumentService` with a `throw new NotImplementedException()` body in `ContosoDashboard\Services\DocumentService.cs`, and the property `public int TotalDocuments { get; set; }` on `DashboardSummary` in `ContosoDashboard\Services\DashboardService.cs` (not filled yet, so the count test fails at runtime). **Done when** `dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj` succeeds with no new errors or warnings.
- [ ] T106 [P] [US7] Write `ContosoDashboard.Tests\DashboardDocumentTests.cs` (LocalDb collection; use a throwaway user from `DocumentTestData.AddUserAsync`, so other tests' uploads don't interfere):
  - **6 uploads**: `GetRecentDocumentsAsync(user)` returns exactly the 5 newest, ordered by `UploadedDate` descending.
  - **Count**: `DashboardService.GetDashboardSummaryAsync(user).TotalDocuments == 6`.
  - **Shared documents don't count**: a document shared **with** the user affects neither the widget nor the count.
  - **Zero uploads**: an empty list and a count of 0.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings, and never with a compile error. New tests that call stubbed members fail at runtime (red) until the implementation tasks replace the stubs; new tests of behavior that is already implemented may pass. Trait: `[Trait("Story", "US7")]` on the class or each method.

### Implementation for User Story 7

- [ ] T107 [US7] Implement `GetRecentDocumentsAsync` (stubbed in T140) in `ContosoDashboard\Services\DocumentService.cs`: `UploadedByUserId == me`, ordered by `UploadedDate` descending then `DocumentId` descending, `Take(Math.Clamp(count, 1, 20))`, projected to `DocumentSummary`.
- [ ] T108 [P] [US7] Fill `DashboardSummary.TotalDocuments` (added in T140) in `GetDashboardSummaryAsync` with `CountAsync(d => d.UploadedByUserId == userId)` in `ContosoDashboard\Services\DashboardService.cs` (FR-032).
- [ ] T109 [US7] Update `ContosoDashboard\Pages\Index.razor`:
  - Add a **My Documents** summary card (`bi-file-earmark-text`, value `summary.TotalDocuments`, linking to `/documents`) beside the existing cards.
  - Add a **Recent Documents** widget that lists `GetRecentDocumentsAsync` items (title linking to `/documents/{id}`, category, upload date).
  - Show the empty state "You haven't uploaded any documents yet." with a link to `/documents`.

  Depends on T107 and T108.
- [ ] T110 [US7] Verify US7: `dotnet test ContosoDashboard.Tests --filter "Story=US7"` passes, `--filter "Story=Foundation|Story=US1"` still passes, and quickstart.md §7 passes.

**Checkpoint**: User Stories 1–7 work independently.

---

## Phase 10: User Story 8 - Team Lead Oversight of Team Documents (Priority: P3)

**Goal**: Team Leads, and PMs through role inheritance, get a **Team Documents** view of uploads by users who are **currently** in their department and **currently** hold the Employee role. They can view, download, edit metadata of, and delete those documents, but not replace or share them. Administrators see Employee uploads from all departments, with a department filter. Employees are denied.

**Independent Test** (quickstart §8):

- Floris's Team Documents lists Ni Kang's and Dana's uploads but not Camille's. Floris can edit and delete them, but can't replace or share them.
- Ni Kang gets Access Denied on `/documents/team`.
- Floris gets "not found" for Mateo's personal document.
- After Dana moves to Marketing, her documents leave Floris's view and appear in Hana's.

### Tests for User Story 8 ⚠️ (stubs first, then tests; stub-dependent tests fail at runtime)

- [ ] T141 [US8] Add `GetTeamDocumentsAsync(int requestingUserId, DocumentQuery query)` and `GetDepartmentsAsync(int requestingUserId)` to `IDocumentService`, with `throw new NotImplementedException()` bodies in `ContosoDashboard\Services\DocumentService.cs` (stub-first rule). **Done when** `dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj` succeeds with no new errors or warnings.
- [ ] T111 [US8] Write `ContosoDashboard.Tests\DocumentAuthorizationMatrixTests.TeamOversight.cs` (partial class). **Hard dependency: US4 and US5 must be complete**, because these tests call `UpdateMetadataAsync`, `DeleteDocumentAsync`, and `ReplaceFileAsync` (US4) and `ShareWithUsersAsync` (US5). Use **throwaway** users from `DocumentTestData.AddUserAsync` for anything that changes department or role.
  - **Floris's view**: `GetTeamDocumentsAsync(Floris)` includes personal uploads by Engineering Employees (4, 5) and **excludes** uploads by Camille (PM, Engineering) and by Marketing users.
  - **Floris managing Ni Kang's personal document**:
    - `UpdateMetadataAsync` and `DeleteDocumentAsync` → `Ok`.
    - `ReplaceFileAsync` and `ShareWithUsersAsync` → `Forbidden`.
  - **Outside the department**: Floris on Mateo's personal document → `NotFound`.
  - **PM in the same department**: Floris on a personal document by a throwaway ProjectManager in Engineering → `NotFound` and not listed (US8 AS5).
  - **Employee**: `GetTeamDocumentsAsync(NiKang)` → empty.
  - **Administrator**: `GetTeamDocumentsAsync(Admin)` returns Employee uploads from every department, and `Department = "Marketing"` filters to Marketing.
  - **Team Lead without a department** → empty.
  - **Role inheritance**: Camille (PM, Engineering) also sees Engineering Employees' uploads.
  - **Department move (US8 AS7)**: move a throwaway Employee from Engineering to Marketing. Floris loses the view and access. Hana gains them for the Employee's **earlier** uploads.
  - **Role change**: promote a throwaway Employee to TeamLead. Their uploads leave Floris's oversight.
  - Update the header table for the "Team Documents view" row and the Team Lead "team members' uploads" cells.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings, and never with a compile error. New tests that call stubbed members fail at runtime (red) until the implementation tasks replace the stubs; new tests of behavior that is already implemented may pass. Trait: `[Trait("Story", "US8")]` on each test method.
- [ ] T142 [P] [US8] Write `ContosoDashboard.Tests\DocumentDepartmentsTests.cs` (LocalDb collection) for `GetDepartmentsAsync` (contracts\service-interfaces.md):
  - **Administrator**: returns distinct, non-empty departments ordered by name, including "Engineering" and "Marketing". A throwaway user with an empty department (from `DocumentTestData.AddUserAsync`) adds no empty entry.
  - **Team Lead or PM**: Floris (TeamLead, Engineering) and Camille (ProjectManager, Engineering) each get `["Engineering"]`, and Hana (TeamLead, Marketing) gets `["Marketing"]`.
  - **No department**: a throwaway TeamLead without a department gets an empty list.
  - **Employee and unknown user**: Ni Kang (4) and user 999999 get an empty list.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings, and never with a compile error. New tests that call stubbed members fail at runtime (red) until the implementation tasks replace the stubs; new tests of behavior that is already implemented may pass. Trait: `[Trait("Story", "US8")]` on the class or each method.

### Implementation for User Story 8

- [ ] T112 [US8] Implement `GetTeamDocumentsAsync` (stubbed in T141) in `ContosoDashboard\Services\DocumentService.cs`, based on the requester's live role:
  - **Employee**: an empty result.
  - **TeamLead or ProjectManager**: `DocumentAccessPolicy.IsTeamOversight(ctx)` predicate. Empty if the department is null or empty.
  - **Administrator**: `d.UploadedByUser.Role == UserRole.Employee`, optionally filtered by `query.Department`.

  Then apply filters, sort, and paging, with uploader names projected.
- [ ] T143 [US8] Implement `GetDepartmentsAsync` (stubbed in T141) in `ContosoDashboard\Services\DocumentService.cs`, from the requester's live `DocumentUserContext`:
  - **Administrator**: `Users.Where(u => u.Department != null && u.Department != "").Select(u => u.Department).Distinct().OrderBy(d => d)`, run in SQL.
  - **TeamLead or ProjectManager**: a one-item list with their own department, or an empty list if they have none.
  - **Employee** or unknown user: an empty list.

  Makes T142 pass.
- [ ] T113 [US8] Create `ContosoDashboard\Pages\TeamDocuments.razor` (`@page "/documents/team"`, `@attribute [Authorize(Policy = "TeamLead")]`):
  - A `DocumentList` with `ShowUploader = true`, bound to `GetTeamDocumentsAsync`, with the same sort and filter options as My Documents.
  - For Administrators (`AuthorizeView Policy="Administrator"`), a department select whose choices come from `IDocumentService.GetDepartmentsAsync(userId)` (T143). The page never injects `ApplicationDbContext` (Principle IV). The selected value goes into `DocumentQuery.Department`.
  - Rows link to `/documents/{id}`, where the flags drive Edit and Delete.
  - The empty state "No team documents found."

  Depends on T112 and T143.
- [ ] T114 [P] [US8] Add a **Team Documents** item (`/documents/team`, icon `bi-people-fill`) inside `<AuthorizeView Policy="TeamLead">` in `ContosoDashboard\Shared\NavMenu.razor`.
- [ ] T115 [US8] Verify US8 with `ContosoDashboard.Tests\` and `specs\001-document-upload-management\quickstart.md` §8:
  - `dotnet test ContosoDashboard.Tests --filter "Story=US8"` passes, and `--filter "Story=Foundation|Story=US1|Story=US4|Story=US5"` still passes.
  - Run quickstart.md §8, including the Administrator's department filter, Access Denied for Ni Kang, and the department move (change Dana back to Engineering afterward).

**Checkpoint**: User Stories 1–8 work independently.

---

## Phase 11: User Story 9 - Document Activity Audit and Reports (Priority: P3)

**Goal**: Administrators open **Document Reports** for an inclusive UTC date range and see the most uploaded document types, the most active uploaders, access patterns (the top downloaded or previewed documents, and per-day counts), and a paged, filterable activity log. Records of deleted documents are still included. Non-Administrators are denied.

**Independent Test** (quickstart §9):

- After the earlier sections' actions, the Admin's activity log shows every action with the user, action, title, ID, and time, and the reports' totals match.
- Entries for deleted D2 are still present.
- Rafael gets Access Denied, and the menu item is hidden from him.

### Tests for User Story 9 ⚠️ (stubs first, then tests; stub-dependent tests fail at runtime)

- [ ] T144 [US9] Create `ContosoDashboard\Services\DocumentReportService.cs` with compile-only stubs (stub-first rule): the records `DocumentTypeCount`, `UploaderCount`, `DocumentAccessCount`, `DailyAccessCount`, `AccessPatternReport`, and `ActivityLogEntry` (real), the interface `IDocumentReportService` from contracts\service-interfaces.md, and the class `DocumentReportService` with its constructor (`ApplicationDbContext`) and every member body `throw new NotImplementedException()`. **Done when** `dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj` succeeds with no new errors or warnings.
- [ ] T116 [P] [US9] Add `CreateReportService(ApplicationDbContext ctx)` to `ContosoDashboard.Tests\TestServiceFactory.cs`. It returns `DocumentReportService`.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings.
- [ ] T117 [P] [US9] Write `ContosoDashboard.Tests\DocumentReportServiceTests.cs` (LocalDb collection). Arrange `DocumentActivity` rows **directly**, with a distinctive future date range so other tests' rows don't interfere.
  - **Most uploaded types**: `GetMostUploadedTypesAsync` ranks by Upload count and returns `DisplayName`s from `DocumentFileRules` ("PDF", "Word", and so on).
  - **Most active uploaders**: `GetMostActiveUploadersAsync` ranks users by Upload count, with their display names.
  - **Access patterns**: `GetAccessPatternsAsync` returns the top 10 documents by Download + Preview, and per-UTC-day Download and Preview counts.
  - **Activity log**: `GetActivityLogAsync` filters by action and pages correctly.
  - **Deleted documents**: rows whose `DocumentId` has no `Documents` row are still reported (US9 AS5).
  - **Invalid range**: `from > to`, or a range over 366 days, gives an empty result.
  - **Non-Administrators**: for each non-Administrator seeded role (Employee 4, TeamLead 3, ProjectManager 2), **every** method returns `null` (US9 AS6). Update the header table for the "Activity records and reports" row.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings, and never with a compile error. New tests that call stubbed members fail at runtime (red) until the implementation tasks replace the stubs; new tests of behavior that is already implemented may pass. Trait: `[Trait("Story", "US9")]` on the class or each method.

### Implementation for User Story 9

- [ ] T118 [US9] Implement `DocumentReportService` in `ContosoDashboard\Services\DocumentReportService.cs` (stubbed in T144), per contracts\service-interfaces.md:
  - **Authorization**: every method first checks the live `Role == Administrator` from `Users`, and returns `null` otherwise.
  - **Range**: inclusive UTC days, `from ≤ to`, at most 366 days. Otherwise return an empty result.
  - **Data source**: queries use `DocumentActivities` only, grouped in SQL. Take the title for top documents from the latest snapshot.
  - **Activity log**: joins `Users.DisplayName` and is paged with `PagedResult<ActivityLogEntry>`.
- [ ] T119 [US9] Register `builder.Services.AddScoped<IDocumentReportService, DocumentReportService>();` in `ContosoDashboard\Program.cs`.
- [ ] T120 [US9] Create `ContosoDashboard\Pages\DocumentReports.razor` (`@page "/admin/document-reports"`, `@attribute [Authorize(Policy = "Administrator")]`):
  - From and To date inputs labelled "(UTC)", defaulting to the last 30 days, and a Run button.
  - Three report tables: Most uploaded types, Most active uploaders, and Access patterns (top documents, plus the per-day table).
  - A paged activity log with an action filter, showing time, user, action, document title and ID, task ID, and details.
  - If the service returns `null`, show the existing Access Denied pattern. Empty tables and an empty log show "No activity in this date range."
- [ ] T121 [P] [US9] Add a **Document Reports** item (`/admin/document-reports`, icon `bi-bar-chart`) inside `<AuthorizeView Policy="Administrator">` in `ContosoDashboard\Shared\NavMenu.razor`.
- [ ] T122 [US9] Verify US9 with `ContosoDashboard.Tests\` and `specs\001-document-upload-management\quickstart.md` §9:
  - `dotnet test ContosoDashboard.Tests --filter "Story=US9"` passes, and `--filter "Story=Foundation|Story=US1"` still passes.
  - Run quickstart.md §9, including Access Denied for Rafael.

**Checkpoint**: All user stories work independently.

---

## Phase 12: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, completeness checks across stories, security review, and the final verification gates

- [ ] T123 [P] Update `README.md` (repository root) and keep the "TRAINING PURPOSES ONLY" disclaimer:
  - **Feature overview**: a Document Upload and Management section listing the routes `/documents`, `/documents/{id}`, `/documents/shared`, `/documents/search`, `/documents/team`, `/admin/document-reports`, `/tasks/{id}`, and the download and preview endpoints.
  - **Sample Data**: users 5–8 (Dana Whitfield, Mateo Alvarez, Hana Sato, Rafael Costa), project 2 "Marketing Website Refresh", and tasks 4–5.
  - **Known Limitations**:
    - The **simulated malware scanner detects only the EICAR test file and is not a production malware control** (FR-038).
    - Files are stored on the local filesystem under `ContosoDashboard\AppData\uploads`, outside `wwwroot`.
    - The Development-only automatic database recreate when the schema predates the feature. When it recreates the database, it also **clears `ContosoDashboard\AppData\uploads`**, so files never outlive their records (and any earlier uploads are lost along with their rows).
    - **Leftover files from a failed cleanup** (spec FR-011): file removal after a failed save, after a replace, or after a delete is best effort. If it fails, the leftover file is logged as a warning with its storage key, is never served or listed (no record refers to it), and can be deleted by hand.
    - Microsoft Defender may quarantine EICAR test files.
  - **Database Issues**: the manual reset (`sqllocaldb stop mssqllocaldb`, `sqllocaldb delete mssqllocaldb`, or `dotnet ef database drop --force`), plus an instruction to **delete `ContosoDashboard\AppData\uploads` whenever the database is reset manually** (the automatic Development recreate does this itself).
  - **Running Tests**: `dotnet test .\ContosoDashboard.Tests`. Needs LocalDB, and network access for the first package restore only.
- [ ] T124 [P] Write `ContosoDashboard.Tests\DocumentActivityCompletenessTests.cs` (SC-008). Run one of each action through the real services: Upload, Download, Preview, MetadataEdit, Replace, Share, ShareRemoved, TaskAttach, TaskDetach, and Delete. Assert exactly one `DocumentActivity` per action, with the correct `UserId`, `Action`, `DocumentId`, `DocumentTitle` snapshot, `OccurredAt` within the test window, and `TaskId` for the task actions. Needs US1, US3, US4, US5, and US6.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings, and the new tests pass (they're written after the implementation). Trait: `[Trait("Story", "Polish")]` on the class or each method.
- [ ] T125 [P] Extend `ContosoDashboard.Tests\DocumentPerformanceTests.cs` with SC-002 timings for `GetProjectDocumentsAsync` (500 documents in one project), `GetTeamDocumentsAsync` (500 Employee uploads in the Team Lead's department), and `GetSharedWithMeAsync` (500 documents shared with the user). Each must take **< 2 s** after a warm-up.
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings, and the new tests pass (they're written after the implementation). Trait: `[Trait("Story", "Polish")]` on each test method.
- [ ] T126 Complete the Permissions-coverage header table in `ContosoDashboard.Tests\DocumentAuthorizationMatrixTests.cs` (SC-006). Classify each of the 44 cells (11 operations × 4 roles):
  - **Restricted cell** (34 cells: the cell limits the operation for that role): map it to at least one **boundary denial** test, where a request just outside what the cell allows is denied. Examples: an Employee editing a P1 document she didn't upload → `Forbidden`; a Team Lead on a Marketing Employee's document → `NotFound`; an Administrator replacing or sharing someone else's document → `Forbidden`; an Employee who can view a project task but isn't a project member attaching to it → `Forbidden`.
  - **Full-grant cell** (10 cells: "Upload a personal document" for all four roles, and the Administrator's "Upload a document to a project", "Access a document", "Edit metadata", "Delete", "Team Documents view", and "Activity records and reports"): mark it "N/A for denial" and map it to a **positive** test that shows the grant works.

  Add any missing boundary denial or positive tests as methods in the matching partial file, each with `[Trait("Story", "Polish")]`. Also add the **FR-023 share-recipient denials moved from T088** (they need US4 members) to `ContosoDashboard.Tests\DocumentAuthorizationMatrixTests.Sharing.cs`: a recipient through a user share and a recipient through a team share each get `Forbidden` for `UpdateMetadataAsync`, `DeleteDocumentAsync`, and `ReplaceFileAsync`, and the document is unchanged. Re-run `dotnet test ContosoDashboard.Tests --filter "FullyQualifiedName~DocumentAuthorizationMatrixTests"`. Needs US1–US9 (at least US4 and US5 for the recipient denials).
  - **Done when**: the solution builds (`dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj`) with no new errors or warnings, and the new tests pass (they're written after the implementation). Trait: `[Trait("Story", "Polish")]` on each test method.
- [ ] T127 Security review sweep of `ContosoDashboard\Pages\` and `ContosoDashboard\Shared\`:
  - No new page or component injects `ApplicationDbContext`.
  - Every new `.razor` page has `[Authorize]` or a policy attribute, and both `.cshtml.cs` endpoints have `[Authorize]`.
  - The security-header block and CSP string in `ContosoDashboard\Program.cs` are byte-identical to the pre-feature version (`git diff` shows no change to those lines).
  - No path under `ContosoDashboard\wwwroot\` is written by the feature.
  - Every new public service method takes `requestingUserId` and checks it (constitution Review gate).
- [ ] T128 [P] Repository hygiene checks across the tracked files and `.gitignore`:
  - **EICAR**: in PowerShell, build the search pattern at runtime from the two EICAR halves. Confirm that **no** tracked file contains the contiguous signature (`git grep -F` with the joined string returns nothing).
  - **Uploads**: `git status --ignored` shows `ContosoDashboard/AppData/` as ignored, and no upload is staged.
- [ ] T129 Build gate: `dotnet build ContosoDashboard\ContosoDashboard.csproj` and `dotnet build ContosoDashboard.Tests\ContosoDashboard.Tests.csproj` succeed with **no new warnings** compared with the T005 baseline. Fix any nullable warnings in the new files.
- [ ] T130 Run the full automated suite, **unfiltered**, with `dotnet test .\ContosoDashboard.Tests` from the repository root. All tests pass: foundational, US1–US9, completeness, timing, and performance (quickstart §11). This is the only verification step that runs every story trait at once.
- [ ] T131 Run the full quickstart.md validation, §0–§12, on a clean database:
  - §0.1 clean start (including deleting `AppData\uploads`), seeded users, and test files.
  - Every story section with its negative authorization checks.
  - §10 cross-cutting checks: offline, headers unchanged, Department claim, no orphans (the `Documents` row count equals the file count; the Development schema guard clears `AppData\uploads` when it recreates the database, and any logged FR-011 leftover file is the only accepted exception), and the notification preference.
  - §12 documentation checks.

  Record any deviations as follow-up tasks.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies. Start immediately.
- **Foundational (Phase 2)**: depends on Setup. **Blocks every user story.** Inside it, 2a (schema) → 2b (contracts) → 2c (T132 stubs, then the harness and runtime-red tests) → 2d (implementation, which turns 2c green, plus the shared `ConfirmDialog`, T083).
- **US1 (Phase 3)**: depends on Foundational. It's the backbone: it creates `DocumentService.cs`, `DocumentList.razor`, `DocumentUploadForm.razor`, `DocumentDetails.razor`, and the download endpoint, which every later story extends.
- **US2–US9 (Phases 4–11)**: each depends on Foundational **and US1**. They're otherwise independent of each other, with the exceptions in the next section (US6 needs T060 from US2; US8 needs US4 and US5).
- **Polish (Phase 12)**: depends on every story you intend to ship. T124 specifically needs US1, US3, US4, US5, and US6. T126 needs at least US4 and US5 (for the FR-023 recipient denials moved from T088).

### User Story Dependencies

```text
Setup ─► Foundational ─► US1 (P1, MVP) ─┬─► US2 (P2) ──────────────────────────────┐
                                        ├─► US3 (P2) ──────────────────────────────┤
                                        ├─► US4 (P2) ──────────────────────────────┤
                                        ├─► US5 (P3) ──────────────────────────────┤
                                        ├─► US6 (P3)  [needs T060 from US2] ───────┤
                                        ├─► US7 (P3) ──────────────────────────────┤
                                        ├─► US8 (P3)  [needs US4 and US5 for T111] ┤
                                        └─► US9 (P3) ──────────────────────────────┴─► Polish
```

| Story | Hard dependencies | Soft integration (works without it) |
|-------|-------------------|-------------------------------------|
| US1 | Foundational | — |
| US2 | US1 | — |
| US3 | US1 | Search finds shared or project documents once US2 and US5 exist |
| US4 | US1 | Delete cascades shares and attachments (the schema handles that even without US5 and US6) |
| US5 | US1 | Uses US3's sort and filter controls in Shared with Me if present. The FR-023 recipient denials that call US4 members live in T126 (Polish), so US5 doesn't depend on US4. |
| US6 | US1, **T060 (US2)** for the project-added notification helper | Uses `ConfirmDialog` (T083), which is now built in Phase 2, so US6 doesn't wait for US4. |
| US7 | US1 | — |
| US8 | US1, **US4 and US5 (for T111)**: T111 calls `UpdateMetadataAsync`, `DeleteDocumentAsync`, and `ReplaceFileAsync` (US4) and `ShareWithUsersAsync` (US5) | Managers' edit and delete in the UI come from US4. |
| US9 | US1 | Report data is richer once US3 (Preview), US4, US5, and US6 write their activity rows. T117 arranges rows directly, so it doesn't depend on them. |

### Shared-file serialization (not parallel across stories)

These files are touched by several stories, so edits to them must be serialized even when the stories otherwise run in parallel:

| File | Tasks |
|------|-------|
| `ContosoDashboard\Services\DocumentService.cs` | T133, T045–T048, T135, T059–T060, T136, T067–T069, T137, T080–T082, T138, T091, T101, T140, T107, T141, T112, T143 |
| `ContosoDashboard\Pages\DocumentDetails.razor` | T054, T074, T084, T093 |
| `ContosoDashboard\Shared\NavMenu.razor` | T055, T075, T094, T114, T121 |
| `ContosoDashboard\Program.cs` | T016, T037, T038, T049, T090, T100, T119 |
| `ContosoDashboard\Pages\ProjectDetails.razor` | T061, T104 |
| `ContosoDashboard.Tests\TestServiceFactory.cs` | T023, T041, T086, T096, T116 |
| `ContosoDashboard\Data\ApplicationDbContext.cs` | T014, T015 |

### Within Each User Story

- **Stub task first** (T133, T135–T141, T144; T132 for Foundational): add the members the tests call with `throw new NotImplementedException()` bodies, so the solution builds.
- Then the tests (including test-factory additions). The solution must build (never a compile error). New tests that call stubbed members fail **at runtime** (red); new tests of behavior that is already implemented may pass.
- Then service members (replacing the stub bodies), then DI registration, then endpoints and pages, then navigation.
- Each story ends with a verification task (its `Story` trait, plus `Foundation` and its hard dependencies' traits, and its quickstart section) before you move on. A story's red tests never block another story's verification, because each verification filters by trait.

---

## Parallel Opportunities

- **Setup**: T002 ∥ T003 (T004 follows T003).
- **Foundational 2a**: T006–T013 (8 model files) in parallel. Then T014 → T015 → T016 in sequence.
- **Foundational 2b**: T017 ∥ T018 ∥ T019.
- **Foundational 2c**: T132 (stubs) first. Then T020, then T021–T030 in parallel.
- **Foundational 2d**: T031–T036 and T083 in parallel. Then T037 → T038 → T039.
- **Per story**: the stub task first, on its own. Then every test task marked [P] in parallel. Then UI files marked [P] (endpoints, components, NavMenu) in parallel with each other once their service members exist.
- **Across stories** (after US1, with multiple developers): US2, US3, US7, and US9 have the fewest shared-file collisions. Coordinate `DocumentService.cs` and `NavMenu.razor` edits using the table above. Start US8 only after US4 and US5 are complete.

### Parallel Example: User Story 1

```text
# Stub first (sequential):
Task: "T133 Create ContosoDashboard\Services\DocumentService.cs with US1 stubs"

# Then tests (all [P], different files):
Task: "T040 Create test doubles in ContosoDashboard.Tests\Fakes\TestDoubles.cs"
Task: "T041 Add CreateDocumentService to ContosoDashboard.Tests\TestServiceFactory.cs"
Task: "T042 Write ContosoDashboard.Tests\DocumentUploadTests.cs"
Task: "T043 Write ContosoDashboard.Tests\DocumentWriteOrderTests.cs"
Task: "T044 Write ContosoDashboard.Tests\DocumentAuthorizationMatrixTests.OwnDocuments.cs"
Task: "T134 Write ContosoDashboard.Tests\DocumentUploadTimingTests.cs"

# After T045–T049 (DocumentService.cs and DI, sequential):
Task: "T050 Create ContosoDashboard\Pages\DocumentDownload.cshtml(.cs)"
Task: "T051 Create ContosoDashboard\Shared\DocumentList.razor"
Task: "T052 Create ContosoDashboard\Shared\DocumentUploadForm.razor"
Task: "T054 Create ContosoDashboard\Pages\DocumentDetails.razor"
Task: "T055 Add Documents item to ContosoDashboard\Shared\NavMenu.razor"
```

### Parallel Example: User Story 3

```text
# Stub first: T136
Task: "T064 Write ContosoDashboard.Tests\DocumentSearchAndQueryTests.cs"
Task: "T065 Write ContosoDashboard.Tests\DocumentPreviewTests.cs"
Task: "T066 Write ContosoDashboard.Tests\DocumentPerformanceTests.cs"
# After T067–T069:
Task: "T070 Create ContosoDashboard\Pages\DocumentPreview.cshtml(.cs)"
Task: "T071 Add sort/filter to ContosoDashboard\Shared\DocumentList.razor"
Task: "T072 Create ContosoDashboard\Pages\DocumentSearch.razor"
Task: "T074 Add Preview button to ContosoDashboard\Pages\DocumentDetails.razor"
Task: "T075 Add Search item to ContosoDashboard\Shared\NavMenu.razor"
```

### Parallel Example: User Story 5

```text
# Stub first: T138
Task: "T086 Add CreateShareService to ContosoDashboard.Tests\TestServiceFactory.cs"
Task: "T087 Write ContosoDashboard.Tests\DocumentShareServiceTests.cs"
Task: "T088 Write ContosoDashboard.Tests\DocumentAuthorizationMatrixTests.Sharing.cs"
# After T089–T091:
Task: "T092 Create ContosoDashboard\Pages\SharedDocuments.razor"
Task: "T094 Add Shared with Me item to ContosoDashboard\Shared\NavMenu.razor"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001–T005).
2. Complete Phase 2: Foundational (T006–T039, plus T132 and T083, in document order). This is critical because it blocks every story.
3. Complete Phase 3: User Story 1 (T040–T056, plus T133 and T134, in document order).
4. **Stop and validate**: run the US1 tests (`--filter "Story=US1"`) and quickstart §1, including the Dana negative cases and the storage check.
5. Demo: secure personal upload, My Documents, details, and download.

### Incremental Delivery

1. Setup + Foundational: foundation ready (schema, seed data, claim, infrastructure, policy, `ConfirmDialog`, harness).
2. \+ US1: MVP.
3. \+ US2: project collaboration.
4. \+ US3: find and preview.
5. \+ US4: keep documents accurate. This completes the P2 set.
6. \+ US5, US6, US7, US8, US9 (P3), in any order after the dependencies above. Run T111 (and so US8) after US4 and US5.
7. Polish (T123–T131): README, completeness and coverage tests, security sweep, build and test gates, and the full quickstart.

### Parallel Team Strategy

1. The whole team completes Setup + Foundational together. Split 2a, 2c, and 2d across people (T132 stubs first in 2c).
2. One developer completes US1, the backbone.
3. Then:
   - Developer A: US2 → US6 (US6 needs T060 from US2)
   - Developer B: US3 → US7
   - Developer C: US4 → US9
   - Developer D: US5 → US8 (US8 starts only when Developer C has finished US4, because T111 needs US4 and US5)
4. Serialize edits to the shared files in the table above, for example with short-lived branches merged in document order.
5. Every branch must build at all times (stub-first rule). Each developer verifies with their story's trait filter (plus `Foundation` and the story's hard dependencies), so another story's runtime-red tests never block them. Don't rely on the unfiltered suite until T130.

---

## Task Summary

| Phase | Tasks | [P] | Added or moved after `/speckit.analyze` |
|-------|-------|-----|------------------------------------------|
| 1. Setup | 5 | 2 | — |
| 2. Foundational | 36 | 28 | T132 (stubs), T083 (moved in from US4) |
| 3. US1 Upload and access my own documents (P1, MVP) | 19 | 11 | T133 (stubs), T134 (SC-001 timing test) |
| 4. US2 Share within a project (P2) | 8 | 4 | T135 (stubs) |
| 5. US3 Find and preview (P2) | 14 | 8 | T136 (stubs); SC-004 timing added to T066 |
| 6. US4 Edit, replace, and delete (P2) | 9 | 3 | T137 (stubs); T083 moved out to Phase 2 |
| 7. US5 Share with people and teams (P3) | 11 | 5 | T138 (stubs) |
| 8. US6 Attach documents to tasks (P3) | 11 | 5 | T139 (stubs) |
| 9. US7 Dashboard (P3) | 6 | 2 | T140 (stubs) |
| 10. US8 Team Lead oversight (P3) | 8 | 2 | T141 (stubs), T142 (`GetDepartmentsAsync` tests), T143 (`GetDepartmentsAsync`); T111 no longer [P] |
| 11. US9 Activity audit and reports (P3) | 8 | 3 | T144 (stubs) |
| 12. Polish | 9 | 4 | — |
| **Total** | **144** | **77** | 13 new tasks (T132–T144), 1 moved task (T083) |

- **IDs**: T001–T144, unique, with no gaps. Execution order follows document order, not numeric order.
- **MVP scope**: Phases 1–3 (60 tasks).
- **Independent test criteria**: each story phase's "Independent Test" block plus its verification task, filtered by the story's `Story` trait.

---

## Notes

- [P] tasks touch different files and have no dependency on an incomplete task.
- [Story] labels map tasks to spec.md user stories for traceability.
- Task IDs are stable. New and moved tasks keep their IDs, and the document order is the execution order.
- The stub-first rule keeps the solution building after every task. A test task is never "done" while the test project has compile errors.
- LocalDB is required for `dotnet test`. Each test run creates and drops its own `ContosoDashboard_Tests_{guid}` database and never touches the developer's `ContosoDashboard` database.
- EICAR bytes exist only in memory in the tests. Never write them to disk in the test project.
- Stop at any checkpoint to validate a story on its own.
- Avoid vague tasks, edits to the same file in parallel, and dependencies between stories that break their independence.

