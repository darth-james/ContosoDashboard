# Quickstart: Verifying Document Upload and Management

**Feature**: `001-document-upload-management` | **Plan**: [plan.md](plan.md) | **Contracts**: [contracts/](contracts/) | **Data model**: [data-model.md](data-model.md)

This guide proves the feature works from end to end on an offline training workstation, using the seeded mock users. Each story section includes at least one **negative authorization check**, as the constitution's verification gate requires. Expected messages come from [contracts/ui-routes.md](contracts/ui-routes.md#user-facing-messages-consistent-wording). Endpoint behavior is in [contracts/http-endpoints.md](contracts/http-endpoints.md).

---

## 0. Prerequisites and setup

| Need | Check |
|------|-------|
| .NET 8 SDK | `dotnet --list-sdks` shows `8.0.x` |
| SQL Server LocalDB | `sqllocaldb info` lists `MSSQLLocalDB` |
| Browser | Edge, Chrome, or Firefox with the built-in PDF viewer |
| Network | Not required. Disconnect to prove offline operation (FR-038). |

### 0.1 Clean start (recommended before the first run)

```powershell
cd C:\TrainingProjects\ContosoDashboard\ContosoDashboard
sqllocaldb stop mssqllocaldb
sqllocaldb delete mssqllocaldb
Remove-Item .\AppData\uploads -Recurse -Force -ErrorAction SilentlyContinue
dotnet build        # must succeed with no new errors or nullable warnings
dotnet run --launch-profile https
```

**Expected**: The app starts at `https://localhost:5001` and creates the database with the new tables and seed data. If you skip the reset and your database predates the feature, the console logs a **warning** that the training database is being recreated (Development schema guard, research R-03). The app then starts normally.

### 0.2 Seeded users (research R-14)

| Id | User | Role | Department | Projects |
|----|------|------|------------|----------|
| 1 | System Administrator | Administrator | IT | none |
| 2 | Camille Nicole | ProjectManager | Engineering | Manages **P1** "ContosoDashboard Development" |
| 3 | Floris Kregel | TeamLead | Engineering | Member of P1 |
| 4 | Ni Kang | Employee | Engineering | Member of P1 |
| 5 | Dana Whitfield | Employee | Engineering | none |
| 6 | Mateo Alvarez | Employee | Marketing | Member of **P2** "Marketing Website Refresh" |
| 7 | Hana Sato | TeamLead | Marketing | none |
| 8 | Rafael Costa | ProjectManager | Marketing | Manages P2 |

**Tasks**:

- Tasks 1–3 are in P1, assigned to Ni Kang.
- Task 4 "Prepare onboarding checklist" has **no project** and is assigned to Ni Kang.
- Task 5 "Draft launch copy" is in P2 and assigned to Mateo.

To switch users, use **Logout** and then select the user on `/login`. A second browser profile or a private window also works.

> **About "forced attempt"**: In the steps below, this means bypassing the UI.
>
> - **Pages and file endpoints**: type the URL directly.
> - **Blazor actions** (edit, delete, share, attach, remove), which have no URL: the hidden button is the UI-level check, and the service-level denial is verified by the authorization matrix in §11. If you don't build the test project, the reviewer checks during code review that every such service method validates `requestingUserId` (constitution, Review gate).

### 0.3 Create test files

```powershell
$t = "$env:USERPROFILE\Documents\doc-tests"; New-Item -ItemType Directory -Force $t | Out-Null
# Size boundaries (type is decided by extension; content is zeros)
$f = [IO.File]::Create("$t\exact-25mb.pdf"); $f.SetLength(26214400); $f.Close()
$f = [IO.File]::Create("$t\over-25mb.pdf");  $f.SetLength(26214401); $f.Close()
New-Item "$t\empty.txt" -ItemType File -Force | Out-Null
# Ordinary files
Set-Content "$t\notes.txt" "Team onboarding notes"
Copy-Item "$t\notes.txt" "$t\plan.docx"            # download-only type (content not validated)
Copy-Item "$t\notes.txt" "$t\report.pdf.exe"       # double extension -> unsupported
Copy-Item "$env:WINDIR\System32\notepad.exe" "$t\tool.exe"
Compress-Archive -Path "$t\notes.txt" -DestinationPath "$t\bundle.zip" -Force
Add-Type -AssemblyName System.Drawing
$b = New-Object System.Drawing.Bitmap 400,240; $g = [System.Drawing.Graphics]::FromImage($b)
$g.Clear([System.Drawing.Color]::SteelBlue); $b.Save("$t\diagram.png", [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $b.Dispose()
```

- **Real PDF**: save any real PDF as `$t\budget.pdf` (about 2 MB is ideal), for example by printing a web page with *Microsoft Print to PDF*. The zero-filled boundary files are not viewable PDFs.
- **EICAR test file**: the simulated scanner flags the standard EICAR string anywhere in the file. The string is written here in two halves, so this guide is never flagged.

```powershell
$p1 = 'X5O!P%@AP[4\PZX54(P^)7CC)7}'
$p2 = '$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*'
[IO.File]::WriteAllText("$t\eicar-test.txt", "Training malware test`r`n" + $p1 + $p2)
```

> ⚠️ Microsoft Defender may quarantine `eicar-test.txt` as soon as it's written. If the file disappears, try these in order:
>
> 1. Rely on the automated test in §11, which feeds the bytes from memory.
> 2. Where policy allows, add a temporary Defender exclusion for `$t`, and remove it afterward.
>
> This is expected behavior from a real antivirus product and isn't a defect in the feature.

---

## 1. US1: Upload and access my own documents (P1)

Sign in as **Ni Kang**.

1. Open **Documents** (`/documents`), then select **Upload**, then choose `budget.pdf`. The title defaults to "budget". Change it to **Q3 Budget Notes**, set the category to **Reports**, leave the project empty, and submit.
   - **Expected**: A progress bar appears, followed by a success message.
   - **Expected**: The list shows the title, Reports, the upload date and time, the size, and "—" for the project.
   - Record the document's ID from its details URL as **D1**.
   - SC-005 counts 3 clicks: Upload, choose file, and Submit.
2. Download D1. **Expected**: The browser saves `budget.pdf` (original name) with content identical to the source. Compare with `Get-FileHash`.
3. Upload `exact-25mb.pdf` (title "Boundary", category Other). **Expected**: Accepted (exactly 25 MB). It finishes within 30 s (SC-001).
4. Select `over-25mb.pdf`, `empty.txt`, `tool.exe`, `bundle.zip`, and `report.pdf.exe`. **Expected**: Each is rejected before storage with its specific message (25 MB limit, empty, or supported-types list).
5. Upload `eicar-test.txt` (category Other). **Expected**: "'eicar-test.txt' was rejected for security reasons."
6. Clear the title field and the category on a pending file. **Expected**: Submit is blocked, and the required fields are highlighted.
7. Multi-file: select `notes.txt`, `diagram.png`, and `over-25mb.pdf` together, set categories, and submit. **Expected**: Each file shows its own result: two successes and one rejection. Record `diagram.png` as **D2**.
8. **Storage check**:

   ```powershell
   Get-ChildItem .\AppData\uploads -Recurse -File | Select FullName, Length
   ```

   - **Expected**: Only the accepted files appear, under `4\personal\{guid}.{ext}`. They never use the user's file names, and none of them is under `wwwroot`.
   - **Expected**: No file exists for the rejected uploads (SC-007).

**Negative (authorization)**:

- Sign in as **Dana Whitfield** (same department, not shared). Open `/documents/{D1}`. **Expected**: "Document not found or not available."
- Still as Dana, browse to `https://localhost:5001/documents/{D1}/download` and `/documents/999999/download`. **Expected**: Both return the same 404 text (US1 AS7).
- Sign out and request `/documents/{D1}/download`. **Expected**: Redirect to `/login`.

## 2. US2: Share documents within a project (P2)

1. As **Ni Kang**, upload `notes.txt` with title "P1 Kickoff Notes", category Project Documents, project **P1**. Record it as **D3**.
   - **Expected**: Only P1 appears in the Project list. P2 isn't offered (US2 AS3).
2. Sign in as **Floris Kregel** (P1 member). Open **Notifications**.
   - **Expected**: "New project document" naming D3 and P1, with an *Open document* link.
   - On `/projects/1`, the Project Documents card lists D3, and download works.
3. As **Camille Nicole** (P1 manager): **Expected**: She received a notification, and on D3's details page Edit and Delete are available, but **Replace file** and **Share** are not (US2 AS5, AS6). Ni Kang, the uploader, received no notification.

**Negative (authorization)**:

- Sign in as **Mateo Alvarez** (not on P1). Open `/documents/{D3}` and `/documents/{D3}/download`. **Expected**: Not found and 404 (US2 AS4).
- As **Rafael Costa** (manages P2, not P1). Open `/documents/{D3}`. **Expected**: Not found. He can't manage another project's documents.

## 3. US3: Find and preview documents (P2)

As **Ni Kang**:

1. Upload two or three more documents with different categories, tags (for example, `onboarding`), and dates. Tag D3 with `onboarding` by editing it.
2. On My Documents, sort by Title, Upload date, Category, and Size in both directions. Then filter by Category, Project, and a date range, alone and combined. **Expected**: The order and matches are correct (US3 AS1, AS2).
3. **Search** for `onboarding`, part of a title, a description word, `Ni Kang` (uploader), and `ContosoDashboard` (project name). **Expected**: Matching accessible documents appear within 2 s. A nonsense term shows "No documents found" (AS4, AS7).
4. **Preview** D1 (PDF) and D2 (PNG). **Expected**: Each opens in a new tab and renders within 3 s (SC-004) without a download. Open `plan.docx` after uploading it. **Expected**: Only Download is offered, and requesting `/documents/{docx}/preview` redirects to the details page (AS6).

**Negative (authorization)**:

- Sign in as **Mateo Alvarez**, upload `notes.txt` (personal, tag `onboarding`), then sign in as **Dana Whitfield** and search `onboarding`. **Expected**: Neither Ni Kang's D3 (P1 only) nor Mateo's personal document appears (AS3).

## 4. US4: Edit, replace, and delete (P2)

As **Ni Kang** on D1:

1. Edit the title, description, category, and tags, then save. **Expected**: The changes appear in My Documents and in search.
2. **Replace file** with `diagram.png`.
   - **Expected**: The type and size update, and download and preview return the new content.
   - **Expected**: The old file is gone from `AppData\uploads`, and a new GUID file exists.
3. Replace with `over-25mb.pdf` and then with `eicar-test.txt`. **Expected**: Both are rejected, and the current file is unchanged (AS6).
4. **Delete** D2. Cancel the first time (**Expected**: no change). Confirm the second time.
   - **Expected**: D2 is gone from all lists and from search.
   - **Expected**: Its file is removed from storage, and `/documents/{D2}/download` returns 404.

**Negative (authorization)**:

- As **Dana Whitfield**, an Employee who isn't the owner: she can't even see Ni Kang's personal document, so it isn't found.
- As **Floris Kregel** (P1 member, Team Lead) on Camille's P1 document (upload one as Camille first): Floris can access it, but because Camille isn't an Employee, Edit and Delete are **not** shown. A forced attempt returns "You don't have permission" (AS4).
- As **Camille Nicole** on D3 (uploaded by Ni Kang): Edit and Delete work. Replace and Share are absent (AS7).

## 5. US5: Share with people and teams (P3)

As **Ni Kang**, upload `notes.txt` (personal) and record it as **D4**.

1. Share D4 with **Dana Whitfield**. **Expected**: Dana gets a "Document shared with you" notification, and D4 appears in her **Shared with Me** with "Shared by Ni Kang" and the date. She can preview or download, but Edit, Replace, Delete, and Share are absent (AS1, AS3).
2. Share D4 with Dana again. **Expected**: No duplicate share and no second notification (AS6).
3. Share D4 with team **Marketing**. **Expected**: Mateo, Hana, and Rafael are each notified and see D4 in Shared with Me (AS2).
4. On D4, review the shares and **remove** the Marketing share. **Expected**: Mateo immediately gets 404 on `/documents/{D4}`, and D4 leaves his Shared with Me (AS4).
5. Share with team **Engineering** (Ni Kang's own department). **Expected**: Ni Kang isn't notified, but Floris, Camille, and Dana are.

**Negative (authorization)**:

- As **Dana Whitfield** (a recipient), try to share D4. **Expected**: Share isn't offered and the service refuses it (AS5).
- As **System Administrator**, open D4. **Expected**: It can be viewed, edited, and deleted, but Share and Replace are absent (owner-only).

## 6. US6: Attach documents to tasks (P3)

As **Ni Kang**:

1. Open **My Tasks**, then the eye icon on task 2 ("Implement authentication", P1), which opens `/tasks/2`. Upload `notes.txt` from the task. **Expected**: It's attached, associated with **P1**, and listed on `/projects/1` (AS2).
2. Attach **D4** (personal, owned by Ni Kang) to task 2. **Expected**: It's attached, and D4 now shows project P1 (AS3).
3. Open task 4 (no project) and attach D1. **Expected**: It's attached, and D1's project is unchanged (AS5).
4. As **Rafael Costa**, upload a P2 document (**D5**) and share it with Ni Kang. Then, as Ni Kang, try to attach D5 to task 2 (P1). **Expected**: "This document belongs to a different project…" (AS4).
5. Remove D4 from task 2. **Expected**: It's gone from the task but **still** listed on P1 (AS8).
6. As **System Administrator** on `/admin/document-reports`, look at the activity log. **Expected**: TaskAttach and TaskDetach entries exist with task ID 2.

**Negative (authorization)**:

- **Attach a shared document that has no project (AS6)**:
  1. As **Camille Nicole**, upload a personal document (no project) and record it as **D6**. Share D6 with **Ni Kang**.
  2. As **Ni Kang** (a P1 member who can view task 2 but who neither owns D6 nor can manage it), try to attach D6 to task 2.
  - **Expected**: The attachment is refused (D6 isn't offered in the attach picker, and a forced attempt returns "You don't have permission…").
  - **Expected**: D6 still has no project, and **Floris** (P1 member, not a share recipient) still gets "not found" for `/documents/{D6}`.
- **Task not visible**: As **Mateo Alvarez**, browse to `/tasks/2`. **Expected**: "Task not found or not available", and no attachments are shown.
- **Remove an attachment you didn't make (AS9)**:
  1. As **Camille Nicole**, attach one of her own P1 documents to task 2. Record it as **D7**.
  2. As **Ni Kang** (didn't attach D7, and as an Employee can't manage Camille's uploads), open `/tasks/2`.
  - **Expected**: No Remove action is shown for D7, and a forced attempt is refused.

## 7. US7: Dashboard (P3)

1. As **Ni Kang** after uploading 6 or more documents, open `/`. **Expected**: The Recent Documents widget shows the 5 newest uploads, newest first, each linked. The "My Documents" summary card shows the total (AS1, AS2).
2. As **Hana Sato** (no uploads). **Expected**: An empty state with an upload link, and a count of 0 (AS3).

**Negative**: As Dana, the widget never shows documents shared with her. It shows uploads only.

## 8. US8: Team Lead oversight (P3)

1. As **Dana Whitfield**, upload one personal document first. Then, as **Floris Kregel**, open **Team Documents**. **Expected**: Lists the uploads by Ni Kang and Dana (Engineering Employees). They can be viewed, edited, and deleted, but Replace and Share are absent (AS1, AS2, AS6).
2. **Expected**: Camille's personal uploads (ProjectManager, same department) are **not** listed (AS5).

**Negative (authorization)**:

- As **Ni Kang**, browse to `/documents/team`. **Expected**: Access Denied (AS4).
- As **Floris**, open Mateo's personal document by its ID. **Expected**: Not found (AS3).
- **Department move (AS7)**: as **Dana**, open **Profile** and change her department to **Marketing**, then save.
  - **Expected**: As **Floris**, Dana's documents vanish from Team Documents and return not found.
  - **Expected**: As **Hana Sato**, Dana's earlier uploads now appear.
  - Change Dana back to Engineering afterward.
- **Role scope**: Team Documents covers only users whose current role is Employee. The UI can't change roles, so the automated matrix (§11) verifies this.

## 9. US9: Activity audit and reports (P3)

As **System Administrator**, open **Document Reports** and choose today's UTC date range.

1. **Expected**: The activity log has an entry with the user, action, document title, ID, and time for each action performed above: uploads, downloads, previews, edits, the replace, the delete, shares, the share removal, and the task attach and detach (SC-008).
2. **Most uploaded types**, **most active uploaders**, and **access patterns** (top documents and per-day counts). **Expected**: The totals match what you did.
3. **Expected**: Entries for the deleted D2 are still present (AS5).

**Negative (authorization)**: As **Rafael Costa** (ProjectManager), browse to `/admin/document-reports`. **Expected**: Access Denied. The menu item is also hidden (AS6).

---

## 10. Cross-cutting checks

| Check | How | Expected |
|-------|-----|----------|
| Offline | Disable networking and repeat §1 steps 1–2 | Works. CDN styling may degrade, but the feature still works (Principle I). |
| Headers unchanged | DevTools → Network → any page and `/documents/{id}/preview` | CSP, `X-Frame-Options: DENY`, and `nosniff` are present. The preview also has `Cache-Control: no-store`. |
| Department claim | Add a temporary debug display, or check that the cookie claims include `Department` after login | Present (Principle III) |
| No orphans | After §1–§6, compare the `Documents` row count with the file count in `AppData\uploads` (optional, if sqlcmd is available): `sqlcmd -S "(localdb)\mssqllocaldb" -d ContosoDashboard -Q "SELECT COUNT(*) FROM Documents"` | The counts match |
| Notification preference | As Floris, turn off in-app notifications in Profile. Have Ni Kang upload to P1. | Floris gets no new notification (FR-028) |

## 11. Automated verification (optional test project)

If `ContosoDashboard.Tests` is implemented (plan.md, Complexity Tracking):

```powershell
cd C:\TrainingProjects\ContosoDashboard
dotnet test .\ContosoDashboard.Tests
```

**Expected**: All tests pass. Coverage includes:

- The role × operation authorization matrix (SC-006), with at least one denial per cell of the spec's Permissions table, including role-change scope.
- Write-order rules: no file when the scan is flagged or errors, no record when the storage write fails, and file cleanup when the save fails (SC-007, FR-011).
- A fail-closed scanner fake.
- Lists and search over 500 seeded documents, each completing under 2 s (SC-002, SC-003).

## 12. Documentation checks

- `README.md` keeps the "TRAINING PURPOSES ONLY" disclaimer.
- README "Known Limitations" lists the **simulated EICAR-only malware scanner** (not a production control, FR-038), local filesystem storage, and the Development-only database auto-recreate.
- README updates the Sample Data, Application Pages, and Database Issues sections. Database Issues also tells readers to delete `AppData\uploads` when resetting.
- `.gitignore` excludes `ContosoDashboard/AppData/`.
