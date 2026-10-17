using System.Diagnostics;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ContosoDashboard.Tests;

/// <summary>
/// SC-001 timing test (T134): a 25 MB upload completes in 30 seconds or less on localhost. It measures validation,
/// scanning, storage and the save; the browser-to-server transfer is checked by hand in quickstart.md section 1.
/// Uses its own fixture, so it gets a separate database and storage root.
/// </summary>
[Trait("Story", "US1")]
public class DocumentUploadTimingTests : IClassFixture<LocalDbFixture>
{
    private const int NiKang = 4;
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    private readonly LocalDbFixture _fixture;

    public DocumentUploadTimingTests(LocalDbFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Upload_TwentyFiveMegabytes_CompletesWithinThirtySeconds()
    {
        await using var context = _fixture.CreateContext();
        var service = TestServiceFactory.CreateDocumentService(
            context, TestServiceFactory.CreateFileStorage(_fixture), TestServiceFactory.CreateScanner());

        // Warm-up: one small upload, not timed.
        var warmUp = await service.UploadDocumentAsync(
            new DocumentUploadRequest("warm-up.pdf", "Warm-up", "Reports", null, null, Array.Empty<string>()),
            new MemoryStream(DocumentUploadTests.PdfBytes(1024)), NiKang);
        Assert.True(warmUp.Succeeded, warmUp.Message);

        // Arrange: exactly 26,214,400 bytes, a %PDF-1.4 header followed by filler (no EICAR content).
        var content = new MemoryStream(DocumentUploadTests.PdfBytes(26_214_400));

        var stopwatch = Stopwatch.StartNew();
        var result = await service.UploadDocumentAsync(
            new DocumentUploadRequest("big.pdf", "Big upload", "Reports", null, null, Array.Empty<string>()),
            content, NiKang);
        stopwatch.Stop();

        Assert.True(result.Succeeded, result.Message);
        Assert.True(stopwatch.Elapsed <= Limit, $"A 25 MB upload took {stopwatch.Elapsed.TotalSeconds:F1} s (limit 30 s).");
    }
}
