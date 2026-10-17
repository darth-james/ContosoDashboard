using Microsoft.Extensions.Options;

namespace ContosoDashboard.Services;

/// <summary>Bound to the "DocumentStorage" configuration section.</summary>
public sealed class DocumentStorageOptions
{
    public string RootPath { get; set; } = "AppData/uploads";
}

/// <summary>
/// Local filesystem implementation of <see cref="IFileStorageService"/> (research R-05).
/// The root is <c>DocumentStorage:RootPath</c> resolved against the content root (outside wwwroot, created on demand).
/// Keys are caller-generated relative paths with forward slashes and are never derived from user file names.
/// </summary>
public sealed class LocalFileStorageService : IFileStorageService
{
    private const string DefaultRootPath = "AppData/uploads";
    private static readonly char[] InvalidKeyChars = Path.GetInvalidFileNameChars().Where(c => c != '/').ToArray();

    private readonly string _root;
    private readonly string _rootWithSeparator;

    /// <summary>Production constructor: the root is resolved against the web host's content root.</summary>
    public LocalFileStorageService(IOptions<DocumentStorageOptions> options, IWebHostEnvironment environment)
        : this(options, environment.ContentRootPath)
    {
    }

    /// <summary>Explicit-content-root constructor, used by tests (an absolute RootPath is used as-is).</summary>
    public LocalFileStorageService(IOptions<DocumentStorageOptions> options, string contentRootPath)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        var configured = options.Value.RootPath;
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = DefaultRootPath;
        }

        var contentRoot = Path.GetFullPath(contentRootPath);
        _root = TrimSeparators(Path.GetFullPath(Path.Combine(contentRoot, configured)));
        _rootWithSeparator = _root + Path.DirectorySeparatorChar;

        // Uploaded files must never be reachable through static files.
        var webRoot = Path.Combine(TrimSeparators(contentRoot), "wwwroot");
        if (_root.Equals(webRoot, PathComparison) ||
            _root.StartsWith(webRoot + Path.DirectorySeparatorChar, PathComparison))
        {
            throw new InvalidOperationException("The document storage root must be outside wwwroot.");
        }
    }

    /// <summary>The fully resolved storage root (never under wwwroot).</summary>
    public string RootPath => _root;

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public async Task<string> UploadAsync(Stream fileStream, string storageKey, string contentType)
    {
        ArgumentNullException.ThrowIfNull(fileStream);
        var path = ResolvePath(storageKey);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // CreateNew never overwrites. If the key already exists this throws IOException and the
        // existing file is left alone (it is not ours to delete).
        var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        try
        {
            await using (target)
            {
                await fileStream.CopyToAsync(target);
                await target.FlushAsync();
            }
        }
        catch
        {
            // The partial file is ours: remove it, then rethrow.
            TryDelete(path);
            throw;
        }

        return storageKey;
    }

    public Task DeleteAsync(string storageKey)
    {
        var path = ResolvePath(storageKey);
        TryDelete(path);
        return Task.CompletedTask;
    }

    public Task<Stream> DownloadAsync(string storageKey)
    {
        var path = ResolvePath(storageKey);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The stored file was not found.", storageKey);
        }

        try
        {
            Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            return Task.FromResult(stream);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new FileNotFoundException("The stored file was not found.", storageKey, ex);
        }
    }

    public Task<string> GetUrlAsync(string storageKey, TimeSpan expiration)
    {
        throw new NotSupportedException(
            "Local files are served only through the authorization-checked document endpoints.");
    }

    /// <summary>Validates the key and resolves it to a full path that is guaranteed to be inside the root.</summary>
    private string ResolvePath(string storageKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        if (Path.IsPathRooted(storageKey) || storageKey.StartsWith('/'))
        {
            throw new ArgumentException("The storage key must be relative.", nameof(storageKey));
        }

        if (storageKey.Contains('\\'))
        {
            throw new ArgumentException("The storage key must use forward slashes.", nameof(storageKey));
        }

        if (storageKey.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("The storage key must not contain '..'.", nameof(storageKey));
        }

        if (storageKey.IndexOfAny(InvalidKeyChars) >= 0)
        {
            throw new ArgumentException("The storage key contains invalid characters.", nameof(storageKey));
        }

        foreach (var segment in storageKey.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                throw new ArgumentException("The storage key contains an empty or '.' segment.", nameof(storageKey));
            }
        }

        var fullPath = Path.GetFullPath(Path.Combine(_root, storageKey));
        if (!fullPath.StartsWith(_rootWithSeparator, PathComparison))
        {
            throw new ArgumentException("The storage key resolves outside the storage root.", nameof(storageKey));
        }

        return fullPath;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Already gone: delete is idempotent.
        }
    }

    private static string TrimSeparators(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
