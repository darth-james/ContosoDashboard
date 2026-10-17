using System.Linq.Expressions;
using ContosoDashboard.Data;
using ContosoDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Services;

/// <summary>The requester's live role and department, loaded from <c>Users</c> on every service call (R-02).</summary>
public sealed record DocumentUserContext(int UserId, UserRole Role, string? Department);

/// <summary>
/// Shared, EF-translatable document access predicates (research R-02). Lists, search, the dashboard, project and
/// task panels, and notification recipients all compose these into the SQL query, so they always agree.
/// An empty or whitespace department counts as "no department". Claims are never used here.
/// </summary>
public static class DocumentAccessPolicy
{
    /// <summary>Reads the user live from the database. Returns null for an unknown user.</summary>
    public static async Task<DocumentUserContext?> LoadUserContextAsync(ApplicationDbContext context, int userId)
    {
        ArgumentNullException.ThrowIfNull(context);

        var user = await context.Users
            .AsNoTracking()
            .Where(u => u.UserId == userId)
            .Select(u => new { u.UserId, u.Role, u.Department })
            .FirstOrDefaultAsync();

        return user == null ? null : new DocumentUserContext(user.UserId, user.Role, user.Department);
    }

    /// <summary>
    /// The requester can view, preview and download the document: the uploader; a member or manager of the document's
    /// project; a share recipient (by user, or by the requester's current department); team oversight; or an Administrator.
    /// </summary>
    public static Expression<Func<Document, bool>> CanAccess(DocumentUserContext user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.Role == UserRole.Administrator)
        {
            return d => true;
        }

        var me = user.UserId;
        var department = NormalizeDepartment(user.Department);

        if (department != null && HasOversightRole(user))
        {
            return d =>
                d.UploadedByUserId == me
                || (d.Project != null && (d.Project.ProjectManagerId == me || d.Project.ProjectMembers.Any(m => m.UserId == me)))
                || d.Shares.Any(s => s.SharedWithUserId == me || s.SharedWithDepartment == department)
                || (d.UploadedByUser.Department == department && d.UploadedByUser.Role == UserRole.Employee);
        }

        if (department != null)
        {
            return d =>
                d.UploadedByUserId == me
                || (d.Project != null && (d.Project.ProjectManagerId == me || d.Project.ProjectMembers.Any(m => m.UserId == me)))
                || d.Shares.Any(s => s.SharedWithUserId == me || s.SharedWithDepartment == department);
        }

        return d =>
            d.UploadedByUserId == me
            || (d.Project != null && (d.Project.ProjectManagerId == me || d.Project.ProjectMembers.Any(m => m.UserId == me)))
            || d.Shares.Any(s => s.SharedWithUserId == me);
    }

    /// <summary>
    /// The requester can edit metadata and delete the document: the uploader; team oversight; the manager of the
    /// document's project (authority comes from <c>Project.ProjectManagerId</c>, not the role name); or an Administrator.
    /// </summary>
    public static Expression<Func<Document, bool>> CanManage(DocumentUserContext user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.Role == UserRole.Administrator)
        {
            return d => true;
        }

        var me = user.UserId;
        var department = NormalizeDepartment(user.Department);

        if (department != null && HasOversightRole(user))
        {
            return d =>
                d.UploadedByUserId == me
                || (d.Project != null && d.Project.ProjectManagerId == me)
                || (d.UploadedByUser.Department == department && d.UploadedByUser.Role == UserRole.Employee);
        }

        return d =>
            d.UploadedByUserId == me
            || (d.Project != null && d.Project.ProjectManagerId == me);
    }

    /// <summary>Owner-only actions (replace file, share, remove share): the uploader, for every role.</summary>
    public static Expression<Func<Document, bool>> IsOwner(DocumentUserContext user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var me = user.UserId;
        return d => d.UploadedByUserId == me;
    }

    /// <summary>
    /// Team oversight: the requester's role is at least Team Lead, they have a non-empty department, and the document's
    /// uploader is currently in that department and currently has the Employee role.
    /// </summary>
    public static Expression<Func<Document, bool>> IsTeamOversight(DocumentUserContext user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var department = NormalizeDepartment(user.Department);
        if (department == null || !HasOversightRole(user))
        {
            return d => false;
        }

        return d => d.UploadedByUser.Department == department && d.UploadedByUser.Role == UserRole.Employee;
    }

    /// <summary>
    /// Projects the requester may upload to: a member or the manager, or every project for an Administrator.
    /// </summary>
    public static Expression<Func<Project, bool>> CanUploadToProject(DocumentUserContext user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.Role == UserRole.Administrator)
        {
            return p => true;
        }

        var me = user.UserId;
        return p => p.ProjectManagerId == me || p.ProjectMembers.Any(m => m.UserId == me);
    }

    private static bool HasOversightRole(DocumentUserContext user) => user.Role >= UserRole.TeamLead;

    private static string? NormalizeDepartment(string? department)
    {
        return string.IsNullOrWhiteSpace(department) ? null : department;
    }
}
