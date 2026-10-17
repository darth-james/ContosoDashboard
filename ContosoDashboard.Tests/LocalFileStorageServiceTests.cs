using ContosoDashboard.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace ContosoDashboard.Tests;

/// <summary>
/// Tests for LocalFileStorageService (T026). Uses its own temp folder, no database.
/// New tests that call stubbed members fail at runtime (red) until T031 replaces the stubs.
/// </summary>
[Trait("Story", "Foundation")]
public sealed class LocalFileStorageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ContosoDashboardTests", "storage-" + Guid.NewGuid().ToString("N"));
    private readonly LocalFileStorageService _storage;

    public LocalFileStorageServiceTests()
    {
        _storage = TestServiceFactory.CreateFileStorage(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception)
        {
            // Best-effort cleanup.
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("C:\\x")]
    [InlineData("/x")]
    [InlineData("a/../b.pdf")]
    [InlineData("..")]
    [InlineData("a\\b.pdf")]
    [InlineData("a/b<c.pdf")]
    public async Task UploadAsync_RejectsInvalidKeys(string? key)
    {
        using var content = new MemoryStream(new byte[] { 1, 2, 3 });

        await Assert.ThrowsAnyAsync<ArgumentException>(() => _storage.UploadAsync(content, key!, "application/pdf"));
    }

    [Theory]
    [InlineData("../outside.pdf")]
    [InlineData("sub/../../outside.pdf")]
    public async Task UploadAsync_RejectsAKeyThatResolvesOutsideTheRoot(string key)
    {
        using var content = new MemoryStream(new byte[] { 1, 2, 3 });
        var outside = Path.GetFullPath(Path.Combine(_root, key));

        await Assert.ThrowsAnyAsync<ArgumentException>(() => _storage.UploadAsync(content, key, "application/pdf"));

        Assert.False(File.Exists(outside));
    }

    [Fact]
    public async Task UploadThenDownload_RoundTripsIdenticalBytes()
    {
        var bytes = new byte[5000];
        new Random(42).NextBytes(bytes);
        var key = $"4/personal/{Guid.NewGuid():N}.pdf";

        using (var content = new MemoryStream(bytes))
        {
            var returnedKey = await _storage.UploadAsync(content, key, "application/pdf");
            Assert.Equal(key, returnedKey);
        }

        await using var downloaded = await _storage.DownloadAsync(key);
        using var copy = new MemoryStream();
        await downloaded.CopyToAsync(copy);

        Assert.Equal(bytes, copy.ToArray());
        Assert.True(File.Exists(Path.Combine(_storage.RootPath, key.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public async Task UploadAsync_ToAnExistingKeyThrowsAndDoesNotOverwrite()
    {
        var key = $"4/personal/{Guid.NewGuid():N}.txt";
        using (var first = new MemoryStream(new byte[] { 1, 1, 1 }))
        {
            await _storage.UploadAsync(first, key, "text/plain");
        }

        using var second = new MemoryStream(new byte[] { 9, 9, 9, 9 });
        await Assert.ThrowsAnyAsync<IOException>(() => _storage.UploadAsync(second, key, "text/plain"));

        await using var downloaded = await _storage.DownloadAsync(key);
        using var copy = new MemoryStream();
        await downloaded.CopyToAsync(copy);
        Assert.Equal(new byte[] { 1, 1, 1 }, copy.ToArray());
    }

    [Fact]
    public async Task UploadAsync_ThatFailsPartwayDeletesThePartialFileAndRethrows()
    {
        var key = $"4/personal/{Guid.NewGuid():N}.pdf";
        using var failing = new FailingAfterFirstReadStream();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _storage.UploadAsync(failing, key, "application/pdf"));

        Assert.False(File.Exists(Path.Combine(_storage.RootPath, key.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public async Task DeleteAsync_OnAMissingKeyDoesNotThrow()
    {
        await _storage.DeleteAsync($"4/personal/{Guid.NewGuid():N}.pdf");
    }

    [Fact]
    public async Task DeleteAsync_RemovesAnExistingFile()
    {
        var key = $"4/personal/{Guid.NewGuid():N}.png";
        using (var content = new MemoryStream(new byte[] { 5, 6, 7 }))
        {
            await _storage.UploadAsync(content, key, "image/png");
        }

        await _storage.DeleteAsync(key);

        await Assert.ThrowsAsync<FileNotFoundException>(() => _storage.DownloadAsync(key));
    }

    [Theory]
    [InlineData("missing.pdf")]
    [InlineData("no-such-folder/missing.pdf")]
    public async Task DownloadAsync_OnAMissingKeyThrowsFileNotFoundException(string key)
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() => _storage.DownloadAsync(key));
    }

    [Fact]
    public async Task GetUrlAsync_ThrowsNotSupportedException()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => _storage.GetUrlAsync("4/personal/x.pdf", TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void ResolvedRoot_IsNeverUnderWwwroot()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "ContosoDashboardTests", "content-" + Guid.NewGuid().ToString("N"));
        var options = Options.Create(new DocumentStorageOptions()); // default "AppData/uploads"
        var storage = new LocalFileStorageService(options, contentRoot);

        var segments = storage.RootPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        Assert.DoesNotContain("wwwroot", segments, StringComparer.OrdinalIgnoreCase);
        Assert.StartsWith(Path.GetFullPath(contentRoot), storage.RootPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("AppData", "uploads"), storage.RootPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DocumentStorageOptions_DefaultsToAppDataUploads()
    {
        Assert.Equal("AppData/uploads", new DocumentStorageOptions().RootPath);
    }

    /// <summary>Yields a few bytes, then fails, to simulate a write that breaks partway.</summary>
    private sealed class FailingAfterFirstReadStream : Stream
    {
        private bool _served;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_served)
            {
                throw new InvalidOperationException("Simulated read failure");
            }

            _served = true;
            var n = Math.Min(count, 16);
            Array.Fill(buffer, (byte)7, offset, n);
            return n;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_served)
            {
                throw new InvalidOperationException("Simulated read failure");
            }

            _served = true;
            var n = Math.Min(buffer.Length, 16);
            buffer.Span[..n].Fill(7);
            return new ValueTask<int>(n);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
