using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ContosoDashboard.Tests;

/// <summary>
/// Write-order and rejection tests (T043; SC-007, FR-011). Every rejection leaves no file, no Documents row and no Upload
/// activity behind. Partial class: US4 adds its replace-file cases in a second file. Put the Story trait on each method.
/// </summary>
[Collection("LocalDb")]
public partial class DocumentWriteOrderTests
{
    private const int NiKang = 4;

    private readonly LocalDbFixture _fixture;

    public DocumentWriteOrderTests(LocalDbFixture fixture)
    {
        _fixture = fixture;
    }

    private static DocumentUploadRequest Request(string fileName, string? title = null)
        => new(fileName, title ?? $"Write order {Guid.NewGuid():N}", "Reports", null, null, Array.Empty<string>());

    private int FileCount()
    {
        return Directory.Exists(_fixture.StorageRoot)
            ? Directory.GetFiles(_fixture.StorageRoot, "*", SearchOption.AllDirectories).Length
            : 0;
    }

    private sealed record Snapshot(int Files, int Documents, int UploadActivities);

    private async Task<Snapshot> TakeSnapshotAsync()
    {
        await using var context = _fixture.CreateContext();
        return new Snapshot(
            FileCount(),
            await context.Documents.CountAsync(),
            await context.DocumentActivities.CountAsync(a => a.Action == DocumentActivityAction.Upload));
    }

    private async Task AssertNothingLeftBehindAsync(Snapshot before)
    {
        var after = await TakeSnapshotAsync();
        Assert.Equal(before.Files, after.Files);
        Assert.Equal(before.Documents, after.Documents);
        Assert.Equal(before.UploadActivities, after.UploadActivities);
    }

    public static IEnumerable<object[]> RejectedFiles()
    {
        yield return new object[] { "too-big.pdf", 26_214_401L, DocumentErrorCode.FileTooLarge };
        yield return new object[] { "empty.pdf", 0L, DocumentErrorCode.FileEmpty };
        yield return new object[] { "malware.exe", 1024L, DocumentErrorCode.UnsupportedFileType };
        yield return new object[] { "archive.zip", 1024L, DocumentErrorCode.UnsupportedFileType };
        yield return new object[] { "report.pdf.exe", 1024L, DocumentErrorCode.UnsupportedFileType };
        yield return new object[] { "no-extension", 1024L, DocumentErrorCode.UnsupportedFileType };
    }

    [Theory]
    [MemberData(nameof(RejectedFiles))]
    [Trait("Story", "US1")]
    public async Task Upload_RejectedFile_ReturnsSpecificErrorAndLeavesNothingBehind(
        string fileName, long length, DocumentErrorCode expected)
    {
        var before = await TakeSnapshotAsync();
        await using var context = _fixture.CreateContext();
        var storage = new RecordingFileStorageService(TestServiceFactory.CreateFileStorage(_fixture));
        var service = TestServiceFactory.CreateDocumentService(context, storage);

        var result = await service.UploadDocumentAsync(
            Request(fileName), new MemoryStream(DocumentUploadTests.PdfBytes((int)length)), NiKang);

        Assert.False(result.Succeeded);
        Assert.Equal(expected, result.Error);
        Assert.Empty(storage.Uploads);
        await AssertNothingLeftBehindAsync(before);
    }

    [Fact]
    [Trait("Story", "US1")]
    public async Task Upload_EicarContent_IsRejectedByTheScannerBeforeAnyStorageCall()
    {
        var before = await TakeSnapshotAsync();
        await using var context = _fixture.CreateContext();
        var storage = new RecordingFileStorageService(TestServiceFactory.CreateFileStorage(_fixture));
        var service = TestServiceFactory.CreateDocumentService(context, storage);

        var result = await service.UploadDocumentAsync(
            Request("eicar-test.txt"), new MemoryStream(EicarTestBytes.WithLeadingLine()), NiKang);

        Assert.False(result.Succeeded);
        Assert.Equal(DocumentErrorCode.MalwareDetected, result.Error);
        Assert.Empty(storage.Uploads);
        Assert.Empty(storage.Calls);
        await AssertNothingLeftBehindAsync(before);
    }

    public static IEnumerable<object[]> FailingScanners()
    {
        yield return new object[] { new ErrorMalwareScanner() };
        yield return new object[] { new ThrowingMalwareScanner() };
        yield return new object[] { new CancelingMalwareScanner() };
    }

    [Theory]
    [MemberData(nameof(FailingScanners))]
    [Trait("Story", "US1")]
    public async Task Upload_ScannerFailure_FailsClosedWithScanFailed(IMalwareScanner scanner)
    {
        var before = await TakeSnapshotAsync();
        await using var context = _fixture.CreateContext();
        var storage = new RecordingFileStorageService(TestServiceFactory.CreateFileStorage(_fixture));
        var service = TestServiceFactory.CreateDocumentService(context, storage, scanner);

        var result = await service.UploadDocumentAsync(
            Request("scan-failure.pdf"), new MemoryStream(DocumentUploadTests.PdfBytes(1024)), NiKang);

        Assert.False(result.Succeeded);
        Assert.Equal(DocumentErrorCode.ScanFailed, result.Error);
        Assert.Empty(storage.Uploads);
        await AssertNothingLeftBehindAsync(before);
    }

    [Fact]
    [Trait("Story", "US1")]
    public async Task Upload_StorageFailure_ReturnsStorageFailedAndNoRow()
    {
        var before = await TakeSnapshotAsync();
        await using var context = _fixture.CreateContext();
        var storage = new FailingFileStorageService(TestServiceFactory.CreateFileStorage(_fixture)) { FailUpload = true };
        var service = TestServiceFactory.CreateDocumentService(context, storage);

        var result = await service.UploadDocumentAsync(
            Request("storage-failure.pdf"), new MemoryStream(DocumentUploadTests.PdfBytes(1024)), NiKang);

        Assert.False(result.Succeeded);
        Assert.Equal(DocumentErrorCode.StorageFailed, result.Error);
        await AssertNothingLeftBehindAsync(before);
    }

    [Fact]
    [Trait("Story", "US1")]
    public async Task Upload_SaveFailure_DeletesTheWrittenFileAndLeavesNoRow()
    {
        var before = await TakeSnapshotAsync();
        var interceptor = new ThrowingSaveChangesInterceptor { Armed = true };
        await using var context = _fixture.CreateContext(interceptor);
        var storage = new RecordingFileStorageService(TestServiceFactory.CreateFileStorage(_fixture));
        var service = TestServiceFactory.CreateDocumentService(context, storage);

        var result = await service.UploadDocumentAsync(
            Request("save-failure.pdf"), new MemoryStream(DocumentUploadTests.PdfBytes(1024)), NiKang);

        Assert.False(result.Succeeded);
        Assert.Equal(DocumentErrorCode.StorageFailed, result.Error);

        // The file was written (step 5) and then compensated (deleted).
        var key = Assert.Single(storage.Uploads);
        Assert.Contains(key, storage.Deletes);
        Assert.False(File.Exists(Path.Combine(_fixture.StorageRoot, key.Replace('/', Path.DirectorySeparatorChar))));

        interceptor.Armed = false;
        await AssertNothingLeftBehindAsync(before);
    }

    /// <summary>Probes the database right after the real upload finishes, to see whether the row is already visible.</summary>
    private sealed class ProbingStorage : IFileStorageService
    {
        private readonly IFileStorageService _inner;
        private readonly Func<string, Task> _afterUpload;

        public ProbingStorage(IFileStorageService inner, Func<string, Task> afterUpload)
        {
            _inner = inner;
            _afterUpload = afterUpload;
        }

        public async Task<string> UploadAsync(Stream fileStream, string storageKey, string contentType)
        {
            var key = await _inner.UploadAsync(fileStream, storageKey, contentType);
            await _afterUpload(key);
            return key;
        }

        public Task DeleteAsync(string storageKey) => _inner.DeleteAsync(storageKey);
        public Task<Stream> DownloadAsync(string storageKey) => _inner.DownloadAsync(storageKey);
        public Task<string> GetUrlAsync(string storageKey, TimeSpan expiration) => _inner.GetUrlAsync(storageKey, expiration);
    }

    [Fact]
    [Trait("Story", "US1")]
    public async Task Upload_Success_FileIsWrittenBeforeTheRowBecomesVisible()
    {
        string? uploadedKey = null;
        var fileExistedAfterUpload = false;
        var rowVisibleAfterUpload = true;

        var inner = TestServiceFactory.CreateFileStorage(_fixture);
        var storage = new ProbingStorage(inner, async key =>
        {
            uploadedKey = key;
            fileExistedAfterUpload = File.Exists(Path.Combine(_fixture.StorageRoot, key.Replace('/', Path.DirectorySeparatorChar)));
            await using var probe = _fixture.CreateContext();
            rowVisibleAfterUpload = await probe.Documents.AnyAsync(d => d.FilePath == key);
        });

        await using var context = _fixture.CreateContext();
        var service = TestServiceFactory.CreateDocumentService(context, storage);

        var result = await service.UploadDocumentAsync(
            Request("ordering.pdf"), new MemoryStream(DocumentUploadTests.PdfBytes(1024)), NiKang);

        Assert.True(result.Succeeded, result.Message);
        Assert.NotNull(uploadedKey);
        Assert.True(fileExistedAfterUpload);
        Assert.False(rowVisibleAfterUpload, "The Documents row must not exist before UploadAsync has completed.");

        await using var verify = _fixture.CreateContext();
        Assert.True(await verify.Documents.AnyAsync(d => d.FilePath == uploadedKey));
    }
}
