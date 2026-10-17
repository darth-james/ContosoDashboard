using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ContosoDashboard.Tests;

/// <summary>
/// Tests for DocumentAccessPolicy (T029, research R-02). Every predicate runs as an EF query against LocalDB,
/// so a client-evaluation exception fails the test.
/// New tests that call stubbed members fail at runtime (red) until T034 replaces the stubs.
/// </summary>
[Collection("LocalDb")]
[Trait("Story", "Foundation")]
public class DocumentAccessPolicyTests
{
    private const int Admin = 1;
    private const int Camille = 2;   // ProjectManager, Engineering, manages Project 1
    private const int Floris = 3;    // TeamLead, Engineering, member of Project 1
    private const int NiKang = 4;    // Employee, Engineering, member of Project 1
    private const int Dana = 5;      // Employee, Engineering, no project
    private const int Mateo = 6;     // Employee, Marketing, member of Project 2
    private const int Hana = 7;      // TeamLead, Marketing
    private const int Rafael = 8;    // ProjectManager, Marketing, manages Project 2
    private const int Project1 = 1;
    private const int Project2 = 2;

    private readonly LocalDbFixture _fixture;
    private readonly DocumentTestData _data;

    public DocumentAccessPolicyTests(LocalDbFixture fixture)
    {
        _fixture = fixture;
        _data = new DocumentTestData(fixture);
    }

    // ---- helpers: each predicate runs as a SQL query ----

    private async Task<DocumentUserContext> UserAsync(int userId)
    {
        await using var context = _fixture.CreateContext();
        var user = await DocumentAccessPolicy.LoadUserContextAsync(context, userId);
        Assert.NotNull(user);
        return user!;
    }

    private async Task<bool> CanAccessAsync(int documentId, int userId)
    {
        var user = await UserAsync(userId);
        await using var context = _fixture.CreateContext();
        return await context.Documents.Where(DocumentAccessPolicy.CanAccess(user)).AnyAsync(d => d.DocumentId == documentId);
    }

    private async Task<bool> CanManageAsync(int documentId, int userId)
    {
        var user = await UserAsync(userId);
        await using var context = _fixture.CreateContext();
        return await context.Documents.Where(DocumentAccessPolicy.CanManage(user)).AnyAsync(d => d.DocumentId == documentId);
    }

    private async Task<bool> IsOwnerAsync(int documentId, int userId)
    {
        var user = await UserAsync(userId);
        await using var context = _fixture.CreateContext();
        return await context.Documents.Where(DocumentAccessPolicy.IsOwner(user)).AnyAsync(d => d.DocumentId == documentId);
    }

    private async Task<bool> IsTeamOversightAsync(int documentId, int userId)
    {
        var user = await UserAsync(userId);
        await using var context = _fixture.CreateContext();
        return await context.Documents.Where(DocumentAccessPolicy.IsTeamOversight(user)).AnyAsync(d => d.DocumentId == documentId);
    }

    private async Task<List<int>> UploadableProjectIdsAsync(int userId)
    {
        var user = await UserAsync(userId);
        await using var context = _fixture.CreateContext();
        return await context.Projects.Where(DocumentAccessPolicy.CanUploadToProject(user)).Select(p => p.ProjectId).ToListAsync();
    }

    private static string UniqueTitle(string prefix) => $"{prefix} {Guid.NewGuid():N}";

    private async Task SetDepartmentAsync(int userId, string? department)
    {
        await using var context = _fixture.CreateContext();
        var user = await context.Users.SingleAsync(u => u.UserId == userId);
        user.Department = department;
        await context.SaveChangesAsync();
    }

    private async Task SetRoleAsync(int userId, UserRole role)
    {
        await using var context = _fixture.CreateContext();
        var user = await context.Users.SingleAsync(u => u.UserId == userId);
        user.Role = role;
        await context.SaveChangesAsync();
    }

    // ---- LoadUserContextAsync ----

    [Fact]
    public async Task LoadUserContext_ForAnUnknownUser_ReturnsNull()
    {
        await using var context = _fixture.CreateContext();

        var user = await DocumentAccessPolicy.LoadUserContextAsync(context, 2_000_000_000);

        Assert.Null(user);
    }

    [Fact]
    public async Task LoadUserContext_ReadsRoleAndDepartmentLiveFromUsers()
    {
        var throwaway = await _data.AddUserAsync(UserRole.Employee, "Finance");

        var before = await UserAsync(throwaway.UserId);
        await SetDepartmentAsync(throwaway.UserId, "Legal");
        await SetRoleAsync(throwaway.UserId, UserRole.TeamLead);
        var after = await UserAsync(throwaway.UserId);

        Assert.Equal(new DocumentUserContext(throwaway.UserId, UserRole.Employee, "Finance"), before);
        Assert.Equal(new DocumentUserContext(throwaway.UserId, UserRole.TeamLead, "Legal"), after);
    }

    [Fact]
    public async Task LoadUserContext_ReturnsTheSeededUsers()
    {
        Assert.Equal(new DocumentUserContext(NiKang, UserRole.Employee, "Engineering"), await UserAsync(NiKang));
        Assert.Equal(new DocumentUserContext(Admin, UserRole.Administrator, "IT"), await UserAsync(Admin));
    }

    // ---- Owner ----

    [Fact]
    public async Task Owner_CanAccessAndManageTheirOwnDocument()
    {
        var document = await _data.AddDocumentAsync(NiKang, null, UniqueTitle("Owner doc"), "Personal Files");

        Assert.True(await CanAccessAsync(document.DocumentId, NiKang));
        Assert.True(await CanManageAsync(document.DocumentId, NiKang));
        Assert.True(await IsOwnerAsync(document.DocumentId, NiKang));
    }

    [Fact]
    public async Task AnotherEmployeeInTheSameDepartment_CannotAccessOrManageAPersonalDocument()
    {
        var document = await _data.AddDocumentAsync(NiKang, null, UniqueTitle("Personal doc"), "Personal Files");

        Assert.False(await CanAccessAsync(document.DocumentId, Dana));
        Assert.False(await CanManageAsync(document.DocumentId, Dana));
        Assert.False(await IsOwnerAsync(document.DocumentId, Dana));
    }

    // ---- Project ----

    [Fact]
    public async Task ProjectMember_CanAccessButNotManageAProjectDocumentUploadedByAnother()
    {
        var document = await _data.AddDocumentAsync(Admin, Project1, UniqueTitle("Project 1 doc"), "Project Documents");

        Assert.True(await CanAccessAsync(document.DocumentId, NiKang));   // member
        Assert.True(await CanAccessAsync(document.DocumentId, Floris));   // member
        Assert.False(await CanManageAsync(document.DocumentId, NiKang));
        Assert.False(await IsOwnerAsync(document.DocumentId, NiKang));
    }

    [Fact]
    public async Task ProjectManager_CanAccessAndManageDocumentsInTheProjectTheyManage()
    {
        var document = await _data.AddDocumentAsync(NiKang, Project1, UniqueTitle("Managed project doc"), "Project Documents");

        Assert.True(await CanAccessAsync(document.DocumentId, Camille));
        Assert.True(await CanManageAsync(document.DocumentId, Camille));
        Assert.False(await IsOwnerAsync(document.DocumentId, Camille));
    }

    [Fact]
    public async Task NonMember_CannotAccessAProjectDocument()
    {
        var document = await _data.AddDocumentAsync(Admin, Project1, UniqueTitle("Project 1 private"), "Project Documents");

        Assert.False(await CanAccessAsync(document.DocumentId, Dana));
        Assert.False(await CanAccessAsync(document.DocumentId, Mateo));
        Assert.False(await CanManageAsync(document.DocumentId, Dana));
    }

    [Fact]
    public async Task ProjectManagerOfAnotherProject_CannotAccessOrManage()
    {
        var document = await _data.AddDocumentAsync(Admin, Project1, UniqueTitle("Project 1 other PM"), "Project Documents");

        Assert.False(await CanAccessAsync(document.DocumentId, Rafael));
        Assert.False(await CanManageAsync(document.DocumentId, Rafael));
    }

    [Fact]
    public async Task ProjectManagerAuthority_ComesFromProjectManagerId_NotFromTheRoleName()
    {
        // A ProjectManager-role user who manages nothing has no project authority.
        var roleOnlyPm = await _data.AddUserAsync(UserRole.ProjectManager, null);
        var document = await _data.AddDocumentAsync(Admin, Project1, UniqueTitle("PM role only"), "Project Documents");

        Assert.False(await CanAccessAsync(document.DocumentId, roleOnlyPm.UserId));
        Assert.False(await CanManageAsync(document.DocumentId, roleOnlyPm.UserId));

        // An Employee-role user who is the ProjectManagerId of a project does.
        var employeeManager = await _data.AddUserAsync(UserRole.Employee, null);
        var project = await _data.AddProjectAsync(employeeManager.UserId, "Managed by an Employee");
        var projectDocument = await _data.AddDocumentAsync(Admin, project.ProjectId, UniqueTitle("Employee-managed"), "Project Documents");

        Assert.True(await CanAccessAsync(projectDocument.DocumentId, employeeManager.UserId));
        Assert.True(await CanManageAsync(projectDocument.DocumentId, employeeManager.UserId));
    }

    // ---- Shares ----

    [Fact]
    public async Task UserShare_GrantsAccessToTheRecipientOnly_NotManage()
    {
        var document = await _data.AddDocumentAsync(NiKang, null, UniqueTitle("User share"), "Reports");
        await _data.AddUserShareAsync(document.DocumentId, NiKang, Mateo);

        Assert.True(await CanAccessAsync(document.DocumentId, Mateo));
        Assert.False(await CanManageAsync(document.DocumentId, Mateo));
        Assert.False(await CanAccessAsync(document.DocumentId, Dana));
    }

    [Fact]
    public async Task DepartmentShare_GrantsAccessToEveryoneCurrentlyInThatDepartment()
    {
        var document = await _data.AddDocumentAsync(NiKang, null, UniqueTitle("Team share"), "Team Resources");
        await _data.AddDepartmentShareAsync(document.DocumentId, NiKang, "Marketing");

        Assert.True(await CanAccessAsync(document.DocumentId, Mateo));
        Assert.True(await CanAccessAsync(document.DocumentId, Hana));
        Assert.True(await CanAccessAsync(document.DocumentId, Rafael));
        Assert.False(await CanAccessAsync(document.DocumentId, Dana));   // Engineering, an Employee: no oversight either
        Assert.False(await CanManageAsync(document.DocumentId, Mateo));
    }

    [Fact]
    public async Task DepartmentShare_FollowsTheRecipientsLiveDepartment()
    {
        var document = await _data.AddDocumentAsync(NiKang, null, UniqueTitle("Live team share"), "Team Resources");
        await _data.AddDepartmentShareAsync(document.DocumentId, NiKang, "Marketing");
        var mover = await _data.AddUserAsync(UserRole.Employee, "Finance");

        Assert.False(await CanAccessAsync(document.DocumentId, mover.UserId));

        await SetDepartmentAsync(mover.UserId, "Marketing");
        Assert.True(await CanAccessAsync(document.DocumentId, mover.UserId));

        await SetDepartmentAsync(mover.UserId, "Finance");
        Assert.False(await CanAccessAsync(document.DocumentId, mover.UserId));
    }

    [Fact]
    public async Task DepartmentShare_NeverMatchesAUserWithoutADepartment()
    {
        var document = await _data.AddDocumentAsync(NiKang, null, UniqueTitle("No-dept share"), "Team Resources");
        await _data.AddDepartmentShareAsync(document.DocumentId, NiKang, "Marketing");
        var noDepartment = await _data.AddUserAsync(UserRole.Employee, null);

        Assert.False(await CanAccessAsync(document.DocumentId, noDepartment.UserId));
    }

    // ---- Team oversight ----

    [Fact]
    public async Task TeamLead_HasOversightOfEmployeesInTheirDepartment()
    {
        var document = await _data.AddDocumentAsync(NiKang, null, UniqueTitle("Oversight"), "Personal Files");

        Assert.True(await IsTeamOversightAsync(document.DocumentId, Floris));
        Assert.True(await CanAccessAsync(document.DocumentId, Floris));
        Assert.True(await CanManageAsync(document.DocumentId, Floris));
        Assert.False(await IsOwnerAsync(document.DocumentId, Floris));
    }

    [Fact]
    public async Task ProjectManager_InheritsTeamOversightInTheirOwnDepartment()
    {
        var document = await _data.AddDocumentAsync(NiKang, null, UniqueTitle("PM oversight"), "Personal Files");

        Assert.True(await IsTeamOversightAsync(document.DocumentId, Camille));   // Engineering
        Assert.True(await CanAccessAsync(document.DocumentId, Camille));
        Assert.True(await CanManageAsync(document.DocumentId, Camille));
    }

    [Fact]
    public async Task TeamLeadInAnotherDepartment_HasNoOversight()
    {
        var document = await _data.AddDocumentAsync(NiKang, null, UniqueTitle("Cross-dept"), "Personal Files");

        Assert.False(await IsTeamOversightAsync(document.DocumentId, Hana));      // Marketing
        Assert.False(await CanAccessAsync(document.DocumentId, Hana));
        Assert.False(await CanManageAsync(document.DocumentId, Hana));
        Assert.False(await CanAccessAsync(document.DocumentId, Rafael));          // PM in Marketing
    }

    [Fact]
    public async Task Oversight_DoesNotCoverDocumentsUploadedByANonEmployee()
    {
        var teamLeadsDocument = await _data.AddDocumentAsync(Floris, null, UniqueTitle("TL upload"), "Personal Files");

        Assert.False(await IsTeamOversightAsync(teamLeadsDocument.DocumentId, Camille));
        Assert.False(await CanAccessAsync(teamLeadsDocument.DocumentId, Camille));
    }

    [Fact]
    public async Task EmployeesHaveNoOversightEvenInTheSameDepartment()
    {
        var document = await _data.AddDocumentAsync(NiKang, null, UniqueTitle("Peer"), "Personal Files");

        Assert.False(await IsTeamOversightAsync(document.DocumentId, Dana));
    }

    [Fact]
    public async Task Oversight_FollowsTheUploadersLiveDepartmentAndRole()
    {
        var uploader = await _data.AddUserAsync(UserRole.Employee, "Engineering");
        var document = await _data.AddDocumentAsync(uploader.UserId, null, UniqueTitle("Live oversight"), "Personal Files");

        Assert.True(await CanManageAsync(document.DocumentId, Floris));

        await SetDepartmentAsync(uploader.UserId, "Marketing");
        Assert.False(await CanManageAsync(document.DocumentId, Floris));
        Assert.True(await CanManageAsync(document.DocumentId, Hana));

        await SetDepartmentAsync(uploader.UserId, "Engineering");
        await SetRoleAsync(uploader.UserId, UserRole.TeamLead);
        Assert.False(await CanManageAsync(document.DocumentId, Floris));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyOrWhitespaceDepartment_IsTreatedAsNoDepartment(string? department)
    {
        var teamLead = await _data.AddUserAsync(UserRole.TeamLead, department);
        var employee = await _data.AddUserAsync(UserRole.Employee, department);
        var document = await _data.AddDocumentAsync(employee.UserId, null, UniqueTitle("No dept"), "Personal Files");

        Assert.False(await IsTeamOversightAsync(document.DocumentId, teamLead.UserId));
        Assert.False(await CanAccessAsync(document.DocumentId, teamLead.UserId));
        Assert.False(await CanManageAsync(document.DocumentId, teamLead.UserId));
    }

    // ---- Administrator ----

    [Fact]
    public async Task Administrator_CanAccessAndManageEverything_ButIsNotTheOwner()
    {
        var personal = await _data.AddDocumentAsync(NiKang, null, UniqueTitle("Admin personal"), "Personal Files");
        var project2 = await _data.AddDocumentAsync(Mateo, Project2, UniqueTitle("Admin project 2"), "Project Documents");

        Assert.True(await CanAccessAsync(personal.DocumentId, Admin));
        Assert.True(await CanManageAsync(personal.DocumentId, Admin));
        Assert.True(await CanAccessAsync(project2.DocumentId, Admin));
        Assert.True(await CanManageAsync(project2.DocumentId, Admin));
        Assert.False(await IsOwnerAsync(personal.DocumentId, Admin));
    }

    // ---- Upload to a project ----

    [Fact]
    public async Task CanUploadToProject_AllowsMembersAndManagers_AndAdministratorsAnywhere()
    {
        var ni = await UploadableProjectIdsAsync(NiKang);
        var camille = await UploadableProjectIdsAsync(Camille);
        var mateo = await UploadableProjectIdsAsync(Mateo);
        var rafael = await UploadableProjectIdsAsync(Rafael);
        var admin = await UploadableProjectIdsAsync(Admin);

        Assert.Contains(Project1, ni);
        Assert.DoesNotContain(Project2, ni);
        Assert.Contains(Project1, camille);         // manager
        Assert.DoesNotContain(Project2, camille);
        Assert.Contains(Project2, mateo);           // member
        Assert.DoesNotContain(Project1, mateo);
        Assert.Contains(Project2, rafael);          // manager
        Assert.DoesNotContain(Project1, rafael);
        Assert.Contains(Project1, admin);
        Assert.Contains(Project2, admin);
    }

    [Fact]
    public async Task CanUploadToProject_DeniesANonMember()
    {
        var dana = await UploadableProjectIdsAsync(Dana);

        Assert.DoesNotContain(Project1, dana);
        Assert.DoesNotContain(Project2, dana);
    }
}
