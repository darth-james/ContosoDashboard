using ContosoDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ContosoDashboard.Tests.Fakes;

/// <summary>Scanner that reports a scanner failure by returning <see cref="MalwareScanStatus.Error"/>.</summary>
public sealed class ErrorMalwareScanner : IMalwareScanner
{
    public Task<MalwareScanResult> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
        => Task.FromResult(new MalwareScanResult(MalwareScanStatus.Error, "Simulated scanner error"));
}

/// <summary>Scanner that throws <see cref="InvalidOperationException"/>.</summary>
public sealed class ThrowingMalwareScanner : IMalwareScanner
{
    public Task<MalwareScanResult> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Simulated scanner failure");
}

/// <summary>Scanner that throws <see cref="OperationCanceledException"/>, simulating the about-10-second scan timeout.</summary>
public sealed class CancelingMalwareScanner : IMalwareScanner
{
    public Task<MalwareScanResult> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
        => throw new OperationCanceledException("Simulated scan timeout");
}

/// <summary>Wraps a real storage service and records every Upload, Delete and Download key, in call order.</summary>
public sealed class RecordingFileStorageService : IFileStorageService
{
    private readonly IFileStorageService _inner;
    private readonly List<string> _uploads = new();
    private readonly List<string> _deletes = new();
    private readonly List<string> _downloads = new();
    private readonly List<string> _calls = new();

    public RecordingFileStorageService(IFileStorageService inner)
    {
        _inner = inner;
    }

    public IReadOnlyList<string> Uploads => _uploads;
    public IReadOnlyList<string> Deletes => _deletes;
    public IReadOnlyList<string> Downloads => _downloads;

    /// <summary>Every call in order, as "Upload:{key}", "Delete:{key}" or "Download:{key}".</summary>
    public IReadOnlyList<string> Calls => _calls;

    public async Task<string> UploadAsync(Stream fileStream, string storageKey, string contentType)
    {
        _uploads.Add(storageKey);
        _calls.Add($"Upload:{storageKey}");
        return await _inner.UploadAsync(fileStream, storageKey, contentType);
    }

    public async Task DeleteAsync(string storageKey)
    {
        _deletes.Add(storageKey);
        _calls.Add($"Delete:{storageKey}");
        await _inner.DeleteAsync(storageKey);
    }

    public async Task<Stream> DownloadAsync(string storageKey)
    {
        _downloads.Add(storageKey);
        _calls.Add($"Download:{storageKey}");
        return await _inner.DownloadAsync(storageKey);
    }

    public Task<string> GetUrlAsync(string storageKey, TimeSpan expiration) => _inner.GetUrlAsync(storageKey, expiration);
}

/// <summary>
/// Wraps a real storage service. <see cref="UploadAsync"/> and <see cref="DeleteAsync"/> can each be configured to throw
/// <see cref="IOException"/> (upload failure for T043, delete failure for T077).
/// </summary>
public sealed class FailingFileStorageService : IFileStorageService
{
    private readonly IFileStorageService _inner;

    public FailingFileStorageService(IFileStorageService inner)
    {
        _inner = inner;
    }

    public bool FailUpload { get; set; }
    public bool FailDelete { get; set; }

    public Task<string> UploadAsync(Stream fileStream, string storageKey, string contentType)
    {
        if (FailUpload)
        {
            throw new IOException("Simulated upload failure");
        }

        return _inner.UploadAsync(fileStream, storageKey, contentType);
    }

    public Task DeleteAsync(string storageKey)
    {
        if (FailDelete)
        {
            throw new IOException("Simulated delete failure");
        }

        return _inner.DeleteAsync(storageKey);
    }

    public Task<Stream> DownloadAsync(string storageKey) => _inner.DownloadAsync(storageKey);

    public Task<string> GetUrlAsync(string storageKey, TimeSpan expiration) => _inner.GetUrlAsync(storageKey, expiration);
}

/// <summary>Throws <see cref="DbUpdateException"/> from <c>SavingChangesAsync</c> while <see cref="Armed"/> is true.</summary>
public sealed class ThrowingSaveChangesInterceptor : SaveChangesInterceptor
{
    public bool Armed { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Armed)
        {
            throw new DbUpdateException("Simulated save failure");
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (Armed)
        {
            throw new DbUpdateException("Simulated save failure");
        }

        return base.SavingChanges(eventData, result);
    }
}
