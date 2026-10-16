# Contract: Authorization-Checked File Endpoints

**Feature**: `001-document-upload-management` | **Implementation**: Razor Pages (constitution: Razor Pages for HTTP-bound flows) | **Research**: R-06

Blazor Server can't stream files over the SignalR circuit, so download and preview are plain HTTP GET endpoints. They authenticate with the existing auth cookie. They're the **only** way to read stored file content (FR-009, Principle III).

## GET `/documents/{documentId:int}/download`

| Item | Contract |
|------|----------|
| Page | `Pages/DocumentDownload.cshtml` + `DocumentDownloadModel` with `[Authorize]` |
| Auth | Cookie. If unauthenticated, the cookie handler redirects (302) to `/login`. |
| User | `int.Parse(User.FindFirst(ClaimTypes.NameIdentifier))`. If missing or invalid, return 404. |
| Service call | `IDocumentService.OpenDocumentContentAsync(documentId, userId, DocumentContentMode.Download)` |
| 200 OK | `FileStreamResult`, `Content-Type` = stored canonical `FileType`, `Content-Disposition: attachment; filename="…"; filename*=UTF-8''…` (the original sanitized name, FR-017) |
| Extra headers | `Cache-Control: private, no-store`. The global headers (CSP, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`) apply unchanged. |
| 404 Not Found | Document missing **or** not accessible **or** file missing in storage. The body is the same plain text for all three: "Document not found or not available." This doesn't reveal whether the document exists. |
| Side effect | One `DocumentActivity` (Download) per successful response (FR-035) |
| Method | GET only. No state change beyond the audit record. |

## GET `/documents/{documentId:int}/preview`

| Item | Contract |
|------|----------|
| Page | `Pages/DocumentPreview.cshtml` + `DocumentPreviewModel` with `[Authorize]` |
| Service call | `IDocumentService.OpenDocumentContentAsync(documentId, userId, DocumentContentMode.Preview)` |
| 200 OK | Only for `application/pdf`, `image/jpeg`, and `image/png`. `Content-Type` is the server-derived MIME (never the client-reported one). `Content-Disposition: inline; filename="…"`. `Cache-Control: private, no-store`. |
| 302 Found | Accessible but not previewable (Office or text): redirect to `/documents/{documentId}`, where Download is offered (FR-018, US3 AS6). No activity is recorded. |
| 404 Not Found | Same as download |
| Side effect | One `DocumentActivity` (Preview) per successful response |
| UI usage | Opened in a **new tab** (`target="_blank" rel="noopener"`). It's never framed, so `X-Frame-Options: DENY` stays. |

## Blazor link rules

| Link | Markup | Why |
|------|--------|-----|
| Download | `<a href="/documents/{id}/download" download>` or `NavigationManager.NavigateTo(url, forceLoad: true)` | Stops the Blazor router from intercepting the click and showing "Page not found" |
| Preview | `<a href="/documents/{id}/preview" target="_blank" rel="noopener">` | Opens a new tab. The router doesn't intercept links with `target="_blank"`. |

## Routing notes

- The Razor Page routes are more specific than `MapFallbackToPage("/_Host")`, so they win. The existing `/login` and `/logout` pages work the same way. If the implementation finds these pages aren't routed, add `app.MapRazorPages();` before `MapBlazorHub()`. That doesn't change the security headers.
- There are no upload HTTP endpoints. Uploads go through the Blazor component and `IDocumentService` over the authenticated circuit (research R-07).

## Negative test matrix (minimum)

| Request | As | Expected |
|---------|----|----------|
| `/documents/{personalDocOfNiKang}/download` | Dana Whitfield (Employee, same dept, not shared) | 404 |
| `/documents/{personalDocOfNiKang}/download` | not signed in | 302 → `/login` |
| `/documents/{project2Doc}/preview` | Ni Kang (not on Project 2) | 404 |
| `/documents/999999/download` | any user | 404 (identical body) |
| `/documents/{docxId}/preview` | owner | 302 → `/documents/{docxId}` |
