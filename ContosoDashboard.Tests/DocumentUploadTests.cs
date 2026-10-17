using System.Text.RegularExpressions;
using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ContosoDashboard.Tests;

/// <summary>
/// User Story 1 upload and download tests (T042) against <see cref="IDocumentService"/>: stored metadata, the Upload and
/// Download activity rows, validation errors, tag normalization, the 25 MB boundary, and the byte-identical download.
/// </summary>
[Collection("LocalDb")]
[Trait("Story", "US1")]
public class DocumentUploadTests
{
    private const int NiKang = 4;   // Employee, Engineering, member of Project 1

    private readonly LocalDbFixture _fixture;

    public DocumentUploadTests(LocalDbFixture fixture)
    {
        _fixture = fixture;
    }

    internal static byte[] PdfBytes(int length)
    {
        var header = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n");
        var bytes = new byte[length];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = i < header.Length ? header[i] : (byte)('a' + (i % 26));
        }

        return bytes;
    }

    private IDocumentService CreateService(ApplicationDbContext context)
        => TestServiceFactory.CreateDocumentService(context, TestServiceFactory.CreateFileStorage(_fixture));

    private static DocumentUploadRequest Request(
        string title, string fileName = "q3-budget.pdf", string category = "Reports", string? description = null,
        IReadOnlyList<string>? tags = null, int? projectId = null)
        => new(fileName, title, category, description, projectId, tags ?? Array.Empty<string>());

    private static string UniqueTitle(string prefix) => $"{prefix} {Guid.NewGuid():N}";

    [Fact]
    public async Task Upload_PersonalPdf_StoresMetadataFromServerNotClient()
    {
        var bytes = PdfBytes(2 * 1024 * 1024);
        var title = UniqueTitle("Q3 Budget Notes");
        var before = DateTime.UtcNow.AddSeconds(-1);

        int documentId;
        await using (var context = _fixture.CreateContext())
        {
            var service = CreateService(context);
            var result = await service.UploadDocumentAsync(
                Request($"  {title}  ", "  Q3 Budget\\..\\Notes.PDF ", "Reports", "Quarterly numbers",
                    new[] { "budget", "q3" }),
                new MemoryStream(bytes), NiKang);

            Assert.True(result.Succeeded, result.Message);
            Assert.Equal(DocumentErrorCode.None, result.Error);
            Assert.NotNull(result.DocumentId);
            documentId = result.DocumentId!.Value;
        }

        await using var verify = _fixture.CreateContext();
        var document = await verify.Documents.Include(d => d.Tags).SingleAsync(d => d.DocumentId == documentId);

        Assert.Equal(title, document.Title);                       // trimmed
        Assert.Equal("Reports", document.Category);
        Assert.Equal("Quarterly numbers", document.Description);
        Assert.Equal(NiKang, document.UploadedByUserId);
        Assert.Null(document.ProjectId);
        Assert.InRange(document.UploadedDate, before, DateTime.UtcNow.AddSeconds(1));
        Assert.InRange(document.LastModifiedDate, before, DateTime.UtcNow.AddSeconds(1));
        Assert.Equal(bytes.Length, document.FileSizeBytes);        // the stream length
        Assert.Equal("application/pdf", document.FileType);        // canonical MIME from the extension
        Assert.Equal("Notes.PDF", document.OriginalFileName);      // sanitized: path segments dropped, trimmed
        Assert.Matches(new Regex(@"^4/personal/[0-9a-f]{32}\.pdf$"), document.FilePath);
        Assert.DoesNotContain("Notes", document.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "budget", "q3" }, document.Tags.Select(t => t.Tag).OrderBy(t => t).ToArray());
    }

    [Fact]
    public async Task Upload_WritesExactlyOneUploadActivityWithSnapshots()
    {
        var title = UniqueTitle("Activity check");
        int documentId;
        await using (var context = _fixture.CreateContext())
        {
            var result = await CreateService(context).UploadDocumentAsync(
                Request(title), new MemoryStream(PdfBytes(1024)), NiKang);
            Assert.True(result.Succeeded, result.Message);
            documentId = result.DocumentId!.Value;
        }

        await using var verify = _fixture.CreateContext();
        var activities = await verify.DocumentActivities.Where(a => a.DocumentId == documentId).ToListAsync();

        var activity = Assert.Single(activities);
        Assert.Equal(DocumentActivityAction.Upload, activity.Action);
        Assert.Equal(NiKang, activity.UserId);
        Assert.Equal(title, activity.DocumentTitle);
        Assert.Equal("application/pdf", activity.FileType);
        Assert.Null(activity.TaskId);
    }

    public static IEnumerable<object[]> InvalidMetadata()
    {
        yield return new object[] { "missing title", null!, "Reports", null!, Array.Empty<string>() };
        yield return new object[] { "whitespace title", "   ", "Reports", null!, Array.Empty<string>() };
        yield return new object[] { "201-char title", new string('t', 201), "Reports", null!, Array.Empty<string>() };
        yield return new object[] { "unknown category", "Valid title", "Secrets", null!, Array.Empty<string>() };
        yield return new object[] { "2001-char description", "Valid title", "Reports", new string('d', 2001), Array.Empty<string>() };
        yield return new object[]
        {
            "11 tags", "Valid title", "Reports", null!, Enumerable.Range(1, 11).Select(i => $"tag{i}").ToArray()
        };
        yield return new object[] { "51-char tag", "Valid title", "Reports", null!, new[] { new string('x', 51) } };
    }

    [Theory]
    [MemberData(nameof(InvalidMetadata))]
    public async Task Upload_InvalidMetadata_ReturnsValidationAndStoresNothing(
        string reason, string? title, string category, string? description, string[] tags)
    {
        _ = reason;
        await using var context = _fixture.CreateContext();
        var service = CreateService(context);
        var documentsBefore = await context.Documents.CountAsync();

        var result = await service.UploadDocumentAsync(
            new DocumentUploadRequest("notes.pdf", title!, category, description, null, tags),
            new MemoryStream(PdfBytes(1024)), NiKang);

        Assert.False(result.Succeeded);
        Assert.Equal(DocumentErrorCode.Validation, result.Error);
        Assert.Equal(documentsBefore, await context.Documents.CountAsync());
    }

    [Fact]
    public async Task Upload_DuplicateAndBlankTags_StoredAsOneTag()
    {
        int documentId;
        await using (var context = _fixture.CreateContext())
        {
            var result = await CreateService(context).UploadDocumentAsync(
                Request(UniqueTitle("Tags"), tags: new[] { "Onboarding", "onboarding ", "  " }),
                new MemoryStream(PdfBytes(1024)), NiKang);
            Assert.True(result.Succeeded, result.Message);
            documentId = result.DocumentId!.Value;
        }

        await using var verify = _fixture.CreateContext();
        var tags = await verify.DocumentTags.Where(t => t.DocumentId == documentId).Select(t => t.Tag).ToListAsync();
        Assert.Equal(new[] { "Onboarding" }, tags);
    }

    [Fact]
    public async Task Upload_ExactlyTwentyFiveMegabytes_IsAccepted()
    {
        await using var context = _fixture.CreateContext();
        var result = await CreateService(context).UploadDocumentAsync(
            Request(UniqueTitle("exact-25mb"), "exact-25mb.pdf"),
            new MemoryStream(PdfBytes(26_214_400)), NiKang);

        Assert.True(result.Succeeded, result.Message);
        var document = await context.Documents.AsNoTracking().SingleAsync(d => d.DocumentId == result.DocumentId);
        Assert.Equal(26_214_400, document.FileSizeBytes);
    }

    [Fact]
    public async Task Download_ByOwner_ReturnsIdenticalBytesAndWritesOneDownloadActivity()
    {
        var bytes = PdfBytes(64 * 1024);
        int documentId;
        await using (var context = _fixture.CreateContext())
        {
            var result = await CreateService(context).UploadDocumentAsync(
                Request(UniqueTitle("Download"), "Original Name.pdf"), new MemoryStream(bytes), NiKang);
            Assert.True(result.Succeeded, result.Message);
            documentId = result.DocumentId!.Value;
        }

        await using var downloadContext = _fixture.CreateContext();
        var content = await CreateService(downloadContext).OpenDocumentContentAsync(
            documentId, NiKang, DocumentContentMode.Download);

        Assert.True(content.Succeeded);
        Assert.Equal(DocumentErrorCode.None, content.Error);
        Assert.Equal("Original Name.pdf", content.DownloadFileName);
        Assert.False(content.Inline);
        Assert.Equal("application/pdf", content.ContentType);
        Assert.NotNull(content.Content);

        await using (content.Content)
        {
            using var copy = new MemoryStream();
            await content.Content!.CopyToAsync(copy);
            Assert.Equal(bytes, copy.ToArray());
        }

        await using var verify = _fixture.CreateContext();
        var downloads = await verify.DocumentActivities
            .Where(a => a.DocumentId == documentId && a.Action == DocumentActivityAction.Download)
            .ToListAsync();
        var activity = Assert.Single(downloads);
        Assert.Equal(NiKang, activity.UserId);
    }
}
