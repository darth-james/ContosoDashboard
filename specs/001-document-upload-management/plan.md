# Implementation Plan: Document Upload and Management

**Branch**: `001-document-upload-management` | **Date**: 2026-10-16 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/001-document-upload-management/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command; its definition describes the execution workflow.

## Summary

Add a secure, offline document repository to ContosoDashboard. Users can upload one or more files (allow-listed types, 25 MB max, scanned by a replaceable offline malware scanner), then organize, find, preview, edit, replace, delete, and share them, and attach them to projects and tasks. Team Leads, Project Managers, and Administrators get oversight within the scope of the Permissions table. Every action is written to an activity log that Administrators can report on.

The technical approach extends the existing layered Blazor Server app without rewrites:

- **Models and data**: new EF Core entities (`Document`, `DocumentTag`, `DocumentShare`, `TaskDocument`, `DocumentActivity`) added to `ApplicationDbContext` and created with `EnsureCreated()`.
- **Infrastructure interfaces**: `IFileStorageService` (local implementation writes outside `wwwroot` under `AppData/uploads`) and `IMalwareScanner` (simulated EICAR scanner, fail closed).
- **Business services**: business rules and authorization live in four new services (`IDocumentService`, `IDocumentShareService`, `ITaskDocumentService`, `IDocumentReportService`). They all share one EF-translatable access policy, so lists, search, and the dashboard can never show documents a user can't access.
- **HTTP endpoints**: download and preview are served by thin, authorization-checked Razor Page endpoints.
- **UI**: new Blazor pages and shared components follow the existing `[Authorize]` and Bootstrap conventions.

Writes always follow this order: generate a unique key, write the file, save the metadata (or compensate on failure).

## Technical Context

**Language/Version**: C# 12 on .NET 8 (`net8.0`, nullable and implicit usings enabled; SDK 8.0.425 installed)

**Primary Dependencies**: ASP.NET Core 8 Blazor Server + Razor Pages, Entity Framework Core 8.0.0 (SqlServer provider), Bootstrap 5.3 + Bootstrap Icons (CDN, already allowed by CSP); no new runtime NuGet packages

**Storage**: SQL Server LocalDB (`(localdb)\mssqllocaldb`, database `ContosoDashboard`) via EF Core `EnsureCreated()` for metadata; local filesystem under `ContosoDashboard/AppData/uploads` (outside `wwwroot`) for file content, behind `IFileStorageService`

**Testing**: Manual quickstart verification with seeded mock users (constitution-mandated). Planned automated tests: an optional `ContosoDashboard.Tests` xUnit project running against a throwaway LocalDB database. It covers service-layer authorization, the write-order rules, and the 500-document performance checks (see Complexity Tracking).

**Target Platform**: Single Windows training workstation, fully offline. Kestrel on `https://localhost:5001` / `http://localhost:5000`, using a modern Chromium-based browser or Firefox with built-in PDF/image viewers.

**Project Type**: Web application. One ASP.NET Core Blazor Server project (`ContosoDashboard/`) with Razor Pages for HTTP-bound flows.

**Performance Goals**:

| Area | Target |
|------|--------|
| Upload (25 MB, including validation and scan) | ≤ 30 s |
| Lists (500 documents) | ≤ 2 s |
| Search (≥ 500 accessible documents) | ≤ 2 s |
| Preview (< 10 MB) | ≤ 3 s |

**Constraints**:

- Offline only: no cloud SDKs and no network calls at runtime.
- Files are never stored under `wwwroot`. Storage names are GUIDs.
- Authorization is enforced in services using the requesting user's ID, with role and department read live from the database.
- Existing security headers (CSP, X-Frame-Options DENY, nosniff, Referrer-Policy, HSTS) stay unchanged.
- `dotnet run` must create and seed the schema with no manual setup.

**Scale/Scope**:

- Training scale: 8 seeded users (4 existing + 4 added for verification) and up to about 500 documents per user. Lists are paged at 25 rows.
- New UI:
  - 6 new Blazor pages: My Documents, Document Details, Shared with Me, Team Documents, Search, and Admin Document Reports.
  - 1 new Blazor page for Task Details.
  - 3 updated pages: Dashboard, Project Details, and Tasks (link to details).
  - 2 Razor Page file endpoints.
- New code: 5 entities, 4 business services, and 2 infrastructure services.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-design evaluation (before Phase 0)

| Principle | Gate | Status | Evidence / Plan Response |
|-----------|------|--------|--------------------------|
| I. Offline-First (NON-NEGOTIABLE) | LocalDB through EF Core | PASS | Reuses `DefaultConnection` LocalDB and `ApplicationDbContext`. |
| I | Files on local FS outside `wwwroot` | PASS | `LocalFileStorageService` writes under `{ContentRoot}/AppData/uploads`. `AppData/` is added to `.gitignore`. |
| I | Existing cookie mock auth | PASS | No identity changes beyond adding the Department claim. |
| I | No cloud SDKs or network dependencies | PASS | No new runtime packages. The scanner is in-process and offline. Pre-existing unused `Microsoft.Identity.Web*` and `OpenIdConnect` package references are out of scope and untouched. |
| I | `dotnet run` creates, seeds, and starts | PASS (with design action) | `EnsureCreated()` does not add tables to an existing database. Research R-03 adds a Development-only schema guard that recreates a stale training database automatically and documents the behavior. |
| II. Infrastructure Abstraction | Storage behind interface + DI | PASS | `IFileStorageService` (`UploadAsync`, `DeleteAsync`, `DownloadAsync`, `GetUrlAsync`) is implemented by `LocalFileStorageService` and registered in `Program.cs`. |
| II | Other infrastructure behind interface | PASS | Malware scanning is behind `IMalwareScanner` (`SimulatedEicarMalwareScanner`). |
| II | UI and business logic depend only on interfaces | PASS | Pages inject the `I*Service` interfaces only. |
| II | Persisted references are relative and portable | PASS | `Document.FilePath` stores a forward-slash relative key `{userId}/{projectId\|personal}/{guid}{ext}` that is valid as a blob name. |
| III. Security by Default | `[Authorize]` / policies on every page | PASS | All new pages use `[Authorize]`. Team Documents uses `Policy = "TeamLead"`. Reports use `Policy = "Administrator"`. File endpoints use `[Authorize]` on the PageModel. |
| III | Service methods take `requestingUserId` and verify access (IDOR) | PASS | Every contract method takes `requestingUserId`. One shared access policy is used for reads, lists, search, the dashboard, and notifications. |
| III | Only authorized data in lists, search, dashboard, notifications | PASS | List and search queries compose the access predicate in SQL. Notification recipients are computed from the same rules. |
| III | Allow-list, size limit, GUID names, no user-supplied paths, protected endpoint | PASS | `DocumentFileRules` handles the allow-list and the 26,214,400-byte limit. Keys use GUIDs. Download and preview go through Razor Page endpoints after an access check. |
| III | Security headers unchanged | PASS | Preview opens in a new top-level tab, so neither `X-Frame-Options` nor CSP needs relaxing (R-06). |
| III | Login issues NameIdentifier, Name, Email, Role, Department | **FAIL → fixed by design** | `Login.cshtml.cs` currently omits Department. The plan adds it (R-09). |
| IV. Layered Architecture | Code in Models/Data/Services/Pages/Shared | PASS | See Project Structure. |
| IV | Pages call services, never `DbContext` | PASS | All data access goes through services. |
| IV | Async end to end; eager loading; indexes | PASS | Queries project to DTOs or use `.Include`. Indexes are listed in data-model.md. |
| IV | `int` PKs; text category; 255-char MIME | PASS | `DocumentId int`, `Category nvarchar(50)` text, `FileType nvarchar(255)`. |
| IV | Write order: unique path → file → metadata | PASS | See the upload, replace, and delete sequences in research R-04. |
| IV | Blazor `IBrowserFile` → `MemoryStream`, metadata first, clear reference | PASS | `DocumentUploadForm` follows the stakeholder pattern, including `@key` reset (R-07). |
| V. Simplicity & Transparency | YAGNI; out-of-scope features excluded | PASS | No versioning, soft delete, quotas, or external integration. |
| V | New abstractions, projects, packages, or patterns justified | PASS (justified) | See Complexity Tracking for `IMalwareScanner`, the result type, the shared access policy, the schema guard, and the optional test project. |
| V | Training shortcuts documented in README | PASS (with design action) | README gains limitations for the simulated EICAR scanner, local storage, and dev-only DB auto-recreate, and the disclaimer stays. |

**Pre-design gate result**: **PASS**. There is one existing violation (the missing Department claim), which the plan fixes. Every new abstraction is justified below. No unresolved `NEEDS CLARIFICATION` items remain after Phase 0 (see [research.md](research.md)).

### Post-design re-evaluation (after Phase 1)

I re-checked each principle against [data-model.md](data-model.md), [contracts/](contracts/), and [quickstart.md](quickstart.md).

| Principle | Result | Notes |
|-----------|--------|-------|
| I | PASS | Contracts use no cloud types. `GetUrlAsync` is never called by the feature, and the local implementation declines it (R-05), so no direct-file URL exists offline. Schema guard keeps `dotnet run` zero-setup. |
| II | PASS | Business contracts reference only `IFileStorageService` and `IMalwareScanner`. The storage key format is valid for both local paths and blob names. Swapping implementations is a DI registration change. |
| III | PASS | Each read and write contract defines its denial behavior. Inaccessible and nonexistent documents return the same `NotFound`. Manage-only actions return `Forbidden` to users who can view but not manage. Owner-only operations (replace, share, unshare) are enforced in services. The quickstart includes negative cases for every role and the direct-URL check. Preview is served with server-derived MIME, `nosniff`, and `Cache-Control: no-store`. |
| IV | PASS | Entities use `int` keys and text category. Cascade paths are checked so SQL Server doesn't reject them for multiple cascade paths (data-model.md). Indexes cover every filter and sort column. Upload, replace, and delete sequences match the required write order, with compensation. |
| V | PASS | No new runtime packages. The only new project (tests) is optional and justified. README documentation items are listed in quickstart.md §0 and in the tasks hand-off. |

**Post-design gate result**: **PASS**. Nothing new needs justification beyond the Complexity Tracking table.

## Project Structure

### Documentation (this feature)

```text
specs/001-document-upload-management/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
│   ├── infrastructure-interfaces.md   # IFileStorageService, IMalwareScanner
│   ├── service-interfaces.md          # IDocumentService, IDocumentShareService, ITaskDocumentService, IDocumentReportService + DTOs
│   ├── http-endpoints.md              # Authorization-checked download/preview endpoints
│   └── ui-routes.md                   # Blazor routes, policies, navigation, component contracts
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
ContosoDashboard/
├── ContosoDashboard.csproj                 # unchanged (no new runtime packages)
├── Program.cs                              # + DI registrations, DocumentStorage options, Dev-only schema guard
├── appsettings.json                        # + "DocumentStorage": { "RootPath": "AppData/uploads" }
├── AppData/uploads/                        # runtime only, git-ignored; {userId}/{projectId|personal}/{guid}{ext}
├── Models/
│   ├── Document.cs                         # NEW entity
│   ├── DocumentTag.cs                      # NEW entity
│   ├── DocumentShare.cs                    # NEW entity
│   ├── TaskDocument.cs                     # NEW entity (task attachment link)
│   ├── DocumentActivity.cs                 # NEW entity + DocumentActivityAction enum
│   ├── DocumentCategories.cs               # NEW fixed text list
│   └── Notification.cs                     # + DocumentId (int?), + NotificationType.DocumentShared/DocumentAdded
├── Data/
│   └── ApplicationDbContext.cs             # + DbSets, relationships, indexes, check constraint, extra seed users/project/tasks
├── Services/
│   ├── IFileStorageService.cs              # NEW interface
│   ├── LocalFileStorageService.cs          # NEW System.IO implementation
│   ├── IMalwareScanner.cs                  # NEW interface + MalwareScanResult
│   ├── SimulatedEicarMalwareScanner.cs     # NEW training scanner
│   ├── DocumentFileRules.cs                # NEW allow-list, size limit, MIME map, file-name sanitizing
│   ├── DocumentAccessPolicy.cs             # NEW EF-translatable access/manage predicates (live role + department)
│   ├── DocumentModels.cs                   # NEW DTOs/requests/results (DocumentOperationResult, DocumentQuery, PagedResult…)
│   ├── DocumentService.cs                  # NEW IDocumentService + implementation
│   ├── DocumentShareService.cs             # NEW IDocumentShareService + implementation
│   ├── TaskDocumentService.cs              # NEW ITaskDocumentService + implementation
│   ├── DocumentReportService.cs            # NEW IDocumentReportService + implementation (Admin)
│   ├── NotificationService.cs              # + CreateNotificationsAsync (honors InAppNotificationsEnabled)
│   └── DashboardService.cs                 # + DashboardSummary.TotalDocuments
├── Pages/
│   ├── Login.cshtml.cs                     # + Department claim
│   ├── Documents.razor                     # NEW /documents (My Documents + upload)
│   ├── DocumentDetails.razor               # NEW /documents/{id:int}
│   ├── SharedDocuments.razor               # NEW /documents/shared
│   ├── TeamDocuments.razor                 # NEW /documents/team (Policy TeamLead)
│   ├── DocumentSearch.razor                # NEW /documents/search
│   ├── DocumentReports.razor               # NEW /admin/document-reports (Policy Administrator)
│   ├── TaskDetails.razor                   # NEW /tasks/{taskId:int} (+ attachments panel)
│   ├── DocumentDownload.cshtml(.cs)        # NEW /documents/{id:int}/download (Razor Page endpoint)
│   ├── DocumentPreview.cshtml(.cs)         # NEW /documents/{id:int}/preview (Razor Page endpoint)
│   ├── Index.razor                         # + Recent Documents widget, Documents summary card
│   ├── ProjectDetails.razor                # + Project Documents card (list + upload to project)
│   ├── Tasks.razor                         # ViewTaskDetails → navigate to /tasks/{id}
│   └── Notifications.razor                 # + "Open document" link for document notifications
└── Shared/
    ├── NavMenu.razor                       # + Documents, Shared with Me, Search, Team Documents (TeamLead), Document Reports (Admin)
    ├── DocumentList.razor                  # NEW reusable table (sort/filter/paging/actions)
    ├── DocumentUploadForm.razor            # NEW multi-file upload with per-file metadata, progress, results
    └── ConfirmDialog.razor                 # NEW lightweight Blazor-rendered confirmation (no JS)

ContosoDashboard.Tests/                     # OPTIONAL (see Complexity Tracking)
├── ContosoDashboard.Tests.csproj           # xUnit; references ContosoDashboard
├── LocalDbFixture.cs                       # creates/drops a uniquely named LocalDB database
├── DocumentAuthorizationMatrixTests.cs     # SC-006 negative cases per role × operation
├── DocumentWriteOrderTests.cs              # orphan prevention, fail-closed scan, replace keeps old file
└── DocumentPerformanceTests.cs             # 500-document list/search timing (SC-002/SC-003)

README.md                                   # + feature docs, sample data, routes, known limitations (EICAR scanner, local storage, dev DB recreate)
.gitignore                                  # + ContosoDashboard/AppData/
```

**Structure Decision**: Keep the single existing web project (`ContosoDashboard/`) and its `Models/`, `Data/`, `Services/`, `Pages/`, and `Shared/` layers, as Constitution Principle IV requires. New services follow the existing "interface and implementation in one file" convention (for example, `TaskService.cs`). Infrastructure interfaces get their own files because they are swap points. The only optional addition is a sibling test project. It doesn't change the runtime application.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

These entries record new abstractions and patterns beyond Principles II and IV, as Principle V requires. None of them is a constitution violation.

| Addition | Why Needed | Simpler Alternative Rejected Because |
|----------|------------|-------------------------------------|
| `IMalwareScanner` + `SimulatedEicarMalwareScanner` | FR-008 needs a replaceable, offline scan step that fails closed. Principle II requires infrastructure behind interfaces. | Inlining the EICAR check in `DocumentService` would hard-code a training shortcut into business logic, and a real scanner couldn't be swapped in through DI. |
| `DocumentAccessPolicy` (static, EF-translatable predicates) | One definition of "can access" and "can manage" must be applied in SQL to lists, search, the dashboard, the project page, task attachments, and notifications (FR-033, SC-006). | Repeating ad-hoc checks per method (the existing service style) would almost certainly drift across about 15 call sites. Filtering in memory after loading breaks the 2 s targets and paging. |
| `DocumentOperationResult` / `PagedResult<T>` DTOs | Specific user-facing errors (size, type, malware, scan failure, forbidden, cross-project) and paging (500+ documents) are required. | The existing `bool`/`null` returns can't report *why* an upload failed (FR-003, SC-007) or return totals for paging. |
| Development-only schema guard in `Program.cs` | `EnsureCreated()` skips existing databases, so learners with a pre-feature DB would get runtime "Invalid object name 'Documents'" errors. Principle I requires `dotnet run` to work with no manual setup. | Switching to EF migrations contradicts the constitution's `EnsureCreated()` mandate and adds tooling. A manual-only reset step violates the zero-setup rule. |
| Optional `ContosoDashboard.Tests` project (xUnit, `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`; test-time only, offline after restore) | SC-006 requires at least one negative case for every role and operation (about 40 cases). SC-002 and SC-003 require timing with 500 documents. The constitution says automated tests, when added, MUST cover service-layer authorization and the write-order rules. | Manual quickstart alone is too slow and error-prone for that matrix and can't produce 500 documents repeatably. Using EF InMemory or SQLite would change string-matching and constraint semantics compared with LocalDB, so tests use LocalDB. If the team declines this project, quickstart.md still satisfies the constitution's verification gate. |
