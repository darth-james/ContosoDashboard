# Feature Specification: Document Upload and Management

**Feature Branch**: `001-document-upload-management`

**Created**: 2026-10-16

**Status**: Draft

**Input**: User description: Stakeholder requirements in `StakeholderDocs/document-upload-and-management-feature.md` ("Document Upload and Management Feature - Requirements"). Contoso employees need to upload work-related documents to ContosoDashboard, organize them by category and project, find them quickly, and share them securely with colleagues. Today documents are scattered across local drives, email attachments, and shared drives. That makes them hard to find, creates security risks, and hides which documents belong to which projects or tasks.

## Clarifications

### Session 2026-10-16

- Q: How should uploaded files be scanned for malware on an offline training workstation? → A: Use a simulated scanner that flags the standard EICAR test file. It is replaceable and documented as a known training limitation. Uploads are still rejected (fail closed) if the scan step fails.
- Q: Who may attach a document that has no project to a project task (which adds it to that project and exposes it to all project members)? → A: Only the document's owner, or someone allowed to edit the document's details. Everyone else can attach only documents that are already in that task's project.
- Q: What does "team" mean, and whose documents does a Team Lead oversee? → A: A team is the users currently in the same department (live membership). A Team Lead oversees only uploads by department members in a lower role (Employees).
- Q: What may Team Leads and Project Managers do with others' documents they manage? → A: View, download, edit details, and delete. Only the uploader replaces the file or shares it.
- Q: Who can remove a document from a task, and does removal change the document's project? → A: The person who attached it, or anyone allowed to edit the document's details. The document keeps its project, and the removal is recorded in the activity log.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Upload and Access My Own Documents (Priority: P1)

An employee selects one or more work files from their computer and enters a title, category, and optional description, project, and tags. They see upload progress and then a clear success or error message for each file. The uploaded documents appear in their **My Documents** list, where they can download them later.

**Why this priority**: This is the core value of the feature: a single, secure place to keep work documents. Every other story depends on documents existing in the system. On its own, this story is a viable MVP.

**Independent Test**: Sign in as a seeded Employee. Upload a valid PDF with a title and category. Confirm it appears in My Documents with the correct metadata, then download it and confirm the content matches. Try an oversized file, an unsupported file type, and the standard EICAR antivirus test file, and confirm all three are rejected with clear messages and nothing is stored. Sign in as a different Employee and confirm they cannot see or download the first user's personal document.

**Acceptance Scenarios**:

1. **Given** a signed-in user on the documents area, **When** they select a 2 MB PDF, enter the title "Q3 Budget Notes", choose the category "Reports", and submit, **Then** a progress indicator is shown, a success message confirms the upload, and the document appears in My Documents with its title, category, upload date and time, file size, and associated project (or none).
2. **Given** a signed-in user, **When** they select a file larger than 25 MB, **Then** the upload is rejected before storage and an error message says the file exceeds the 25 MB limit.
3. **Given** a signed-in user, **When** they select a file whose type is not supported (for example, `.exe` or `.zip`), **Then** the upload is rejected and an error message lists the supported file types.
4. **Given** a signed-in user filling in upload details, **When** they leave the title empty or do not choose a category, **Then** submission is blocked and the missing required fields are clearly indicated.
5. **Given** a signed-in user, **When** they upload a text file (`.txt`) that contains the standard EICAR antivirus test string, which the malware scan flags, **Then** the file is not stored, no document record is created, and the user sees a message that the file was rejected for security reasons.
6. **Given** a user who uploaded a document, **When** they choose to download it from My Documents, **Then** they receive the original file with its original file name.
7. **Given** a user who did not upload a personal document and has no other access path to it (see the permissions table), **When** they try to open or download it by any means, including a direct link, **Then** access is denied and no document details are revealed.
8. **Given** a signed-in user who selects three files at once, two valid and one over 25 MB, **When** they submit, **Then** the two valid files are uploaded, the oversized file is rejected with its own error message, and the result for each file is shown separately.

---

### User Story 2 - Share Documents Within a Project (Priority: P2)

A project member (an Employee assigned to the project, or the project's Project Manager) uploads a document and associates it with that project. Everyone on the project team can see the document on the project's page and download it. Other project members are notified that a new document was added. The Project Manager can view, download, edit the details of, and delete every document in their project. Only a document's uploader can replace its file or share it.

**Why this priority**: Project collaboration is the main business reason for the feature: making it clear which documents belong to which projects. It builds directly on Story 1.

**Independent Test**: Sign in as a member of a seeded project and upload a document associated with that project. Sign in as another member of the same project. Confirm they see the document in the project's document list, can download it, and received an in-app notification. Sign in as a user who is not a member of the project and confirm the document is not visible and cannot be downloaded.

**Acceptance Scenarios**:

1. **Given** a user who is a member of Project A, **When** they upload a document and associate it with Project A, **Then** the document appears in Project A's document list for every Project A member and for Project A's Project Manager.
2. **Given** a document has just been added to Project A, **When** the upload completes, **Then** every other Project A member, including the Project Manager, receives an in-app notification that names the document and the project. The uploader does not receive one.
3. **Given** a non-Administrator user who is neither a member nor the Project Manager of Project B, **When** they try to upload a document associated with Project B, **Then** the system does not offer Project B as a choice, and any attempt to bypass this is denied.
4. **Given** a user who is not a member of Project A and has no other access path, **When** they try to view or download a Project A document, **Then** access is denied.
5. **Given** the Project Manager of Project A, **When** they view Project A's documents, **Then** they can view, download, edit the metadata of, and delete any document associated with Project A, including documents uploaded by others.
6. **Given** the Project Manager of Project A and a Project A document uploaded by someone else, **When** they try to replace its file or share it, **Then** the actions are unavailable in the interface and denied if attempted by other means.

---

### User Story 3 - Find and Preview Documents (Priority: P2)

A user finds documents by sorting and filtering their lists and by searching all documents they are allowed to see. They search by title, description, tag, uploader name, or project. They preview PDFs and images in the browser without downloading them.

**Why this priority**: Faster retrieval is a headline success metric: locating a document in under 30 seconds. Once users have more than a handful of documents, this story makes the feature useful.

**Independent Test**: Seed or upload documents with a variety of categories, projects, dates, and tags. Confirm that each sort order and filter returns the expected documents. Confirm that search finds documents by each searchable attribute and never returns documents the user cannot access. Open a PDF and a PNG in preview.

**Acceptance Scenarios**:

1. **Given** a user with documents in My Documents, **When** they sort by title, upload date, category, or file size, **Then** the list is reordered by that attribute in ascending or descending order, as chosen.
2. **Given** a user with documents in My Documents, **When** they filter by category, associated project, or upload date range, alone or combined, **Then** only documents that match all the active filters are shown.
3. **Given** a user who can access a document tagged "onboarding" and cannot access another document with the same tag, **When** they search for "onboarding", **Then** only the accessible document is returned.
4. **Given** a user, **When** they search using text that matches a document's title, description, tag, uploader name, or associated project name, **Then** the matching accessible documents are returned within 2 seconds.
5. **Given** a user with access to a PDF or a JPEG/PNG image, **When** they choose Preview, **Then** the document is shown in the browser without being downloaded, within 3 seconds.
6. **Given** a user with access to a Word, Excel, PowerPoint, or text document, **When** they view it, **Then** a download option is offered instead of a preview.
7. **Given** a search or filter that matches nothing, **When** results are shown, **Then** a clear "no documents found" message appears.

---

### User Story 4 - Edit, Replace, and Delete Documents (Priority: P2)

A document's uploader keeps it accurate by editing its title, description, category, and tags, and by replacing the file with an updated version. They delete documents they no longer need. Project Managers, Team Leads, and Administrators can view, download, edit the metadata of, and delete documents uploaded by others, within the scope described in the permissions table. Only the uploader can replace a document's file or share it.

**Why this priority**: Keeping documents accurate and removing obsolete ones is necessary for a trustworthy repository and for the target of 90% properly categorized documents.

**Independent Test**: As the uploader, edit a document's metadata and confirm the changes appear everywhere the document is listed. Replace the file and confirm that downloading returns the new content. Delete the document after confirming the prompt, and confirm it no longer appears anywhere and can no longer be downloaded. As a non-owner Employee, confirm that edit, replace, and delete are unavailable and are denied if attempted.

**Acceptance Scenarios**:

1. **Given** the uploader of a document, **When** they change its title, description, category, or tags and save, **Then** the updated metadata appears in all lists and search results that include the document.
2. **Given** the uploader of a document, **When** they replace the file with a new valid file, **Then** later downloads and previews return the new file, the file size and type are updated, and the previous file is permanently discarded (no version history is kept).
3. **Given** the uploader of a document, **When** they choose Delete, **Then** they are asked to confirm. If they confirm, the document and its file are permanently removed. If they cancel, nothing changes.
4. **Given** an Employee who did not upload a document, **When** they try to edit, replace, or delete it, **Then** the action is unavailable in the interface and denied if attempted by other means.
5. **Given** a deleted document that had been shared or attached to tasks, **When** recipients or task viewers look for it, **Then** it no longer appears in Shared with Me, task attachments, project lists, search results, or the dashboard.
6. **Given** a replacement file that fails validation (too large, unsupported type, or flagged by the malware scan), **When** the user submits it, **Then** the replacement is rejected and the existing file is kept unchanged.
7. **Given** a Team Lead, Project Manager, or Administrator who did not upload a document but may manage it (see the permissions table), **When** they open the document, **Then** edit metadata and delete are available, but replace file and share are unavailable in the interface and denied if attempted by other means.

---

### User Story 5 - Share Documents with People and Teams (Priority: P3)

A document owner shares a document with specific colleagues or with a whole team. Recipients receive an in-app notification and find the document in their **Shared with Me** section. The owner can see who a document is shared with and can remove a share.

**Why this priority**: Controlled sharing replaces risky ad-hoc sharing by email and shared drives. It depends on Stories 1 and 3 and adds value beyond the project-based access in Story 2.

**Independent Test**: As the owner, share a personal document with one named user and with another department's team. Confirm each recipient receives a notification and sees the document under Shared with Me with view and download access only. Remove the share and confirm access is revoked. Confirm that a user who is neither a recipient nor otherwise authorized still cannot access the document.

**Acceptance Scenarios**:

1. **Given** the owner of a document, **When** they share it with a specific user, **Then** that user receives an in-app notification naming the document and the person who shared it, and the document appears in their Shared with Me section.
2. **Given** the owner of a document, **When** they share it with a team, **Then** every current member of that team, except the owner, is notified and can see the document in Shared with Me.
3. **Given** a recipient of a shared document, **When** they open Shared with Me, **Then** they can view, preview (where supported), and download the document, but cannot edit, replace, delete, or re-share it.
4. **Given** the owner of a shared document, **When** they remove a share, **Then** the affected recipients immediately lose access gained through that share, and the document disappears from their Shared with Me section.
5. **Given** a user who is not the document's owner, **When** they try to share the document, **Then** the action is unavailable and denied if attempted.
6. **Given** the owner, **When** they share a document with a user who already has a share for it, **Then** no duplicate share or duplicate notification is created.

---

### User Story 6 - Attach Documents to Tasks (Priority: P3)

While viewing a task, a user sees the documents attached to it, attaches an existing document, uploads a new document directly from the task, or removes an attachment. Documents attached to a project task are automatically associated with that task's project. Because this exposes a document to every project member, only the document's owner, or someone allowed to edit the document's details, can attach a document that has no project to a project task. Removing a document from a task does not change the document's project.

**Why this priority**: This connects documents to day-to-day work. It is valuable but not essential for the initial document repository.

**Independent Test**: Open a seeded task that belongs to a project. Upload a document from the task and confirm it is listed on the task and associated with the task's project. Attach an existing personal document you own and confirm it now appears on the task and in the project's document list. As a user who received a no-project document through a share, confirm that attaching it to the project task is refused. Remove an attachment and confirm the document is no longer on the task, is still in the project's document list, and that the attach and removal both appear in the activity records. As a user without access to the task, confirm its attachments are not visible.

**Acceptance Scenarios**:

1. **Given** a user who can view a task, **When** they open it, **Then** they see the list of documents attached to the task that they are permitted to access.
2. **Given** a user who can view a task that belongs to Project A, **When** they upload a document from the task, **Then** the document is attached to the task and automatically associated with Project A.
3. **Given** a user who owns a document that has no project association, or who is allowed to edit that document's details, **When** they attach it to a task that belongs to Project A, **Then** the document is attached and becomes associated with Project A.
4. **Given** a document associated with Project B, **When** a user tries to attach it to a task in Project A, **Then** the attachment is refused with a message that the document belongs to a different project.
5. **Given** a task that does not belong to any project, **When** a user uploads or attaches a document they can access to it, **Then** the document is attached and keeps its existing project association (or none).
6. **Given** a user who can access a document that has no project association (for example, through a share) but neither owns it nor is allowed to edit its details, **When** they try to attach it to a task in Project A, **Then** the attachment is refused, the document stays unassociated with any project, and Project A members gain no access to it.
7. **Given** a user who can view a task in Project A and can access a document already associated with Project A, **When** they attach it to the task, **Then** the document is attached.
8. **Given** a document attached to a task in Project A, **When** the person who attached it, or a user allowed to edit the document's details, removes it from the task, **Then** it no longer appears on the task, it stays associated with Project A (and in Project A's document list), and the removal is recorded in the activity records.
9. **Given** a user who neither attached a document to a task nor is allowed to edit the document's details, **When** they try to remove it from the task, **Then** the action is unavailable in the interface and denied if attempted by other means.

---

### User Story 7 - Documents on the Dashboard (Priority: P3)

On the dashboard home page, a user sees a **Recent Documents** widget that lists the last 5 documents they uploaded, and a summary card that shows their document count.

**Why this priority**: This makes the feature visible where employees already work every day and supports the adoption target. It is useful but not required for document management itself.

**Independent Test**: As a user who has uploaded 6 documents, open the dashboard. Confirm the widget lists the 5 most recent uploads, newest first, and that the summary card shows 6. As a user with no documents, confirm the widget shows an empty state with a link to upload.

**Acceptance Scenarios**:

1. **Given** a user who has uploaded 6 or more documents, **When** they open the dashboard, **Then** the Recent Documents widget shows their 5 most recently uploaded documents, newest first, each linking to the document.
2. **Given** a user who has uploaded documents, **When** they open the dashboard, **Then** a summary card shows the total number of documents they have uploaded.
3. **Given** a user who has not uploaded any documents, **When** they open the dashboard, **Then** the widget shows an empty state with a link to upload a document, and the summary card shows 0.

---

### User Story 8 - Team Lead Oversight of Team Documents (Priority: P3)

A Team Lead views and manages documents uploaded by the Employees on their team (the users currently in the Team Lead's department who hold the Employee role), so they can keep the team's documents organized. Managing means viewing, downloading, editing details, and deleting. Only the uploader can replace the file or share it.

**Why this priority**: The stakeholder document requires this role capability. It serves a smaller audience than the earlier stories.

**Independent Test**: Sign in as a seeded Team Lead. Confirm that a **Team Documents** view lists documents uploaded by Employees currently in the Team Lead's department, and that the Team Lead can view, download, edit the metadata of, and delete them, but cannot replace or share them. Confirm that documents uploaded by users outside the department, and by other Team Leads, Project Managers, or Administrators in the same department, do not appear (unless accessible by another rule). Confirm that an Employee cannot open the Team Documents view.

**Acceptance Scenarios**:

1. **Given** a Team Lead, **When** they open Team Documents, **Then** they see documents uploaded by Employees currently in their department, with the same sort and filter options as My Documents.
2. **Given** a Team Lead, **When** they edit the metadata of, or delete, a document uploaded by an Employee in their department, **Then** the change is applied (delete requires confirmation).
3. **Given** a Team Lead, **When** they try to access a personal document uploaded by someone outside their department that was not shared with them, **Then** access is denied.
4. **Given** an Employee, **When** they try to open Team Documents, **Then** access is denied.
5. **Given** a Team Lead and a personal document uploaded by another Team Lead or a Project Manager in the same department that was not shared with them, **When** the Team Lead opens Team Documents or tries to access, edit, or delete that document, **Then** it is not listed and the actions are denied.
6. **Given** a Team Lead and a document uploaded by an Employee in their department, **When** they try to replace its file or share it, **Then** the actions are unavailable in the interface and denied if attempted by other means.
7. **Given** an Employee who moves from the Team Lead's department to another department, **When** the Team Lead next opens Team Documents or tries to access that Employee's personal documents, **Then** those documents are no longer listed or accessible through team oversight. The new department's Team Lead gains oversight of all of that Employee's uploads, including earlier ones.

---

### User Story 9 - Document Activity Audit and Reports (Priority: P3)

Document activity (uploads, downloads, previews, metadata edits, file replacements, deletions, share changes, and task attachments and removals) is recorded. An Administrator generates reports on the most uploaded document types, the most active uploaders, and document access patterns for audit and compliance.

**Why this priority**: This supports the audit and compliance goals and the "zero security incidents" metric. It is used by a small group of Administrators and does not affect day-to-day document use.

**Independent Test**: Perform an upload, a download, a share, and a delete as different users. Sign in as an Administrator and confirm each action appears in the activity records with who did it, what they did, which document, and when. Run each report for a date range and confirm the totals match the actions performed. Confirm that a non-Administrator cannot open the reports.

**Acceptance Scenarios**:

1. **Given** any user performs an upload, download, preview, metadata edit, file replacement, deletion, share, share removal, task attachment, or task attachment removal, **When** the action completes, **Then** an activity record is stored with the acting user, the action, the document (title and identifier), and the date and time.
2. **Given** an Administrator, **When** they request the "most uploaded document types" report for a date range, **Then** they see document types ranked by number of uploads in that range.
3. **Given** an Administrator, **When** they request the "most active uploaders" report for a date range, **Then** they see users ranked by number of uploads in that range.
4. **Given** an Administrator, **When** they request the "document access patterns" report for a date range, **Then** they see the most downloaded and previewed documents and access counts per day in that range.
5. **Given** a document that has been deleted, **When** an Administrator reviews activity, **Then** the activity records for that document are still available.
6. **Given** a user who is not an Administrator, **When** they try to open the reports, **Then** access is denied.

---

### Edge Cases

- **Size boundary**: A file of exactly 25 MB is accepted. A file even 1 byte larger is rejected. Empty (0-byte) files are rejected with a clear message.
- **Supported-type boundary**: File type is decided by the file extension against the allow-list, case-insensitively (for example, `.PDF` is accepted). Files with no extension or a double extension that ends in an unsupported type (for example, `report.pdf.exe`) are rejected.
- **Unsafe or unusual file names**: File names that contain path segments, reserved characters, or very long names never affect where or how the file is stored. They are kept only as a display and download name, made safe for display.
- **Duplicate uploads**: Uploading the same file or title twice creates two separate documents. Titles do not need to be unique.
- **Failure during upload**: If the file cannot be fully stored (for example, an interrupted connection or a storage error), no document record is created and the user sees an error. If the record cannot be saved after the file was stored, the stored file is removed, so no orphaned files or records remain.
- **Malware scan step fails**: If the scan step fails or cannot complete (for example, the scanner reports an error), the upload or replacement is rejected (fail closed), no file or document record is kept (for a replacement, the existing file is kept), and the user is told to try again later.
- **Simulated scanner coverage**: The training build's simulated scanner flags only the standard EICAR antivirus test file, including when it is saved with a supported extension such as `.txt`. Other files are treated as clean. This is a documented training limitation, not a production control.
- **Removed project member**: A user removed from a project loses access to that project's documents, except documents they uploaded themselves (still accessible as the owner) and documents shared with them directly.
- **User without a team**: A user with no department assigned cannot be the target of a team share. A Team Lead with no department assigned sees an empty Team Documents view.
- **Department change**: Team membership is checked when a document is accessed. A user who leaves a department loses access gained through that department's team shares, and their uploads leave that department's Team Lead's oversight. A user who joins a department gains access through its team shares, and, if they are an Employee, their uploads (including earlier ones) come under that department's Team Lead's oversight.
- **Role change**: Team Lead oversight covers only uploads by users who currently hold the Employee role. If an Employee is promoted, their uploads leave Team Lead oversight.
- **Removing a task attachment**: Removing a document from a task removes only the link between them. The document keeps its project association and stays in the project's document list. Removing a document from a task is different from deleting the document.
- **Direct links to inaccessible documents**: Requests for documents that do not exist and requests for documents the user may not access get the same "not found or not available" response, so the existence of documents is not revealed.
- **Concurrent edits**: If two authorized users edit the same document's metadata at the same time, the last saved change wins, and both users see the saved result after refreshing.
- **Deletion of shared or attached documents**: Deleting a document also removes its shares and task attachments. Existing notifications that refer to it lead to a "document no longer available" message.
- **Tags**: Tags are trimmed and compared case-insensitively, and duplicates on the same document are ignored.
- **Large lists**: Lists of up to 500 documents load within the performance target. Longer lists are split into pages.
- **Unsupported preview**: Choosing Preview for a type that cannot be previewed (Office or text files) offers a download instead.
- **Self-share**: Sharing a document with yourself, or with a team you belong to, does not notify you.

## Requirements *(mandatory)*

### Functional Requirements

#### Upload and Validation

- **FR-001**: Users MUST be able to select one or more files from their computer and upload them in a single operation. Each file becomes a separate document, and each file's result (success or a specific error) is reported separately.
- **FR-002**: The system MUST accept only these file types: PDF (`.pdf`); Microsoft Word (`.doc`, `.docx`); Microsoft Excel (`.xls`, `.xlsx`); Microsoft PowerPoint (`.ppt`, `.pptx`); plain text (`.txt`); and images (`.jpg`, `.jpeg`, `.png`). It MUST reject all other types with a message that lists the supported types.
- **FR-003**: The system MUST reject any file larger than 25 MB, and any empty file, with a clear message that states the reason and the limit.
- **FR-004**: Users MUST provide a title (required, up to 200 characters) and choose one category (required) from this fixed list: Project Documents, Team Resources, Personal Files, Reports, Presentations, Other. When several files are uploaded together, each file's title MUST default to its file name without the extension and MUST be editable before submission.
- **FR-005**: Users MAY provide a description (up to 2,000 characters), one associated project chosen from projects they are a member of or manage, and up to 10 free-text tags (each up to 50 characters).
- **FR-006**: The system MUST automatically record, for each document: the upload date and time, the uploading user, the file size, the file type (full media-type value, which can be up to 255 characters for Office documents), and the original file name.
- **FR-007**: The system MUST show a progress indicator while files upload, and a success or error message for each file when the upload finishes.
- **FR-008**: The system MUST pass every uploaded or replacement file through a malware scan step before the file becomes available. The scan step MUST work without an internet connection, and MUST be replaceable with a real scanner without changing other behavior. In the training build, the scan step is a simulated scanner that flags the standard EICAR antivirus test file. Files that are flagged, or for which the scan step fails or cannot complete, MUST be rejected and not kept (fail closed).
- **FR-009**: Stored files MUST NOT be directly reachable by a public address. Every download and preview MUST go through an access check against the requesting user's permissions.
- **FR-010**: The system MUST store each file under a unique, system-generated name. User-supplied file names MUST NOT be used to decide where a file is stored.
- **FR-011**: The system MUST complete file storage before creating the document record, and MUST leave no orphaned file and no orphaned record when either step fails.

#### Browsing, Search, and Preview

- **FR-012**: Users MUST have a **My Documents** view that lists every document they uploaded. It MUST show the title, category, upload date, file size, and associated project.
- **FR-013**: Users MUST be able to sort document lists by title, upload date, category, and file size, in ascending and descending order.
- **FR-014**: Users MUST be able to filter document lists by category, associated project, and upload date range, alone or in combination.
- **FR-015**: Each project's page MUST show all documents associated with that project to the project's members and its Project Manager.
- **FR-016**: Users MUST be able to search by title, description, tags, uploader name, and associated project name. Results MUST include only documents the user is permitted to access.
- **FR-017**: Users MUST be able to download any document they are permitted to access. The download MUST use the document's original file name.
- **FR-018**: Users MUST be able to preview PDF, JPEG, and PNG documents in the browser without downloading them. Other supported types MUST offer download only.

#### Managing Documents

- **FR-019**: These users MUST be able to edit a document's title, description, category, and tags: its uploader; a Team Lead (and, by role inheritance, a Project Manager), for documents uploaded by Employees currently in their department; a Project Manager, for documents in projects they manage; and an Administrator, for all documents (see the Permissions table).
- **FR-020**: Only a document's uploader MUST be able to replace its file with a new file. No other user, including Team Leads, Project Managers, and Administrators, may replace it. The new file goes through the same validation and scan as an upload. The previous file MUST be permanently discarded, with no version history. If the replacement fails, the existing file MUST be kept.
- **FR-021**: The users who may edit a document's details under FR-019 MUST be able to delete it. Deletion MUST require explicit confirmation and MUST permanently remove the file, the document's metadata, its shares, and its task attachments. Activity records are kept.

#### Sharing and Notifications

- **FR-022**: Only a document's owner (its uploader) MUST be able to share it, with one or more specific users and with one or more teams. No other user, including Team Leads, Project Managers, and Administrators, may share it. A team is the set of users currently in the same department. Team membership MUST be checked each time a document is accessed, not only when it is shared.
- **FR-023**: Share recipients MUST get view, preview, and download access only. They MUST NOT be able to edit, replace, delete, or re-share the document.
- **FR-024**: Users MUST have a **Shared with Me** view that lists the documents shared with them directly or through their team. It MUST show the person who shared each document and the date it was shared.
- **FR-025**: A document's owner MUST be able to see the current shares of the document and remove any of them. Removing a share MUST revoke the access it granted immediately.
- **FR-026**: The system MUST send an in-app notification to each recipient when a document is shared with them (directly or through their team), excluding the person who shared it.
- **FR-027**: The system MUST send an in-app notification to every member of a project, including its Project Manager but not the uploader, when a new document is associated with that project.
- **FR-028**: Notifications MUST follow each user's existing in-app notification preference. They MUST NOT reveal document details to users who cannot access the document.

#### Task and Dashboard Integration

- **FR-029**: A task's detail view MUST show the documents attached to the task that the viewing user is permitted to access.
- **FR-030**: Users who can view a task MUST be able to upload a new document from the task and attach an existing document they can access, subject to these rules:
  - A document attached to a task that belongs to a project MUST be associated with that project.
  - A document already associated with a different project MUST NOT be attached.
  - A document with no project association MAY be attached to a project task only by its owner or by a user allowed to edit its details (FR-019). Other users MUST be refused, and the document's project association MUST remain unchanged.
  - Any user who can view a project task MAY attach a document they can access that is already associated with that task's project.
  - For a task that does not belong to a project, any document the user can access MAY be attached, and its project association does not change.
- **FR-030a**: The user who attached a document to a task, or any user allowed to edit the document's details (FR-019), MUST be able to remove the document from the task. Other users MUST be refused. Removal MUST remove only the task attachment. The document's project association MUST NOT change.
- **FR-031**: The dashboard home page MUST show a **Recent Documents** widget with the user's 5 most recently uploaded documents, newest first.
- **FR-032**: The dashboard summary cards MUST include the total number of documents the user has uploaded.

#### Roles and Access Control

- **FR-033**: The system MUST enforce the permissions in the Permissions table for every operation, in every place a document can appear (lists, project pages, task pages, search, dashboard, notifications, downloads, and previews). It MUST also enforce them when an operation is requested directly, not only by hiding options in the interface.
- **FR-034**: Team Leads MUST have a **Team Documents** view that lists documents uploaded by users who are currently in the Team Lead's department and hold the Employee role. Documents uploaded by other Team Leads, Project Managers, or Administrators in the department MUST NOT appear in the view or come under Team Lead oversight. Membership and role MUST be checked each time the view or a document is accessed.

#### Audit and Reporting

- **FR-035**: The system MUST record an activity entry for every upload, download, preview, metadata edit, file replacement, deletion, share, share removal, task attachment, and task attachment removal. Each entry MUST capture the acting user, the action, the document, and the date and time. Task attachment and removal entries MUST also identify the task.
- **FR-036**: Activity records MUST be kept after the document they refer to is deleted. Only Administrators can view them.
- **FR-037**: Administrators MUST be able to generate, for a chosen date range, reports of: the most uploaded document types, the most active uploaders, and document access patterns (the most downloaded or previewed documents and access counts per day).

#### Operation and Portability

- **FR-038**: All document capabilities, including storage and malware scanning, MUST work fully offline on a single training workstation, with no cloud service or internet connection. The README MUST list the simulated malware scanner as a known training limitation: it detects only the EICAR test file and is not a production-ready malware control.
- **FR-039**: Stored references to files MUST be portable, so the storage location can later move to a cloud storage service without changing document records, user-facing behavior, or business rules.

### Permissions

"Can access" means the user can view, preview, and download a document. All other operations require the permission shown in the table. Higher roles also have the permissions of lower roles. Project-scoped permissions apply only to projects where the user is a member or the Project Manager. "Team members' uploads" means documents uploaded by users who are currently in the same department and hold the Employee role. Department membership and role are checked each time a document is accessed.

| Operation | Employee | Team Lead | Project Manager | Administrator |
|-----------|----------|-----------|-----------------|---------------|
| Upload a personal document (no project) | Yes | Yes | Yes | Yes |
| Upload a document to a project | Projects they are a member of | Projects they are a member of | Projects they are a member of or manage | Any project |
| Access a document | Own uploads; documents of their projects; documents shared with them or their team | Same as Employee, plus team members' uploads (Employees currently in their department) | Same as Team Lead, plus all documents in projects they manage | All documents |
| Edit metadata | Own uploads | Own uploads and team members' uploads (Employees currently in their department) | Same as Team Lead, plus all documents in projects they manage | All documents |
| Replace file | Own uploads | Own uploads | Own uploads | Own uploads |
| Delete | Own uploads | Own uploads and team members' uploads (Employees currently in their department) | Same as Team Lead, plus all documents in projects they manage | All documents |
| Share or remove shares | Own uploads | Own uploads | Own uploads | Own uploads |
| Attach a document to a task | Tasks they can view, using documents they can access. For a project task: documents already in that project, or their own documents with no project | Same as Employee, plus no-project documents whose metadata they can edit | Same as Team Lead | Same as Team Lead |
| Remove a document from a task | Attachments they made, and attachments of documents whose metadata they can edit | Same as Employee | Same as Employee | Same as Employee |
| Team Documents view | No | Yes (uploads by Employees currently in their department) | Yes (uploads by Employees currently in their department) | Yes (all departments) |
| Activity records and reports | No | No | No | Yes |

### Key Entities *(include if feature involves data)*

- **Document**: A work file uploaded by a user, together with its metadata: title, optional description, category (stored as its readable name), optional associated project, tags, uploader, upload date and time, last-modified date and time, original file name, file size, file type (media type, up to 255 characters), and a portable reference to the stored file. Its identifier follows the same style as the existing user and project identifiers.
- **Document Category**: One value from a fixed list: Project Documents, Team Resources, Personal Files, Reports, Presentations, Other.
- **Document Tag**: A short free-text label attached to a document to make it easier to find. A document can have up to 10 tags.
- **Document Share**: Gives a document's recipient access to it. It records the document, the user who shared it, the recipient (either a specific user or a team, meaning a department), and the date it was shared. For a team share, the recipients are the users in the department at the time of each access, not a list saved when the document was shared.
- **Task Document Attachment**: Links a document to a task. It records who attached the document and when. Removing the attachment deletes this link only. The removal is kept in the Document Activity Record.
- **Document Activity Record**: An audit entry for a document action. It records the acting user, the action type, the document identifier and title at the time of the action, and the date and time.
- **Existing entities used**: **User** (role and department, which defines the team); **Project** and **Project Member** (project access and Project Manager); **Task** (attachments and the project it belongs to); **Notification** (in-app notifications for shares and new project documents).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A 25 MB file finishes uploading, including validation and scanning, within 30 seconds on a typical office network or a local training workstation.
- **SC-002**: Document lists (My Documents, Project Documents, Team Documents, Shared with Me) show their contents within 2 seconds for a user with up to 500 documents.
- **SC-003**: Search returns results within 2 seconds when the user can access at least 500 documents.
- **SC-004**: A PDF or image preview of a typical document (under 10 MB) appears within 3 seconds.
- **SC-005**: From the documents area, a user can upload a single document in no more than 3 clicks, not counting typing the metadata.
- **SC-006**: In verification testing, 100% of unauthorized access attempts are denied. This includes at least one negative case per role and per operation in the Permissions table, and covers direct links. No inaccessible document ever appears in a list, search result, dashboard widget, or notification.
- **SC-007**: 100% of rejected uploads (oversized, empty, unsupported type, or flagged by the malware scan) show the user a specific reason and leave no stored file and no document record.
- **SC-008**: 100% of uploads, downloads, previews, metadata edits, replacements, deletions, share changes, and task attachments and removals performed during verification appear in the activity records with the correct user, action, document, and time.
- **SC-009**: Within 3 months of launch, 70% of active dashboard users have uploaded at least one document.
- **SC-010**: Within 3 months of launch, the average time for a user to locate a known document is under 30 seconds, measured by timed task sampling.
- **SC-011**: Within 3 months of launch, 90% of uploaded documents are properly categorized, measured by an Administrator's review of a random sample.
- **SC-012**: Zero security incidents related to document access occur within 3 months of launch.

## Assumptions

- **Teams**: "Team" means the users who are currently in the same department in their user profile. No separate team entity is introduced. Team membership is checked live, when a document is accessed, for both team shares and Team Lead oversight. Users who join the department later gain access, and users who leave lose it. Notifications go to the members at the time of sharing. Team Lead oversight covers only uploads by department members in a lower role (Employees), not uploads by other Team Leads, Project Managers, or Administrators in the department.
- **Ownership**: The uploader is the document's owner. Ownership does not transfer. A user who leaves a project keeps access to the documents they uploaded.
- **"Manage" means**: For Team Leads (uploads by Employees currently in their department) and Project Managers (documents in projects they manage), "manage" means view, download, edit details (metadata), and delete. Only the uploader replaces the file or shares it, so that only the owner changes a document's content and who it is shared with.
- **Administrators** have full access to every document for audit and compliance: viewing, editing metadata, deleting, and reporting. Like other managers, they cannot replace the file of, or share, documents they did not upload.
- **Malware scanning**: The training build uses a simulated, offline scanner that flags the standard EICAR antivirus test file and treats all other files as clean. The scanner is a replaceable step, so a real scanner can be substituted later without changing business rules or user-facing behavior. As the constitution requires, this is a known training limitation and MUST be documented in the README. Uploads and replacements fail closed when the scan step fails or cannot complete.
- **Supported Office formats** include both the legacy and the current Word, Excel, and PowerPoint file formats. "Text files" means `.txt` only.
- **Dashboard document count** counts the documents the user uploaded, matching the Recent Documents widget. It does not count all documents the user can access.
- **Notifications** are in-app only. Email notifications are not part of this release.
- **Field limits** (title 200 characters, description 2,000 characters, 10 tags of 50 characters each) are reasonable defaults, because the stakeholder document does not specify them.
- **Activity records** are kept for the life of the training database. No retention or purge policy is defined for this release.
- **Preview** uses the browser's built-in support for viewing PDFs and images. No conversion of Office documents for preview is provided.
- **Environment**: Local disk storage is available. Most documents are under 10 MB. Users are familiar with basic file management. The feature uses the existing mock sign-in, which provides each user's identifier, name, email, role, and department.
- **Implementation constraints**: The stakeholder document's Technical Constraints, Implementation Approach, Cloud Migration Readiness, UI-framework-specific, and Database Setup sections are implementation guidance. They are carried into `/speckit.plan` and governed by the project constitution, not restated here.
- **Delivery timeline**: The feature should be production-ready within 8 to 10 weeks, per the stakeholder document.

## Out of Scope

- Real-time collaborative editing of documents
- Version history and rollback (replacing a file permanently discards the previous one)
- Advanced document workflows, such as approval processes and document routing
- Integration with external systems, such as SharePoint and OneDrive
- Mobile app support (this release is web only)
- Document templates and document generation
- Storage quotas and quota management
- Soft delete, a trash folder, or recovery of deleted documents
- Cloud storage in the training build (the design only needs to allow a later move)
- Email notifications for document events
- In-browser preview of Office and text documents
