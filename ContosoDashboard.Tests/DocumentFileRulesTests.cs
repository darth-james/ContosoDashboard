using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Xunit;

namespace ContosoDashboard.Tests;

/// <summary>
/// Tests for DocumentFileRules (T028, research R-11).
/// New tests that call stubbed members fail at runtime (red) until T033 replaces the stubs.
/// </summary>
[Trait("Story", "Foundation")]
public class DocumentFileRulesTests
{
    // ---- Size ----

    [Fact]
    public void MaxFileSizeBytes_Is25Megabytes()
    {
        Assert.Equal(26_214_400L, DocumentFileRules.MaxFileSizeBytes);
        Assert.Equal(25L * 1024 * 1024, DocumentFileRules.MaxFileSizeBytes);
    }

    [Fact]
    public void ValidateFile_AcceptsExactlyTheMaximumSize()
    {
        Assert.Equal(DocumentErrorCode.None, DocumentFileRules.ValidateFile("report.pdf", 26_214_400));
    }

    [Fact]
    public void ValidateFile_RejectsOneByteOverTheMaximum()
    {
        Assert.Equal(DocumentErrorCode.FileTooLarge, DocumentFileRules.ValidateFile("report.pdf", 26_214_401));
    }

    [Fact]
    public void ValidateFile_RejectsAnEmptyFile()
    {
        Assert.Equal(DocumentErrorCode.FileEmpty, DocumentFileRules.ValidateFile("report.pdf", 0));
    }

    [Fact]
    public void ValidateFile_AcceptsOneByte()
    {
        Assert.Equal(DocumentErrorCode.None, DocumentFileRules.ValidateFile("notes.txt", 1));
    }

    // ---- Extensions ----

    [Fact]
    public void Extension_MatchingIsCaseInsensitive()
    {
        Assert.Equal(DocumentErrorCode.None, DocumentFileRules.ValidateFile("REPORT.PDF", 100));

        Assert.True(DocumentFileRules.TryGetFileType("REPORT.PDF", out var extension, out var mime));
        Assert.Equal(".pdf", extension);
        Assert.Equal("application/pdf", mime);
    }

    [Theory]
    [InlineData("report.pdf.exe")]
    [InlineData("bundle.zip")]
    [InlineData("tool.exe")]
    [InlineData("noextension")]
    public void UnsupportedNamesAreRejected(string fileName)
    {
        Assert.Equal(DocumentErrorCode.UnsupportedFileType, DocumentFileRules.ValidateFile(fileName, 100));
        Assert.False(DocumentFileRules.TryGetFileType(fileName, out _, out _));
    }

    // ---- MIME ----

    [Theory]
    [InlineData("a.pdf", ".pdf", "application/pdf")]
    [InlineData("a.doc", ".doc", "application/msword")]
    [InlineData("a.docx", ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("a.xls", ".xls", "application/vnd.ms-excel")]
    [InlineData("a.xlsx", ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("a.ppt", ".ppt", "application/vnd.ms-powerpoint")]
    [InlineData("a.pptx", ".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    [InlineData("a.txt", ".txt", "text/plain")]
    [InlineData("a.jpg", ".jpg", "image/jpeg")]
    [InlineData("a.jpeg", ".jpeg", "image/jpeg")]
    [InlineData("a.png", ".png", "image/png")]
    public void EveryAllowListedExtension_MapsToItsCanonicalMime(string fileName, string expectedExtension, string expectedMime)
    {
        Assert.True(DocumentFileRules.TryGetFileType(fileName, out var extension, out var mime));
        Assert.Equal(expectedExtension, extension);
        Assert.Equal(expectedMime, mime);
    }

    [Theory]
    [InlineData("application/pdf", true)]
    [InlineData("image/jpeg", true)]
    [InlineData("image/png", true)]
    [InlineData("application/msword", false)]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", false)]
    [InlineData("application/vnd.ms-excel", false)]
    [InlineData("application/vnd.ms-powerpoint", false)]
    [InlineData("text/plain", false)]
    public void OnlyPdfJpegAndPngArePreviewable(string mime, bool expected)
    {
        Assert.Equal(expected, DocumentFileRules.IsPreviewable(mime));
    }

    // ---- SanitizeFileName ----

    [Fact]
    public void SanitizeFileName_DropsPathSegments()
    {
        Assert.Equal("evil.pdf", DocumentFileRules.SanitizeFileName("..\\..\\evil.pdf"));
        Assert.Equal("evil.pdf", DocumentFileRules.SanitizeFileName("../../evil.pdf"));
    }

    [Fact]
    public void SanitizeFileName_ReplacesInvalidAndControlCharactersWithUnderscore()
    {
        Assert.Equal("a_b_c_d_e.pdf", DocumentFileRules.SanitizeFileName("a<b>c|d\u0001e.pdf"));
    }

    [Fact]
    public void SanitizeFileName_CollapsesWhitespaceAndTrims()
    {
        Assert.Equal("my report.pdf", DocumentFileRules.SanitizeFileName("  my   report.pdf  "));
    }

    [Fact]
    public void SanitizeFileName_TruncatesTo255CharactersKeepingTheExtension()
    {
        var sanitized = DocumentFileRules.SanitizeFileName(new string('a', 300) + ".pdf");

        Assert.Equal(255, sanitized.Length);
        Assert.EndsWith(".pdf", sanitized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:\\folder\\")]
    [InlineData(null)]
    public void SanitizeFileName_FallsBackToDocument(string? fileName)
    {
        Assert.Equal("document", DocumentFileRules.SanitizeFileName(fileName));
    }

    // ---- Metadata ----

    [Fact]
    public void ValidateMetadata_AcceptsValidInput()
    {
        Assert.Null(DocumentFileRules.ValidateMetadata("Quarterly report", "Reports", "A description"));
        Assert.Null(DocumentFileRules.ValidateMetadata("Quarterly report", "Reports", null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateMetadata_RequiresATitle(string? title)
    {
        Assert.Equal(DocumentMessages.TitleInvalid, DocumentFileRules.ValidateMetadata(title, "Reports", null));
    }

    [Fact]
    public void ValidateMetadata_TitleIsLimitedTo200CharactersAfterTrimming()
    {
        Assert.Null(DocumentFileRules.ValidateMetadata(new string('t', 200), "Reports", null));
        Assert.Null(DocumentFileRules.ValidateMetadata("  " + new string('t', 200) + "  ", "Reports", null));
        Assert.Equal(DocumentMessages.TitleInvalid, DocumentFileRules.ValidateMetadata(new string('t', 201), "Reports", null));
    }

    [Fact]
    public void ValidateMetadata_DescriptionIsLimitedTo2000Characters()
    {
        Assert.Null(DocumentFileRules.ValidateMetadata("Title", "Reports", new string('d', 2000)));
        Assert.Equal(DocumentMessages.DescriptionTooLong, DocumentFileRules.ValidateMetadata("Title", "Reports", new string('d', 2001)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("reports")]
    [InlineData("Unknown")]
    public void ValidateMetadata_CategoryMustBeInTheFixedList(string? category)
    {
        Assert.Equal(DocumentMessages.CategoryInvalid, DocumentFileRules.ValidateMetadata("Title", category, null));
    }

    [Fact]
    public void ValidateMetadata_AcceptsEverySixCategory()
    {
        Assert.Equal(6, DocumentCategories.All.Count);
        Assert.All(DocumentCategories.All, category => Assert.Null(DocumentFileRules.ValidateMetadata("Title", category, null)));
    }

    [Fact]
    public void NormalizeTags_TrimsDropsEmptyAndDeduplicatesIgnoringCase()
    {
        var (tags, error) = DocumentFileRules.NormalizeTags(new[] { " alpha ", "", "   ", "ALPHA", "Beta", "beta ", "gamma" });

        Assert.Null(error);
        Assert.Equal(new[] { "alpha", "Beta", "gamma" }, tags);
    }

    [Fact]
    public void NormalizeTags_AllowsAtMostTenTags()
    {
        var ten = Enumerable.Range(1, 10).Select(i => $"tag{i}").ToList();
        var eleven = Enumerable.Range(1, 11).Select(i => $"tag{i}").ToList();

        var (okTags, okError) = DocumentFileRules.NormalizeTags(ten);
        var (_, tooManyError) = DocumentFileRules.NormalizeTags(eleven);

        Assert.Null(okError);
        Assert.Equal(10, okTags.Count);
        Assert.Equal(DocumentMessages.TooManyTags, tooManyError);
    }

    [Fact]
    public void NormalizeTags_EachTagIsLimitedTo50Characters()
    {
        var (okTags, okError) = DocumentFileRules.NormalizeTags(new[] { new string('x', 50) });
        var (_, tooLongError) = DocumentFileRules.NormalizeTags(new[] { new string('x', 51) });

        Assert.Null(okError);
        Assert.Single(okTags);
        Assert.Equal(DocumentMessages.TagTooLong, tooLongError);
    }

    [Fact]
    public void NormalizeTags_AcceptsNoTags()
    {
        var (tags, error) = DocumentFileRules.NormalizeTags(Array.Empty<string>());

        Assert.Null(error);
        Assert.Empty(tags);
    }

    // ---- Display names ----

    [Theory]
    [InlineData("application/pdf", "PDF")]
    [InlineData("application/msword", "Word")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "Word")]
    [InlineData("application/vnd.ms-excel", "Excel")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Excel")]
    [InlineData("application/vnd.ms-powerpoint", "PowerPoint")]
    [InlineData("application/vnd.openxmlformats-officedocument.presentationml.presentation", "PowerPoint")]
    [InlineData("text/plain", "Text")]
    [InlineData("image/jpeg", "JPEG")]
    [InlineData("image/png", "PNG")]
    public void GetDisplayName_ReturnsTheFriendlyTypeName(string mime, string expected)
    {
        Assert.Equal(expected, DocumentFileRules.GetDisplayName(mime));
    }
}
