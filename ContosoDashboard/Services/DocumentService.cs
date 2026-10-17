using System.Linq.Expressions;
using ContosoDashboard.Data;
using ContosoDashboard.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContosoDashboard.Services;

/// <summary>
/// Document upload, listing and download (contracts\service-interfaces.md, "IDocumentService").
/// The interface grows story by story: this file holds the User Story 1 members.
/// </summary>
public interface IDocumentService
{
    // Upload (FR-001..FR-011, US1)
    Task<DocumentOperationResult> UploadDocumentAsync(
        DocumentUploadRequest request, Stream content, int requestingUserId, CancellationToken ct = default);
    Task<List<ProjectOption>> GetUploadableProjectsAsync(int requestingUserId);

    // Lists (FR-012)
    Task<PagedResult<DocumentSummary>> GetMyDocumentsAsync(int requestingUserId, DocumentQuery query);

    // Single document (FR-017, FR-018)
    Task<DocumentDetails?> GetDocumentAsync(int documentId, int requestingUserId);
    Task<DocumentContentResult> OpenDocumentContentAsync(int documentId, int requestingUserId, DocumentContentMode mode);
}

public class DocumentService : IDocumentService
{
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(10);

    // Copy of the previewable MIME list so the projection below translates to SQL (IN (...)).
    private static readonly string[] PreviewableTypes = DocumentFileRules.PreviewableMimeTypes.ToArray();

    /// <summary>EF-translatable projection from a document row to its list summary. IsPreviewable comes from DocumentFileRules.</summary>
    private static readonly Expression<Func<Document, DocumentSummary>> ToSummary = d => new DocumentSummary(
        d.DocumentId,
        d.Title,
        d.Category,
        d.UploadedDate,
        d.FileSizeBytes,
        d.FileType,
        PreviewableTypes.Contains(d.FileType),
        d.ProjectId,
        d.Project != null ? d.Project.Name : null,
        d.UploadedByUserId,
        d.UploadedByUser.DisplayName);

    private readonly ApplicationDbContext _context;
    private readonly IFileStorageService _storage;
    private readonly IMalwareScanner _scanner;
    private readonly INotificationService _notifications;
    private readonly ITaskService _tasks;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(
        ApplicationDbContext context,
        IFileStorageService storage,
        IMalwareScanner scanner,
        INotificationService notifications,
        ITaskService tasks,
        ILogger<DocumentService> logger)
    {
        _context = context;
        _storage = storage;
        _scanner = scanner;
        _notifications = notifications;
        _tasks = tasks;
        _logger = logger;
    }

    // ------------------------------------------------------------------------------------------------
    // Private helpers (T045)
    // ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Default sort UploadedDate descending, then DocumentId, then the requested page. TotalCount is the number of
    /// rows before paging. The projection and the paging run in SQL.
    /// </summary>
    private static async Task<PagedResult<DocumentSummary>> ApplyPaging(IQueryable<Document> source, DocumentQuery query)
    {
        var total = await source.CountAsync();

        var items = await source
            .OrderByDescending(d => d.UploadedDate)
            .ThenBy(d => d.DocumentId)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(ToSummary)
            .ToListAsync();

        return new PagedResult<DocumentSummary>(items, total, query.Page, query.PageSize);
    }

    /// <summary>"{uploaderId}/{projectId|personal}/{guid:N}{ext}": never derived from the user-supplied file name.</summary>
    private static string BuildStorageKey(int uploaderId, int? projectId, string extension)
    {
        return $"{uploaderId}/{(projectId.HasValue ? projectId.Value.ToString() : "personal")}/{Guid.NewGuid():N}{extension}";
    }

    private static DocumentContentResult ContentFailure(DocumentErrorCode error)
        => new(false, error, null, null, null, false);

    private DocumentOperationResult Reject(DocumentErrorCode code, string message, int userId, string fileName, string reason)
    {
        _logger.LogWarning(
            "Document upload rejected for user {UserId}, file '{FileName}': {Reason}", userId, fileName, reason);
        return DocumentOperationResult.Fail(code, message);
    }

    // ------------------------------------------------------------------------------------------------
    // Upload (T046, research R-04)
    // ------------------------------------------------------------------------------------------------

    public async Task<DocumentOperationResult> UploadDocumentAsync(
        DocumentUploadRequest request, Stream content, int requestingUserId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(content);

        var fileName = DocumentFileRules.SanitizeFileName(request.OriginalFileName);

        // 1. Load the requester's live role and department (R-02). An unknown user gets NotFound.
        var user = await DocumentAccessPolicy.LoadUserContextAsync(_context, requestingUserId);
        if (user == null)
        {
            return Reject(DocumentErrorCode.NotFound, DocumentMessages.NotFound, requestingUserId, fileName, "unknown user");
        }

        // The size rule applies to the stream length, never to a client-reported value. A non-seekable stream is
        // buffered (bounded to the limit plus one byte) so it can be length-checked, scanned and stored.
        var buffered = false;
        if (!content.CanSeek)
        {
            var copy = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while (copy.Length <= DocumentFileRules.MaxFileSizeBytes
                   && (read = await content.ReadAsync(chunk.AsMemory(0, chunk.Length), ct)) > 0)
            {
                copy.Write(chunk, 0, read);
            }

            copy.Position = 0;
            content = copy;
            buffered = true;
        }

        try
        {
            content.Position = 0;
            var length = content.Length;

            // 2. Validate the file (empty, size, extension), then the metadata and tags.
            var fileError = DocumentFileRules.ValidateFile(fileName, length);
            if (fileError != DocumentErrorCode.None)
            {
                return Reject(fileError, DocumentMessages.ForCode(fileError, fileName), requestingUserId, fileName, fileError.ToString());
            }

            var metadataError = DocumentFileRules.ValidateMetadata(request.Title, request.Category, request.Description);
            if (metadataError != null)
            {
                return Reject(DocumentErrorCode.Validation, metadataError, requestingUserId, fileName, "invalid metadata");
            }

            var (tags, tagError) = DocumentFileRules.NormalizeTags(request.Tags);
            if (tagError != null)
            {
                return Reject(DocumentErrorCode.Validation, tagError, requestingUserId, fileName, "invalid tags");
            }

            DocumentFileRules.TryGetFileType(fileName, out var extension, out var mimeType);

            // 3. Project uploads need membership, management, or the Administrator role. A project that doesn't
            //    exist gives the same result, so nothing about it is revealed.
            if (request.ProjectId.HasValue)
            {
                var projectId = request.ProjectId.Value;
                var allowed = await _context.Projects
                    .AsNoTracking()
                    .Where(p => p.ProjectId == projectId)
                    .Where(DocumentAccessPolicy.CanUploadToProject(user))
                    .AnyAsync(ct);
                if (!allowed)
                {
                    return Reject(DocumentErrorCode.Forbidden, DocumentMessages.Forbidden, requestingUserId, fileName, "project not allowed");
                }
            }

            // 4. Task uploads arrive with the task-attachment work (T101); until then a TaskId is invalid.
            if (request.TaskId.HasValue)
            {
                return Reject(DocumentErrorCode.Validation, DocumentMessages.Validation, requestingUserId, fileName, "task uploads not available");
            }

            // 5. Scan, with a 10-second timeout. Anything but a clean result fails closed.
            try
            {
                using var scanCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                scanCts.CancelAfter(ScanTimeout);
                var scan = await _scanner.ScanAsync(content, fileName, scanCts.Token);
                content.Position = 0;

                if (scan.Status == MalwareScanStatus.Infected)
                {
                    return Reject(DocumentErrorCode.MalwareDetected, DocumentMessages.MalwareDetected(fileName),
                        requestingUserId, fileName, "malware detected");
                }

                if (scan.Status != MalwareScanStatus.Clean)
                {
                    return Reject(DocumentErrorCode.ScanFailed, DocumentMessages.ScanFailed(fileName),
                        requestingUserId, fileName, "scanner returned an error");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Malware scan failed for user {UserId}, file '{FileName}'", requestingUserId, fileName);
                return Reject(DocumentErrorCode.ScanFailed, DocumentMessages.ScanFailed(fileName),
                    requestingUserId, fileName, "scanner failure or timeout");
            }

            // 6. Generate the key, then write the file (file first, then the record).
            var key = BuildStorageKey(requestingUserId, request.ProjectId, extension);
            try
            {
                content.Position = 0;
                await _storage.UploadAsync(content, key, mimeType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Storing the file failed for user {UserId}, file '{FileName}'", requestingUserId, fileName);
                return Reject(DocumentErrorCode.StorageFailed, DocumentMessages.StorageFailed,
                    requestingUserId, fileName, "storage failure");
            }

            // 7. Save the record: the Document with its tags, and the Upload activity. The activity needs the generated
            //    DocumentId (it has no navigation, so EF can't fix it up). Both saves therefore run in ONE transaction,
            //    which commits or rolls back as a unit.
            var now = DateTime.UtcNow;
            var title = request.Title.Trim();
            var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            var document = new Document
            {
                Title = title,
                Description = description,
                Category = request.Category,
                ProjectId = request.ProjectId,
                UploadedByUserId = requestingUserId,
                UploadedDate = now,
                LastModifiedDate = now,
                OriginalFileName = fileName,
                FileSizeBytes = length,
                FileType = mimeType,
                FilePath = key
            };
            foreach (var tag in tags)
            {
                document.Tags.Add(new DocumentTag { Tag = tag });
            }

            DocumentActivity? activity = null;
            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(ct);
                _context.Documents.Add(document);
                await _context.SaveChangesAsync(ct);

                activity = new DocumentActivity
                {
                    DocumentId = document.DocumentId,
                    DocumentTitle = title,
                    FileType = mimeType,
                    UserId = requestingUserId,
                    Action = DocumentActivityAction.Upload,
                    OccurredAt = now
                };
                _context.DocumentActivities.Add(activity);
                await _context.SaveChangesAsync(ct);

                await transaction.CommitAsync(ct);
            }
            catch (Exception ex)
            {
                // 8. Compensate: nothing was saved, so remove the file (best effort). A leftover file is never served.
                DetachAdded(document, activity);
                _logger.LogWarning(ex, "Saving the document record failed for user {UserId}, file '{FileName}'; removing the stored file",
                    requestingUserId, fileName);
                try
                {
                    await _storage.DeleteAsync(key);
                }
                catch (Exception deleteEx)
                {
                    _logger.LogWarning(deleteEx, "Could not remove the stored file with key {StorageKey} after a failed save; the leftover file is never served", key);
                }

                return DocumentOperationResult.Fail(DocumentErrorCode.StorageFailed, DocumentMessages.StorageFailed);
            }

            _logger.LogInformation(
                "Document {DocumentId} uploaded by user {UserId}: '{FileName}', {Length} bytes", document.DocumentId, requestingUserId, fileName, length);
            return DocumentOperationResult.Ok(document.DocumentId);
        }
        finally
        {
            if (buffered)
            {
                await content.DisposeAsync();
            }
        }
    }

    /// <summary>Stops the (long-lived, per-circuit) context from retrying rows whose save failed.</summary>
    private void DetachAdded(Document document, DocumentActivity? activity)
    {
        foreach (var tag in document.Tags)
        {
            _context.Entry(tag).State = EntityState.Detached;
        }

        _context.Entry(document).State = EntityState.Detached;
        if (activity != null)
        {
            _context.Entry(activity).State = EntityState.Detached;
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Uploadable projects and My Documents (T047)
    // ------------------------------------------------------------------------------------------------

    public async Task<List<ProjectOption>> GetUploadableProjectsAsync(int requestingUserId)
    {
        var user = await DocumentAccessPolicy.LoadUserContextAsync(_context, requestingUserId);
        if (user == null)
        {
            return new List<ProjectOption>();
        }

        return await _context.Projects
            .AsNoTracking()
            .Where(DocumentAccessPolicy.CanUploadToProject(user))
            .OrderBy(p => p.Name)
            .ThenBy(p => p.ProjectId)
            .Select(p => new ProjectOption(p.ProjectId, p.Name))
            .ToListAsync();
    }

    public async Task<PagedResult<DocumentSummary>> GetMyDocumentsAsync(int requestingUserId, DocumentQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var mine = _context.Documents
            .AsNoTracking()
            .Where(d => d.UploadedByUserId == requestingUserId);

        return await ApplyPaging(mine, query);
    }

    // ------------------------------------------------------------------------------------------------
    // Single document and download (T048)
    // ------------------------------------------------------------------------------------------------

    public async Task<DocumentDetails?> GetDocumentAsync(int documentId, int requestingUserId)
    {
        var user = await DocumentAccessPolicy.LoadUserContextAsync(_context, requestingUserId);
        if (user == null)
        {
            return null;
        }

        var accessible = _context.Documents
            .AsNoTracking()
            .Where(d => d.DocumentId == documentId)
            .Where(DocumentAccessPolicy.CanAccess(user));

        var summary = await accessible.Select(ToSummary).FirstOrDefaultAsync();
        if (summary == null)
        {
            return null;
        }

        var extras = await accessible
            .Select(d => new
            {
                d.Description,
                d.OriginalFileName,
                d.LastModifiedDate,
                Tags = d.Tags.Select(t => t.Tag).OrderBy(t => t).ToList()
            })
            .FirstOrDefaultAsync();
        if (extras == null)
        {
            return null;
        }

        var canManage = await accessible.Where(DocumentAccessPolicy.CanManage(user)).AnyAsync();
        var isOwner = await accessible.Where(DocumentAccessPolicy.IsOwner(user)).AnyAsync();

        // CanEditMetadata = CanDelete = CanManage; CanReplaceFile = CanShare = IsOwner. No activity is recorded.
        return new DocumentDetails(
            summary, extras.Description, extras.Tags, extras.OriginalFileName, extras.LastModifiedDate,
            CanEditMetadata: canManage, CanDelete: canManage, CanReplaceFile: isOwner, CanShare: isOwner);
    }

    public async Task<DocumentContentResult> OpenDocumentContentAsync(
        int documentId, int requestingUserId, DocumentContentMode mode)
    {
        var user = await DocumentAccessPolicy.LoadUserContextAsync(_context, requestingUserId);
        if (user == null)
        {
            return ContentFailure(DocumentErrorCode.NotFound);
        }

        var document = await _context.Documents
            .AsNoTracking()
            .Where(d => d.DocumentId == documentId)
            .Where(DocumentAccessPolicy.CanAccess(user))
            .Select(d => new { d.DocumentId, d.Title, d.FilePath, d.FileType, d.OriginalFileName })
            .FirstOrDefaultAsync();
        if (document == null)
        {
            return ContentFailure(DocumentErrorCode.NotFound);
        }

        if (mode == DocumentContentMode.Preview)
        {
            // Preview arrives with T069.
            return ContentFailure(DocumentErrorCode.UnsupportedFileType);
        }

        Stream stream;
        try
        {
            stream = await _storage.DownloadAsync(document.FilePath);
        }
        catch (Exception ex)
        {
            // A missing or unreadable file: the endpoint maps this to the same 404 as "not found".
            _logger.LogError(ex, "The stored file for document {DocumentId} could not be opened (key {StorageKey})",
                document.DocumentId, document.FilePath);
            return ContentFailure(DocumentErrorCode.StorageFailed);
        }

        try
        {
            _context.DocumentActivities.Add(new DocumentActivity
            {
                DocumentId = document.DocumentId,
                DocumentTitle = document.Title,
                UserId = requestingUserId,
                Action = DocumentActivityAction.Download,
                OccurredAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            await stream.DisposeAsync();
            foreach (var entry in _context.ChangeTracker.Entries<DocumentActivity>()
                         .Where(e => e.State == EntityState.Added).ToList())
            {
                entry.State = EntityState.Detached;
            }

            _logger.LogError(ex, "Recording the download of document {DocumentId} failed", document.DocumentId);
            return ContentFailure(DocumentErrorCode.StorageFailed);
        }

        return new DocumentContentResult(
            true, DocumentErrorCode.None, stream, document.FileType, document.OriginalFileName, Inline: false);
    }
}
