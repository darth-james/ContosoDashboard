namespace ContosoDashboard.Services;

// Shared types for the document services (contracts\service-interfaces.md, "Shared types").
// Share, task, and report records live in their own service files.

public enum DocumentErrorCode
{
    None,
    NotFound,
    Forbidden,
    Validation,
    FileEmpty,
    FileTooLarge,
    UnsupportedFileType,
    MalwareDetected,
    ScanFailed,
    StorageFailed,
    CrossProject,
    InvalidShareTarget
}

public sealed record DocumentOperationResult(
    bool Succeeded, DocumentErrorCode Error, string? Message, int? DocumentId = null)
{
    public static DocumentOperationResult Ok(int? documentId = null, string? message = null)
        => new(true, DocumentErrorCode.None, message, documentId);

    public static DocumentOperationResult Fail(DocumentErrorCode error, string message)
        => new(false, error, message);
}

public enum DocumentSortField
{
    UploadedDate,
    Title,
    Category,
    FileSize
}

public sealed class DocumentQuery
{
    private readonly int _page = 1;
    private readonly int _pageSize = 25;

    /// <summary>Must be in DocumentCategories.All when set.</summary>
    public string? Category { get; init; }
    public int? ProjectId { get; init; }

    /// <summary>Inclusive, UTC day.</summary>
    public DateOnly? FromDate { get; init; }

    /// <summary>Inclusive, UTC day.</summary>
    public DateOnly? ToDate { get; init; }

    /// <summary>Team Documents (Administrator only); ignored elsewhere.</summary>
    public string? Department { get; init; }

    public DocumentSortField SortBy { get; init; } = DocumentSortField.UploadedDate;
    public bool Descending { get; init; } = true;

    /// <summary>1-based. Values below 1 are raised to 1.</summary>
    public int Page
    {
        get => _page;
        init => _page = Math.Max(1, value);
    }

    /// <summary>Clamped to 1..100.</summary>
    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = Math.Clamp(value, 1, 100);
    }
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

public sealed record DocumentSummary(
    int DocumentId, string Title, string Category, DateTime UploadedDate, long FileSizeBytes,
    string FileType, bool IsPreviewable, int? ProjectId, string? ProjectName,
    int UploadedByUserId, string UploadedByName);

public sealed record SharedDocumentSummary(
    DocumentSummary Document, string SharedByName, DateTime SharedDate, bool ViaTeam);

public sealed record DocumentDetails(
    DocumentSummary Summary, string? Description, IReadOnlyList<string> Tags,
    string OriginalFileName, DateTime LastModifiedDate,
    bool CanEditMetadata, bool CanDelete, bool CanReplaceFile, bool CanShare);

public sealed record DocumentUploadRequest(
    string OriginalFileName, string Title, string Category, string? Description,
    int? ProjectId, IReadOnlyList<string> Tags, int? TaskId = null);

public sealed record DocumentMetadataUpdate(
    string Title, string Category, string? Description, IReadOnlyList<string> Tags);

public enum DocumentContentMode
{
    Download,
    Preview
}

/// <summary>The caller disposes <see cref="Content"/>.</summary>
public sealed record DocumentContentResult(
    bool Succeeded, DocumentErrorCode Error, Stream? Content, string? ContentType,
    string? DownloadFileName, bool Inline);

public sealed record ProjectOption(int ProjectId, string Name);

/// <summary>
/// Every user-facing document message, word for word from contracts\ui-routes.md "User-facing messages".
/// Pages and services use these and never invent their own wording.
/// </summary>
public static class DocumentMessages
{
    // ---- Error codes ----
    public static string FileTooLarge(string name) => $"'{name}' is larger than the 25 MB limit.";
    public static string FileEmpty(string name) => $"'{name}' is empty. Choose a file that contains data.";
    public static string UnsupportedFileType(string name) =>
        $"'{name}' is not a supported file type. Supported types: PDF, Word (.doc, .docx), Excel (.xls, .xlsx), PowerPoint (.ppt, .pptx), text (.txt), JPEG, PNG.";
    public static string MalwareDetected(string name) => $"'{name}' was rejected for security reasons.";
    public static string ScanFailed(string name) => $"'{name}' could not be checked for malware. Please try again later.";

    public const string NotFound = "Document not found or not available.";
    public const string Forbidden = "You don't have permission to perform this action.";
    public const string CrossProject = "This document belongs to a different project and can't be attached to this task.";
    public const string StorageFailed = "The file could not be saved. Please try again.";
    public const string InvalidShareTarget = "Choose at least one other user, or a team that has members.";
    public const string Validation = "Some details are missing or invalid. Check the highlighted fields and try again.";

    // ---- Validation details (shown next to the field, with the Validation code) ----
    public const string TitleInvalid = "Enter a title of 1 to 200 characters.";
    public const string CategoryInvalid = "Choose a category.";
    public const string DescriptionTooLong = "The description can't be longer than 2,000 characters.";
    public const string TooManyTags = "Use at most 10 tags.";
    public const string TagTooLong = "Each tag can be at most 50 characters.";
    public const string SearchTermInvalid = "Enter a search term of 1 to 100 characters.";
    public const string TooManyFiles = "You can upload up to 10 files at a time. Select 10 or fewer files.";

    // ---- Other messages ----
    public const string TaskNotFound = "Task not found or not available.";
    public const string TaskProjectMembersOnly = "Only members of this task's project can add documents to it.";
    public const string PreviewNotAvailable = "Preview isn't available for this file type. Download it instead.";
    public static string Uploaded(string name) => $"'{name}' was uploaded.";
    public const string DetailsSaved = "Document details saved.";
    public const string FileReplaced = "The file was replaced.";
    public const string DocumentDeleted = "The document was deleted.";
    public const string Shared = "The document was shared.";
    public const string ShareRemoved = "The share was removed.";
    public const string AttachedToTask = "The document was attached to the task.";
    public const string RemovedFromTask = "The document was removed from the task.";

    public const string DeleteConfirmTitle = "Delete document?";
    public static string DeleteConfirmMessage(string title) =>
        $"'{title}' and its file will be permanently deleted. This can't be undone.";
    public const string DeleteConfirmButton = "Delete";

    public const string RemoveFromTaskConfirmTitle = "Remove from task?";
    public static string RemoveFromTaskConfirmMessage(string title) =>
        $"'{title}' will be removed from this task. The document itself isn't deleted and keeps its project.";
    public const string RemoveFromTaskConfirmButton = "Remove";

    // ---- Empty states ----
    public const string EmptyDefault = "No documents found.";
    public const string EmptyMyDocuments = "You haven't uploaded any documents yet.";
    public const string EmptyProjectDocuments = "No documents have been added to this project yet.";
    public const string EmptySharedWithMe = "No documents have been shared with you.";
    public const string EmptyTeamDocuments = "No team documents found.";
    public const string EmptyTaskAttachments = "No documents are attached to this task.";
    public const string EmptyAttachPicker = "No documents can be attached to this task.";
    public const string EmptyActivity = "No activity in this date range.";

    /// <summary>Maps an error code to its user-facing message. <paramref name="name"/> is the sanitized file name.</summary>
    public static string ForCode(DocumentErrorCode code, string? name)
    {
        var fileName = name ?? string.Empty;
        return code switch
        {
            DocumentErrorCode.FileTooLarge => FileTooLarge(fileName),
            DocumentErrorCode.FileEmpty => FileEmpty(fileName),
            DocumentErrorCode.UnsupportedFileType => UnsupportedFileType(fileName),
            DocumentErrorCode.MalwareDetected => MalwareDetected(fileName),
            DocumentErrorCode.ScanFailed => ScanFailed(fileName),
            DocumentErrorCode.NotFound => NotFound,
            DocumentErrorCode.Forbidden => Forbidden,
            DocumentErrorCode.CrossProject => CrossProject,
            DocumentErrorCode.StorageFailed => StorageFailed,
            DocumentErrorCode.InvalidShareTarget => InvalidShareTarget,
            DocumentErrorCode.Validation => Validation,
            _ => string.Empty
        };
    }
}
