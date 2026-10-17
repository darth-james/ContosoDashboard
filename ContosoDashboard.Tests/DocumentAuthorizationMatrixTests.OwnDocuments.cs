using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ContosoDashboard.Tests;

// User Story 1 part of the authorization matrix (T044): denial cases for a personal document owned by Ni Kang,
// the "Access a document (Employee)" and "Upload a document to a project (Employee)" cells.
public partial class DocumentAuthorizationMatrixTests
{
    private IDocumentService CreateDocumentService(ApplicationDbContext context)
        => TestServiceFactory.CreateDocumentService(context, TestServiceFactory.CreateFileStorage(Fixture));

    private async Task<Document> AddNiKangPersonalDocumentAsync(string? title = null)
    {
        return await Data.AddDocumentAsync(
            NiKang, null, title ?? $"Personal {Guid.NewGuid():N}", "Personal Files", bytes: new byte[] { 1, 2, 3, 4 });
    }

    [Theory]
    [InlineData(Dana)]
    [InlineData(Mateo)]
    [InlineData(Hana)]
    [InlineData(Rafael)]
    [Trait("Story", "US1")]
    public async Task GetDocument_PersonalDocumentOfAnotherUser_ReturnsNull(int requester)
    {
        var document = await AddNiKangPersonalDocumentAsync();

        await using var context = Fixture.CreateContext();
        var details = await CreateDocumentService(context).GetDocumentAsync(document.DocumentId, requester);

        Assert.Null(details);
    }

    [Theory]
    [InlineData(Dana)]
    [InlineData(Mateo)]
    [InlineData(Hana)]
    [InlineData(Rafael)]
    [Trait("Story", "US1")]
    public async Task Download_PersonalDocumentOfAnotherUser_IsIndistinguishableFromANonexistentDocument(int requester)
    {
        var document = await AddNiKangPersonalDocumentAsync();

        await using var context = Fixture.CreateContext();
        var service = CreateDocumentService(context);

        var denied = await service.OpenDocumentContentAsync(document.DocumentId, requester, DocumentContentMode.Download);
        var missing = await service.OpenDocumentContentAsync(999999, requester, DocumentContentMode.Download);

        Assert.False(denied.Succeeded);
        Assert.Equal(DocumentErrorCode.NotFound, denied.Error);
        Assert.Null(denied.Content);
        Assert.Equal(missing, denied);   // same result shape, no field reveals the document exists

        await using var verify = Fixture.CreateContext();
        Assert.False(await verify.DocumentActivities.AnyAsync(
            a => a.DocumentId == document.DocumentId && a.UserId == requester));
    }

    [Fact]
    [Trait("Story", "US1")]
    public async Task GetMyDocuments_NeverContainsDocumentsUploadedByOthers()
    {
        var others = await AddNiKangPersonalDocumentAsync();
        var own = await Data.AddDocumentAsync(Dana, null, $"Dana own {Guid.NewGuid():N}", "Personal Files");

        await using var context = Fixture.CreateContext();
        var page = await CreateDocumentService(context).GetMyDocumentsAsync(Dana, new DocumentQuery { PageSize = 100 });

        Assert.All(page.Items, item => Assert.Equal(Dana, item.UploadedByUserId));
        Assert.Contains(page.Items, item => item.DocumentId == own.DocumentId);
        Assert.DoesNotContain(page.Items, item => item.DocumentId == others.DocumentId);
    }

    [Fact]
    [Trait("Story", "US1")]
    public async Task Upload_ToAProjectTheEmployeeIsNotAMemberOf_IsForbiddenAndStoresNothing()
    {
        await using var before = Fixture.CreateContext();
        var documentsBefore = await before.Documents.CountAsync();
        var filesBefore = FileCount();

        await using var context = Fixture.CreateContext();
        var result = await CreateDocumentService(context).UploadDocumentAsync(
            new DocumentUploadRequest("plan.pdf", $"Not allowed {Guid.NewGuid():N}", "Reports", null, Project2,
                Array.Empty<string>()),
            new MemoryStream(DocumentUploadTests.PdfBytes(1024)), NiKang);

        Assert.False(result.Succeeded);
        Assert.Equal(DocumentErrorCode.Forbidden, result.Error);

        await using var after = Fixture.CreateContext();
        Assert.Equal(documentsBefore, await after.Documents.CountAsync());
        Assert.Equal(filesBefore, FileCount());
    }

    [Fact]
    [Trait("Story", "US1")]
    public async Task Upload_ByAnUnknownUser_IsDeniedAndStoresNothing()
    {
        await using var before = Fixture.CreateContext();
        var documentsBefore = await before.Documents.CountAsync();
        var filesBefore = FileCount();

        await using var context = Fixture.CreateContext();
        var result = await CreateDocumentService(context).UploadDocumentAsync(
            new DocumentUploadRequest("plan.pdf", $"Unknown user {Guid.NewGuid():N}", "Reports", null, null,
                Array.Empty<string>()),
            new MemoryStream(DocumentUploadTests.PdfBytes(1024)), 987654);

        Assert.False(result.Succeeded);
        Assert.True(result.Error is DocumentErrorCode.NotFound or DocumentErrorCode.Forbidden);

        await using var after = Fixture.CreateContext();
        Assert.Equal(documentsBefore, await after.Documents.CountAsync());
        Assert.Equal(filesBefore, FileCount());
    }

    [Fact]
    [Trait("Story", "US1")]
    public async Task GetUploadableProjects_ReturnsOnlyProjectsTheUserMayUploadTo()
    {
        await using var context = Fixture.CreateContext();
        var service = CreateDocumentService(context);

        var niKang = await service.GetUploadableProjectsAsync(NiKang);
        var mateo = await service.GetUploadableProjectsAsync(Mateo);
        var admin = await service.GetUploadableProjectsAsync(Admin);
        var dana = await service.GetUploadableProjectsAsync(Dana);

        Assert.Equal(new[] { Project1 }, niKang.Select(p => p.ProjectId).ToArray());
        Assert.Equal(new[] { Project2 }, mateo.Select(p => p.ProjectId).ToArray());
        Assert.Contains(admin, p => p.ProjectId == Project1);
        Assert.Contains(admin, p => p.ProjectId == Project2);
        Assert.Empty(dana);
        Assert.Equal(admin.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => p.ProjectId),
            admin.Select(p => p.ProjectId));   // ordered by name
    }

    [Fact]
    [Trait("Story", "US1")]
    public async Task GetDocument_ByTheOwner_GrantsEveryOwnerCapability()
    {
        var document = await AddNiKangPersonalDocumentAsync();

        await using var context = Fixture.CreateContext();
        var details = await CreateDocumentService(context).GetDocumentAsync(document.DocumentId, NiKang);

        Assert.NotNull(details);
        Assert.True(details!.CanEditMetadata);
        Assert.True(details.CanDelete);
        Assert.True(details.CanReplaceFile);
        Assert.True(details.CanShare);
        Assert.Equal(document.DocumentId, details.Summary.DocumentId);
    }

    private int FileCount()
    {
        return Directory.Exists(Fixture.StorageRoot)
            ? Directory.GetFiles(Fixture.StorageRoot, "*", SearchOption.AllDirectories).Length
            : 0;
    }
}
