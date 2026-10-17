using System.Text.RegularExpressions;
using ContosoDashboard.Models;

namespace ContosoDashboard.Services;

/// <summary>
/// File and metadata rules (research R-11): the extension allow-list with canonical MIME types, the size limit,
/// file-name sanitizing, and title, description, category and tag validation.
/// Type is decided by the allow-listed extension, never by the browser-reported content type.
/// </summary>
public static class DocumentFileRules
{
    /// <summary>25 MB: 26,214,400 bytes. Exactly this size is accepted; one byte more is rejected.</summary>
    public const long MaxFileSizeBytes = 26_214_400;

    /// <summary>User-facing list of supported types.</summary>
    public const string SupportedTypesDescription =
        "PDF, Word (.doc, .docx), Excel (.xls, .xlsx), PowerPoint (.ppt, .pptx), text (.txt), JPEG, PNG";

    private const int MaxFileNameLength = 255;
    private const int MaxTitleLength = 200;
    private const int MaxDescriptionLength = 2000;
    private const int MaxTags = 10;
    private const int MaxTagLength = 50;
    private const string FallbackFileName = "document";

    private static readonly Dictionary<string, string> ExtensionToMime = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".txt"] = "text/plain",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png"
    };

    private static readonly HashSet<string> PreviewableTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/jpeg",
        "image/png"
    };

    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = "PDF",
        ["application/msword"] = "Word",
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = "Word",
        ["application/vnd.ms-excel"] = "Excel",
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = "Excel",
        ["application/vnd.ms-powerpoint"] = "PowerPoint",
        ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = "PowerPoint",
        ["text/plain"] = "Text",
        ["image/jpeg"] = "JPEG",
        ["image/png"] = "PNG"
    };

    /// <summary>True for application/pdf, image/jpeg and image/png.</summary>
    public static bool IsPreviewable(string? mimeType)
    {
        return mimeType != null && PreviewableTypes.Contains(mimeType);
    }

    /// <summary>
    /// Looks up the lower-case extension and canonical MIME for an allow-listed file name. The extension is taken from
    /// the sanitized name, so "report.pdf.exe" has the extension ".exe" and is rejected.
    /// </summary>
    public static bool TryGetFileType(string? fileName, out string extension, out string mimeType)
    {
        extension = string.Empty;
        mimeType = string.Empty;

        var extensionOfName = Path.GetExtension(SanitizeFileName(fileName)).ToLowerInvariant();
        if (extensionOfName.Length == 0 || !ExtensionToMime.TryGetValue(extensionOfName, out var mime))
        {
            return false;
        }

        extension = extensionOfName;
        mimeType = mime;
        return true;
    }

    /// <summary>Returns <see cref="DocumentErrorCode.None"/>, FileEmpty, FileTooLarge or UnsupportedFileType (checked in that order).</summary>
    public static DocumentErrorCode ValidateFile(string? fileName, long length)
    {
        if (length <= 0)
        {
            return DocumentErrorCode.FileEmpty;
        }

        if (length > MaxFileSizeBytes)
        {
            return DocumentErrorCode.FileTooLarge;
        }

        return TryGetFileType(fileName, out _, out _) ? DocumentErrorCode.None : DocumentErrorCode.UnsupportedFileType;
    }

    /// <summary>
    /// Returns a safe display and download name. It is used only for display and download, never for storage paths:
    /// path segments are dropped, invalid and control characters become '_', whitespace is collapsed and trimmed,
    /// the name is cut to 255 characters keeping its extension, and an empty result becomes "document".
    /// </summary>
    public static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return FallbackFileName;
        }

        // Drop path segments (both separators, whatever the host OS).
        var separatorIndex = fileName.LastIndexOfAny(new[] { '/', '\\' });
        var name = separatorIndex >= 0 ? fileName[(separatorIndex + 1)..] : fileName;

        // Replace invalid and control characters.
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => char.IsControl(c) || Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray();
        name = new string(chars);

        // Collapse whitespace and trim.
        name = Regex.Replace(name, @"\s+", " ").Trim();

        var extension = Path.GetExtension(name);

        // A name that is empty, or made only of dots or spaces, carries no usable name: fall back.
        if (name.Trim('.', ' ').Length == 0)
        {
            return FallbackFileName;
        }

        // An extension with no name in front of it (".pdf") becomes "document.pdf".
        if (Path.GetFileNameWithoutExtension(name).Trim().Length == 0)
        {
            name = FallbackFileName + extension;
        }

        if (name.Length > MaxFileNameLength)
        {
            name = extension.Length < MaxFileNameLength
                ? name[..(MaxFileNameLength - extension.Length)] + extension
                : name[..MaxFileNameLength];
        }

        return name;
    }

    /// <summary>
    /// Validates title (required, 1-200 characters after trimming), category (one of the six fixed names, ordinal)
    /// and description (at most 2,000 characters after trimming). Returns the validation detail message from
    /// <see cref="DocumentMessages"/>, or null when everything is valid. Services store the trimmed values.
    /// </summary>
    public static string? ValidateMetadata(string? title, string? category, string? description)
    {
        var trimmedTitle = title?.Trim() ?? string.Empty;
        if (trimmedTitle.Length < 1 || trimmedTitle.Length > MaxTitleLength)
        {
            return DocumentMessages.TitleInvalid;
        }

        if (!DocumentCategories.IsValid(category))
        {
            return DocumentMessages.CategoryInvalid;
        }

        if ((description?.Trim().Length ?? 0) > MaxDescriptionLength)
        {
            return DocumentMessages.DescriptionTooLong;
        }

        return null;
    }

    /// <summary>
    /// Trims tags, drops empty ones, removes duplicates (case-insensitive, first spelling wins), and enforces at most
    /// 10 tags of at most 50 characters each. Returns the normalized tags, or an empty list plus the validation
    /// detail message from <see cref="DocumentMessages"/> when a rule is broken.
    /// </summary>
    public static (IReadOnlyList<string> Tags, string? Error) NormalizeTags(IEnumerable<string>? tags)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in tags ?? Enumerable.Empty<string>())
        {
            var tag = raw?.Trim();
            if (string.IsNullOrEmpty(tag))
            {
                continue;
            }

            if (tag.Length > MaxTagLength)
            {
                return (Array.Empty<string>(), DocumentMessages.TagTooLong);
            }

            if (seen.Add(tag))
            {
                result.Add(tag);
            }
        }

        if (result.Count > MaxTags)
        {
            return (Array.Empty<string>(), DocumentMessages.TooManyTags);
        }

        return (result, null);
    }

    /// <summary>"PDF", "Word", "Excel", "PowerPoint", "Text", "JPEG" or "PNG" for a canonical MIME type; "File" otherwise.</summary>
    public static string GetDisplayName(string? mimeType)
    {
        return mimeType != null && DisplayNames.TryGetValue(mimeType, out var name) ? name : "File";
    }
}
