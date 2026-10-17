using System.Security.Claims;
using ContosoDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ContosoDashboard.Pages
{
    /// <summary>
    /// GET /documents/{documentId}/download (contracts\http-endpoints.md). The only way to read stored file content:
    /// the service re-checks access for the signed-in user. A missing document, a document the user can't access, and a
    /// missing file in storage all return the same 404 with the same body.
    /// </summary>
    [Authorize]
    public class DocumentDownloadModel : PageModel
    {
        private readonly IDocumentService _documentService;

        public DocumentDownloadModel(IDocumentService documentService)
        {
            _documentService = documentService;
        }

        public async Task<IActionResult> OnGetAsync(int documentId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            {
                return DocumentNotFound();
            }

            var result = await _documentService.OpenDocumentContentAsync(documentId, userId, DocumentContentMode.Download);
            if (!result.Succeeded || result.Content == null)
            {
                if (result.Content != null)
                {
                    await result.Content.DisposeAsync();
                }

                return DocumentNotFound();
            }

            Response.Headers.CacheControl = "private, no-store";

            // FileStreamResult disposes the stream. fileDownloadName produces "Content-Disposition: attachment;
            // filename=...; filename*=UTF-8''..." for the original (sanitized) name.
            return File(result.Content, result.ContentType ?? "application/octet-stream", result.DownloadFileName);
        }

        private static IActionResult DocumentNotFound()
        {
            return new ContentResult
            {
                StatusCode = StatusCodes.Status404NotFound,
                ContentType = "text/plain",
                Content = DocumentMessages.NotFound
            };
        }
    }
}
