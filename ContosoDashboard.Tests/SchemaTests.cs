using ContosoDashboard.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ContosoDashboard.Tests;

/// <summary>
/// Schema tests (T025). These exercise real code from T014-T015, so they may pass before the 2d tasks.
/// </summary>
[Collection("LocalDb")]
[Trait("Story", "Foundation")]
public class SchemaTests
{
    private readonly LocalDbFixture _fixture;
    private readonly DocumentTestData _data;

    public SchemaTests(LocalDbFixture fixture)
    {
        _fixture = fixture;
        _data = new DocumentTestData(fixture);
    }

    [Fact]
    public async Task AllFiveTablesExist()
    {
        await using var context = _fixture.CreateContext();

        var count = await context.Database
            .SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name IN ('Documents','DocumentTags','DocumentShares','TaskDocuments','DocumentActivities')")
            .SingleAsync();

        Assert.Equal(5, count);
    }

    [Fact]
    public async Task NotificationsDocumentIdColumnExists()
    {
        await using var context = _fixture.CreateContext();

        var count = await context.Database
            .SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id = OBJECT_ID('Notifications') AND name = 'DocumentId'")
            .SingleAsync();

        Assert.Equal(1, count);
        // The column is also queryable through the model.
        _ = await context.Notifications.Select(n => n.DocumentId).AnyAsync();
    }

    [Fact]
    public async Task SeedUsers5To8HaveTheResearchR14Values()
    {
        await using var context = _fixture.CreateContext();
        var users = await context.Users.Where(u => u.UserId >= 5 && u.UserId <= 8).OrderBy(u => u.UserId).ToListAsync();

        Assert.Equal(4, users.Count);
        AssertUser(users[0], 5, "Dana Whitfield", "dana.whitfield@contoso.com", UserRole.Employee, "Engineering");
        AssertUser(users[1], 6, "Mateo Alvarez", "mateo.alvarez@contoso.com", UserRole.Employee, "Marketing");
        AssertUser(users[2], 7, "Hana Sato", "hana.sato@contoso.com", UserRole.TeamLead, "Marketing");
        AssertUser(users[3], 8, "Rafael Costa", "rafael.costa@contoso.com", UserRole.ProjectManager, "Marketing");
        Assert.All(users, u => Assert.True(u.InAppNotificationsEnabled));
    }

    [Fact]
    public async Task SeedProject2ProjectMember3AndTasks4To5HaveTheResearchR14Values()
    {
        await using var context = _fixture.CreateContext();

        var project = await context.Projects.SingleAsync(p => p.ProjectId == 2);
        Assert.Equal("Marketing Website Refresh", project.Name);
        Assert.Equal(8, project.ProjectManagerId);
        Assert.Equal(ProjectStatus.Active, project.Status);

        var member = await context.ProjectMembers.SingleAsync(m => m.ProjectMemberId == 3);
        Assert.Equal(2, member.ProjectId);
        Assert.Equal(6, member.UserId);
        Assert.Equal("Developer", member.Role);

        var task4 = await context.Tasks.SingleAsync(t => t.TaskId == 4);
        Assert.Equal("Prepare onboarding checklist", task4.Title);
        Assert.Null(task4.ProjectId);
        Assert.Equal(4, task4.AssignedUserId);
        Assert.Equal(3, task4.CreatedByUserId);

        var task5 = await context.Tasks.SingleAsync(t => t.TaskId == 5);
        Assert.Equal("Draft launch copy", task5.Title);
        Assert.Equal(2, task5.ProjectId);
        Assert.Equal(6, task5.AssignedUserId);
        Assert.Equal(8, task5.CreatedByUserId);
    }

    [Fact]
    public async Task OriginalSeedUsers1To4AreUnchanged()
    {
        await using var context = _fixture.CreateContext();
        var users = await context.Users.Where(u => u.UserId <= 4).OrderBy(u => u.UserId).ToListAsync();

        Assert.Equal(4, users.Count);
        AssertUser(users[0], 1, "System Administrator", "admin@contoso.com", UserRole.Administrator, "IT");
        AssertUser(users[1], 2, "Camille Nicole", "camille.nicole@contoso.com", UserRole.ProjectManager, "Engineering");
        AssertUser(users[2], 3, "Floris Kregel", "floris.kregel@contoso.com", UserRole.TeamLead, "Engineering");
        AssertUser(users[3], 4, "Ni Kang", "ni.kang@contoso.com", UserRole.Employee, "Engineering");
    }

    [Fact]
    public async Task ShareWithBothTargetsNullIsRejectedByTheCheckConstraint()
    {
        var document = await _data.AddDocumentAsync(4, null, $"Schema both-null {Guid.NewGuid():N}", "Other");

        await using var context = _fixture.CreateContext();
        context.DocumentShares.Add(new DocumentShare { DocumentId = document.DocumentId, SharedByUserId = 4 });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task ShareWithBothTargetsSetIsRejectedByTheCheckConstraint()
    {
        var document = await _data.AddDocumentAsync(4, null, $"Schema both-set {Guid.NewGuid():N}", "Other");

        await using var context = _fixture.CreateContext();
        context.DocumentShares.Add(new DocumentShare
        {
            DocumentId = document.DocumentId,
            SharedByUserId = 4,
            SharedWithUserId = 5,
            SharedWithDepartment = "Marketing"
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task DuplicateUserShareIsRejectedByTheUniqueFilteredIndex()
    {
        var document = await _data.AddDocumentAsync(4, null, $"Schema dup-user {Guid.NewGuid():N}", "Other");
        await _data.AddUserShareAsync(document.DocumentId, 4, 5);

        await Assert.ThrowsAsync<DbUpdateException>(() => _data.AddUserShareAsync(document.DocumentId, 4, 5));
    }

    [Fact]
    public async Task DuplicateDepartmentShareIsRejectedByTheUniqueFilteredIndex()
    {
        var document = await _data.AddDocumentAsync(4, null, $"Schema dup-dept {Guid.NewGuid():N}", "Other");
        await _data.AddDepartmentShareAsync(document.DocumentId, 4, "Marketing");

        await Assert.ThrowsAsync<DbUpdateException>(() => _data.AddDepartmentShareAsync(document.DocumentId, 4, "Marketing"));
    }

    [Fact]
    public async Task UserShareAndDepartmentShareOnTheSameDocumentAreBothAllowed()
    {
        var document = await _data.AddDocumentAsync(4, null, $"Schema both-kinds {Guid.NewGuid():N}", "Other");

        await _data.AddUserShareAsync(document.DocumentId, 4, 5);
        await _data.AddDepartmentShareAsync(document.DocumentId, 4, "Marketing");

        await using var context = _fixture.CreateContext();
        Assert.Equal(2, await context.DocumentShares.CountAsync(s => s.DocumentId == document.DocumentId));
    }

    [Fact]
    public async Task DeletingADocumentCascadesToTagsSharesAndAttachmentsButKeepsActivityRows()
    {
        var document = await _data.AddDocumentAsync(
            4, null, $"Schema cascade {Guid.NewGuid():N}", "Reports", new[] { "alpha", "beta" });
        await _data.AddUserShareAsync(document.DocumentId, 4, 5);
        await _data.AddDepartmentShareAsync(document.DocumentId, 4, "Marketing");
        await _data.AddTaskAttachmentAsync(4, document.DocumentId, 4);

        await using (var arrange = _fixture.CreateContext())
        {
            arrange.DocumentActivities.Add(new DocumentActivity
            {
                DocumentId = document.DocumentId,
                DocumentTitle = document.Title,
                FileType = document.FileType,
                UserId = 4,
                Action = DocumentActivityAction.Upload
            });
            await arrange.SaveChangesAsync();
        }

        await using (var act = _fixture.CreateContext())
        {
            var toDelete = await act.Documents.SingleAsync(d => d.DocumentId == document.DocumentId);
            act.Documents.Remove(toDelete);
            await act.SaveChangesAsync();
        }

        await using var assert = _fixture.CreateContext();
        Assert.False(await assert.Documents.AnyAsync(d => d.DocumentId == document.DocumentId));
        Assert.False(await assert.DocumentTags.AnyAsync(t => t.DocumentId == document.DocumentId));
        Assert.False(await assert.DocumentShares.AnyAsync(s => s.DocumentId == document.DocumentId));
        Assert.False(await assert.TaskDocuments.AnyAsync(a => a.DocumentId == document.DocumentId));
        Assert.Equal(1, await assert.DocumentActivities.CountAsync(a => a.DocumentId == document.DocumentId));
    }

    [Fact]
    public async Task DuplicateFilePathIsRejectedByTheUniqueIndex()
    {
        var first = await _data.AddDocumentAsync(4, null, $"Schema path-1 {Guid.NewGuid():N}", "Other");

        await using var context = _fixture.CreateContext();
        context.Documents.Add(new Document
        {
            Title = $"Schema path-2 {Guid.NewGuid():N}",
            Category = "Other",
            UploadedByUserId = 4,
            OriginalFileName = "dup.pdf",
            FileSizeBytes = 10,
            FileType = "application/pdf",
            FilePath = first.FilePath
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task AttachingTheSameDocumentToTheSameTaskTwiceIsRejectedByTheUniqueIndex()
    {
        var document = await _data.AddDocumentAsync(4, null, $"Schema dup-attach {Guid.NewGuid():N}", "Other");
        await _data.AddTaskAttachmentAsync(4, document.DocumentId, 4);

        await Assert.ThrowsAsync<DbUpdateException>(() => _data.AddTaskAttachmentAsync(4, document.DocumentId, 4));
    }

    [Fact]
    public async Task DocumentActivityHasNoForeignKeyOnDocumentIdOrTaskId()
    {
        await using var context = _fixture.CreateContext();
        var unknownDocumentId = 2_000_000_000;

        context.DocumentActivities.Add(new DocumentActivity
        {
            DocumentId = unknownDocumentId,
            DocumentTitle = "Deleted document snapshot",
            UserId = 4,
            Action = DocumentActivityAction.Delete,
            TaskId = 2_000_000_001
        });

        await context.SaveChangesAsync();
        Assert.True(await context.DocumentActivities.AnyAsync(a => a.DocumentId == unknownDocumentId));
    }

    private static void AssertUser(User user, int id, string name, string email, UserRole role, string department)
    {
        Assert.Equal(id, user.UserId);
        Assert.Equal(name, user.DisplayName);
        Assert.Equal(email, user.Email);
        Assert.Equal(role, user.Role);
        Assert.Equal(department, user.Department);
    }
}
