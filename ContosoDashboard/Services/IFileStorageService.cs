namespace ContosoDashboard.Services;

/// <summary>
/// Stores and retrieves file content by a relative, portable storage key.
/// Keys are caller-generated, use forward slashes, are relative, and are never derived from user-supplied file names.
/// They are valid both as local relative paths and as Azure blob names.
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Writes content to the caller-generated storage key and returns that key.
    /// The key is created by DocumentService as "{userId}/{projectId|personal}/{guid:N}{ext}".
    /// It is NEVER derived from a user-supplied file name. An existing key is never overwritten.
    /// </summary>
    Task<string> UploadAsync(Stream fileStream, string storageKey, string contentType);

    /// <summary>Deletes the content at the key. Idempotent: a missing key is not an error.</summary>
    Task DeleteAsync(string storageKey);

    /// <summary>
    /// Opens a read-only stream for the key. Throws <see cref="FileNotFoundException"/> if the key is missing.
    /// The caller owns and disposes the stream.
    /// </summary>
    Task<Stream> DownloadAsync(string storageKey);

    /// <summary>
    /// Returns a time-limited direct URL (future cloud implementation, for example a blob SAS).
    /// Local implementation: throws <see cref="NotSupportedException"/>. Local files are served only through
    /// the authorization-checked endpoints. No feature code calls this method.
    /// </summary>
    Task<string> GetUrlAsync(string storageKey, TimeSpan expiration);
}
