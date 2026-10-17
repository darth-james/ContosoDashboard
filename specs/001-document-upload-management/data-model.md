# Data Model: Document Upload and Management

**Feature**: `001-document-upload-management` | **Date**: 2026-10-16 | **Plan**: [plan.md](plan.md) | **Research**: [research.md](research.md)

All new entities live in `ContosoDashboard/Models/` and are configured in `ContosoDashboard/Data/ApplicationDbContext.cs`. `EnsureCreated()` creates the schema (research R-03). Conventions match the existing models:

- `int` keys with `[Key]`
- `[Required]` and `[MaxLength]` data annotations
- `DateTime` in UTC
- `virtual` navigation properties
- Enums stored as `int`, except Category, which is stored as text

## Entity Relationship Overview

```text
User 1───* Document (UploadedByUserId)            [Restrict]
Project 1───* Document (ProjectId, optional)      [Restrict]
Document 1───* DocumentTag                        [Cascade]
Document 1───* DocumentShare                      [Cascade]
User 1───* DocumentShare (SharedByUserId)         [Restrict]
User 1───* DocumentShare (SharedWithUserId, opt.) [Restrict]
Document 1───* TaskDocument                       [Cascade]
TaskItem 1───* TaskDocument                       [Cascade]
User 1───* TaskDocument (AttachedByUserId)        [Restrict]
User 1───* DocumentActivity (UserId)              [Restrict]
DocumentActivity.DocumentId / TaskId              [no FK: audit survives deletion]
Notification.DocumentId                           [no FK: link survives deletion]
```

**Cascade-path check (SQL Server)**: the only cascades are Document→Tag, Document→Share, Document→TaskDocument, and TaskItem→TaskDocument. Every relationship from `Users` and `Projects` is Restrict. The existing `Projects→Tasks` relationship is ClientSetNull (NO ACTION). That rules out "multiple cascade paths" errors.

---

## Document (new)

| Field | Type / Column | Rules |
|-------|---------------|-------|
| `DocumentId` | `int` PK, identity | FR / constitution: int key |
| `Title` | `nvarchar(200)` required | Trimmed, 1–200 characters (FR-004) |
| `Description` | `nvarchar(2000)` null | ≤ 2,000 characters (FR-005) |
| `Category` | `nvarchar(50)` required | One of `DocumentCategories.All`, stored as the readable text |
| `ProjectId` | `int` null, FK → `Projects` | Optional. On upload, the user must be a member or manager, or an Administrator. It can be set later only by a task attachment (FR-030) or a metadata edit by a manager (see validation). |
| `UploadedByUserId` | `int` required, FK → `Users` | The owner. Ownership never transfers. |
| `UploadedDate` | `datetime2` required | Set by the server, UTC |
| `LastModifiedDate` | `datetime2` required | Updated on metadata edit, replace, and project association |
| `OriginalFileName` | `nvarchar(255)` required | Sanitized display and download name only (R-11). Never used for storage paths. |
| `FileSizeBytes` | `bigint` required | 1 to 26,214,400 |
| `FileType` | `nvarchar(255)` required | Canonical MIME derived from the allow-listed extension (R-11) |
| `FilePath` | `nvarchar(500)` required, **unique** | Relative, portable storage key `{uploaderId}/{projectId\|personal}/{guid:N}{ext}` (R-05). Never empty: it's generated before the insert. |

**Navigations**: `UploadedByUser` (User), `Project` (Project?), `Tags` (ICollection<DocumentTag>), `Shares` (ICollection<DocumentShare>), `TaskAttachments` (ICollection<TaskDocument>).

**Indexes**:

- `IX_Documents_UploadedByUserId_UploadedDate` on (`UploadedByUserId`, `UploadedDate` desc): My Documents, Recent Documents widget, dashboard count, Team Documents join
- `IX_Documents_ProjectId`: project lists and the access predicate
- `IX_Documents_Category`: filter
- `IX_Documents_UploadedDate`: date-range filter and sort
- `IX_Documents_FilePath` (unique): integrity

**Validation rules** (enforced in `DocumentService`; UI mirrors them):

- Title required and ≤ 200. Category must be in the fixed list. Description ≤ 2,000. Tags follow the DocumentTag rules.
- `ProjectId` on upload: the requester must be a project member or the project manager, or an Administrator. Otherwise the result is `Forbidden`, and the UI never offers that project.
- `ProjectId` on metadata edit: **not editable** through `UpdateMetadataAsync`. FR-019 limits edits to title, description, category, and tags. The project changes only through the FR-030 task-attachment rule.
- File rules: allow-listed extension, 0 < size ≤ 26,214,400, scan result `Clean`.

**Lifecycle / state transitions**:

```text
(none) --upload: validate → scan → write file → insert--> Active
Active --edit metadata (CanManage)--> Active (LastModifiedDate updated)
Active --replace file (owner only): validate → scan → write new key → update → delete old file--> Active
Active --attach to project task (no project → task's project; owner or CanManage)--> Active (ProjectId set)
Active --delete (CanManage, confirmed): delete row (+tags, shares, attachments) → delete file--> (gone; activity retained)
```

There is no soft-delete state and no version history (out of scope).

---

## DocumentCategories (new static list, not a table)

`Models/DocumentCategories.cs`: `public static readonly IReadOnlyList<string> All = ["Project Documents", "Team Resources", "Personal Files", "Reports", "Presentations", "Other"];`

Category is stored as text in `Document.Category`. Validation compares by ordinal.

---

## DocumentTag (new)

| Field | Type / Column | Rules |
|-------|---------------|-------|
| `DocumentTagId` | `int` PK | |
| `DocumentId` | `int` FK → `Documents` (Cascade) | |
| `Tag` | `nvarchar(50)` required | Trimmed and non-empty. Duplicates on the same document are ignored, compared case-insensitively. |

**Indexes**: unique (`DocumentId`, `Tag`), relying on the case-insensitive default collation as a backstop to the service de-duplication. A separate index on `Tag` supports search.

**Rules**: at most 10 tags per document (FR-005). Updating tags replaces the set: the service removes missing tags and adds new ones.

---

## DocumentShare (new)

| Field | Type / Column | Rules |
|-------|---------------|-------|
| `DocumentShareId` | `int` PK | |
| `DocumentId` | `int` FK → `Documents` (Cascade) | |
| `SharedByUserId` | `int` FK → `Users` (Restrict) | Always the document owner (FR-022) |
| `SharedWithUserId` | `int` null, FK → `Users` (Restrict) | Set for a user share |
| `SharedWithDepartment` | `nvarchar(100)` null | Set for a team share. Matches `User.Department`, which is also 100. |
| `SharedDate` | `datetime2` required | UTC |

**Constraints**:

- Check constraint `CK_DocumentShares_SingleTarget`: `([SharedWithUserId] IS NOT NULL AND [SharedWithDepartment] IS NULL) OR ([SharedWithUserId] IS NULL AND [SharedWithDepartment] IS NOT NULL)`.
- Unique filtered index (`DocumentId`, `SharedWithUserId`) `WHERE [SharedWithUserId] IS NOT NULL`. This prevents duplicate user shares (US5 AS6).
- Unique filtered index (`DocumentId`, `SharedWithDepartment`) `WHERE [SharedWithDepartment] IS NOT NULL`.
- Indexes on `SharedWithUserId` and `SharedWithDepartment` support the access predicate and Shared with Me.

**Semantics**:

- Team membership is evaluated **at access time** (`s.SharedWithDepartment == currentUser.Department`, read live from `Users`). It isn't expanded when the document is shared.
- A user share to the owner, or a team share to a department nobody belongs to, is refused with a validation message. A team share to the owner's own department is allowed, and the owner isn't notified.
- Removing a share deletes the row, so access is revoked immediately (FR-025).

---

## TaskDocument (new: task attachment)

| Field | Type / Column | Rules |
|-------|---------------|-------|
| `TaskDocumentId` | `int` PK | |
| `TaskId` | `int` FK → `Tasks` (Cascade) | |
| `DocumentId` | `int` FK → `Documents` (Cascade) | |
| `AttachedByUserId` | `int` FK → `Users` (Restrict) | Used by the detach rule (FR-030a) |
| `AttachedDate` | `datetime2` required | UTC |

**Indexes**: unique (`TaskId`, `DocumentId`). Attaching the same document twice is a no-op that returns success. There's also an index on `DocumentId`.

**Attach rules** (FR-030, evaluated in `TaskDocumentService` with live data):

1. The requester can view the task (`ITaskService.GetTaskByIdAsync != null`) **and** can access the document (`CanAccess`). Otherwise the result is `NotFound`.
2. Task has a project P:
   - **Membership first**: the requester must be a member or the manager of P, or an Administrator (`CanUploadToProject(P)`). A task viewer who isn't (for example, a non-member assignee or creator) gets `Forbidden`, and nothing changes. The same rule applies to uploading from the task (`UploadDocumentAsync` with `TaskId`).
   - Document's project == P: allowed.
   - Document's project is another project: refused with `CrossProject` ("document belongs to a different project").
   - Document has no project: allowed only if the requester is the owner or `CanManage`. Otherwise `Forbidden`, and nothing changes. When allowed, `Document.ProjectId = P` and `LastModifiedDate` is updated. The new-project-document notification goes to P's members (R-12).
3. Task has no project: allowed for any user who can view the task. The document's project is unchanged.

**Detach rules** (FR-030a): the requester is the `AttachedByUserId` **or** `CanManage(document)`. Only the `TaskDocument` row is deleted, and `Document.ProjectId` is unchanged. A `TaskDetach` activity is recorded.

---

## DocumentActivity (new: audit record)

| Field | Type / Column | Rules |
|-------|---------------|-------|
| `DocumentActivityId` | `int` PK | |
| `DocumentId` | `int` required (**no FK**) | Survives document deletion (FR-036) |
| `DocumentTitle` | `nvarchar(200)` required | Title snapshot at the time of the action |
| `FileType` | `nvarchar(255)` null | MIME snapshot (Upload and Replace), used by the types report |
| `UserId` | `int` FK → `Users` (Restrict) | Acting user |
| `Action` | `int` (`DocumentActivityAction`) | See enum |
| `TaskId` | `int` null (no FK) | Required for TaskAttach and TaskDetach (FR-035) |
| `Details` | `nvarchar(500)` null | For example, "Shared with user Dana Whitfield", "Shared with department Marketing", or "Category: Reports → Other" |
| `OccurredAt` | `datetime2` required | UTC |

**Enum `DocumentActivityAction`**: `Upload = 0, Download = 1, Preview = 2, MetadataEdit = 3, Replace = 4, Delete = 5, Share = 6, ShareRemoved = 7, TaskAttach = 8, TaskDetach = 9`.

**Indexes**: `OccurredAt`; (`Action`, `OccurredAt`) for reports; `DocumentId`; `UserId`.

**Rules**: insert only. The application has no update or delete path. Rows are written in the same `SaveChangesAsync()` as the action, or just before streaming for Download and Preview (R-13). Only Administrators can read them.

---

## Changes to existing entities

### Notification (modified)

- Add `public int? DocumentId { get; set; }`. No FK, no navigation.
- Append to `NotificationType`: `DocumentShared`, `DocumentAdded`. Existing integer values are unchanged.

### DashboardSummary (DTO in `DashboardService.cs`, modified)

- Add `public int TotalDocuments { get; set; }`: `COUNT(*) FROM Documents WHERE UploadedByUserId = @me` (FR-032).

### User, Project, TaskItem

- **No column changes.** Optional inverse navigations (`Project.Documents`, `TaskItem.DocumentAttachments`) may be added for `.Include` convenience. All relationships are configured with the fluent API in `OnModelCreating`.

### Seed data (modified, see research R-14)

| Entity | New rows |
|--------|----------|
| User | 5 Dana Whitfield (Employee, Engineering), 6 Mateo Alvarez (Employee, Marketing), 7 Hana Sato (TeamLead, Marketing), 8 Rafael Costa (ProjectManager, Marketing). All have `InAppNotificationsEnabled = true`. |
| Project | 2 "Marketing Website Refresh" (PM 8, Active) |
| ProjectMember | 3 (Project 2, User 6, "Developer") |
| TaskItem | 4 "Prepare onboarding checklist" (no project, assignee 4, creator 3); 5 "Draft launch copy" (Project 2, assignee 6, creator 8) |

---

## Derived / non-persisted concepts

| Concept | Definition |
|---------|------------|
| Team | Users whose `Department` equals a given non-empty value, evaluated live |
| Team members' uploads (oversight) | Documents whose uploader is currently in the overseer's department **and** currently has `Role == Employee`. Requires the overseer to have `Role ≥ TeamLead` and a non-empty department. |
| `DocumentUserContext` | `{ UserId, Role, Department }`, loaded from `Users` per service call (R-02) |
| `CanAccess`, `CanManage`, `IsOwner` | Predicates in `DocumentAccessPolicy` (R-02); see the [service contracts](contracts/service-interfaces.md) |

## Requirement traceability (data)

| Requirement | Satisfied by |
|-------------|--------------|
| FR-004, FR-005, FR-006 | `Document` fields and `DocumentTag` limits |
| FR-010, FR-039 | `Document.FilePath` relative GUID key, unique |
| FR-011 | Upload, replace, and delete write order (R-04) |
| FR-021 | Cascades to Tag, Share, and TaskDocument. `DocumentActivity` is retained (no FK). |
| FR-022 to FR-025 | `DocumentShare` with live department match and unique filtered indexes |
| FR-030, FR-030a | `TaskDocument` plus the attach and detach rules |
| FR-035 to FR-037 | `DocumentActivity` with snapshots and report indexes |
| FR-028 | `Notification.DocumentId` and the preference filter in `CreateNotificationsAsync` |
| FR-032 | `DashboardSummary.TotalDocuments` |
