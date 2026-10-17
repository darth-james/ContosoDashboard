using Microsoft.EntityFrameworkCore;
using ContosoDashboard.Models;

namespace ContosoDashboard.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<TaskItem> Tasks { get; set; } = null!;
    public DbSet<Project> Projects { get; set; } = null!;
    public DbSet<TaskComment> TaskComments { get; set; } = null!;
    public DbSet<Notification> Notifications { get; set; } = null!;
    public DbSet<ProjectMember> ProjectMembers { get; set; } = null!;
    public DbSet<Announcement> Announcements { get; set; } = null!;
    public DbSet<Document> Documents { get; set; } = null!;
    public DbSet<DocumentTag> DocumentTags { get; set; } = null!;
    public DbSet<DocumentShare> DocumentShares { get; set; } = null!;
    public DbSet<TaskDocument> TaskDocuments { get; set; } = null!;
    public DbSet<DocumentActivity> DocumentActivities { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure User relationships
        modelBuilder.Entity<User>()
            .HasMany(u => u.AssignedTasks)
            .WithOne(t => t.AssignedUser)
            .HasForeignKey(t => t.AssignedUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<User>()
            .HasMany(u => u.CreatedTasks)
            .WithOne(t => t.CreatedByUser)
            .HasForeignKey(t => t.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<User>()
            .HasMany(u => u.ManagedProjects)
            .WithOne(p => p.ProjectManager)
            .HasForeignKey(p => p.ProjectManagerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure indexes for performance
        modelBuilder.Entity<TaskItem>()
            .HasIndex(t => t.AssignedUserId);

        modelBuilder.Entity<TaskItem>()
            .HasIndex(t => t.Status);

        modelBuilder.Entity<TaskItem>()
            .HasIndex(t => t.DueDate);

        modelBuilder.Entity<Project>()
            .HasIndex(p => p.ProjectManagerId);

        modelBuilder.Entity<Project>()
            .HasIndex(p => p.Status);

        modelBuilder.Entity<Notification>()
            .HasIndex(n => new { n.UserId, n.IsRead });

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        ConfigureDocuments(modelBuilder);

        // Seed initial data
        SeedData(modelBuilder);
    }

    private static void ConfigureDocuments(ModelBuilder modelBuilder)
    {
        // Document relationships. Every relationship from Users and Projects is Restrict,
        // which rules out "multiple cascade paths" errors on SQL Server.
        modelBuilder.Entity<Document>()
            .HasOne(d => d.UploadedByUser)
            .WithMany()
            .HasForeignKey(d => d.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Document>()
            .HasOne(d => d.Project)
            .WithMany(p => p.Documents)
            .HasForeignKey(d => d.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DocumentTag>()
            .HasOne(t => t.Document)
            .WithMany(d => d.Tags)
            .HasForeignKey(t => t.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DocumentShare>()
            .HasOne(s => s.Document)
            .WithMany(d => d.Shares)
            .HasForeignKey(s => s.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DocumentShare>()
            .HasOne(s => s.SharedByUser)
            .WithMany()
            .HasForeignKey(s => s.SharedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DocumentShare>()
            .HasOne(s => s.SharedWithUser)
            .WithMany()
            .HasForeignKey(s => s.SharedWithUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TaskDocument>()
            .HasOne(td => td.Task)
            .WithMany(t => t.DocumentAttachments)
            .HasForeignKey(td => td.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TaskDocument>()
            .HasOne(td => td.Document)
            .WithMany(d => d.TaskAttachments)
            .HasForeignKey(td => td.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TaskDocument>()
            .HasOne(td => td.AttachedByUser)
            .WithMany()
            .HasForeignKey(td => td.AttachedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DocumentActivity>()
            .HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // DocumentActivity.DocumentId, DocumentActivity.TaskId and Notification.DocumentId
        // intentionally have no relationship: the audit record and the notification survive deletion.

        // Document indexes
        modelBuilder.Entity<Document>()
            .HasIndex(d => new { d.UploadedByUserId, d.UploadedDate })
            .IsDescending(false, true)
            .HasDatabaseName("IX_Documents_UploadedByUserId_UploadedDate");

        modelBuilder.Entity<Document>()
            .HasIndex(d => d.ProjectId)
            .HasDatabaseName("IX_Documents_ProjectId");

        modelBuilder.Entity<Document>()
            .HasIndex(d => d.Category)
            .HasDatabaseName("IX_Documents_Category");

        modelBuilder.Entity<Document>()
            .HasIndex(d => d.UploadedDate)
            .HasDatabaseName("IX_Documents_UploadedDate");

        modelBuilder.Entity<Document>()
            .HasIndex(d => d.FilePath)
            .IsUnique()
            .HasDatabaseName("IX_Documents_FilePath");

        // DocumentTag indexes
        modelBuilder.Entity<DocumentTag>()
            .HasIndex(t => new { t.DocumentId, t.Tag })
            .IsUnique();

        modelBuilder.Entity<DocumentTag>()
            .HasIndex(t => t.Tag);

        // DocumentShare indexes and constraint
        modelBuilder.Entity<DocumentShare>()
            .HasIndex(s => new { s.DocumentId, s.SharedWithUserId })
            .IsUnique()
            .HasFilter("[SharedWithUserId] IS NOT NULL");

        modelBuilder.Entity<DocumentShare>()
            .HasIndex(s => new { s.DocumentId, s.SharedWithDepartment })
            .IsUnique()
            .HasFilter("[SharedWithDepartment] IS NOT NULL");

        modelBuilder.Entity<DocumentShare>()
            .HasIndex(s => s.SharedWithUserId);

        modelBuilder.Entity<DocumentShare>()
            .HasIndex(s => s.SharedWithDepartment);

        modelBuilder.Entity<DocumentShare>()
            .ToTable(t => t.HasCheckConstraint(
                "CK_DocumentShares_SingleTarget",
                "([SharedWithUserId] IS NOT NULL AND [SharedWithDepartment] IS NULL) OR ([SharedWithUserId] IS NULL AND [SharedWithDepartment] IS NOT NULL)"));

        // TaskDocument indexes
        modelBuilder.Entity<TaskDocument>()
            .HasIndex(td => new { td.TaskId, td.DocumentId })
            .IsUnique();

        modelBuilder.Entity<TaskDocument>()
            .HasIndex(td => td.DocumentId);

        // DocumentActivity indexes
        modelBuilder.Entity<DocumentActivity>()
            .HasIndex(a => a.OccurredAt);

        modelBuilder.Entity<DocumentActivity>()
            .HasIndex(a => new { a.Action, a.OccurredAt });

        modelBuilder.Entity<DocumentActivity>()
            .HasIndex(a => a.DocumentId);

        modelBuilder.Entity<DocumentActivity>()
            .HasIndex(a => a.UserId);
    }

    private void SeedData(ModelBuilder modelBuilder)
    {
        // Seed an admin user
        modelBuilder.Entity<User>().HasData(
            new User
            {
                UserId = 1,
                Email = "admin@contoso.com",
                DisplayName = "System Administrator",
                Department = "IT",
                JobTitle = "Administrator",
                Role = UserRole.Administrator,
                AvailabilityStatus = AvailabilityStatus.Available,
                CreatedDate = DateTime.UtcNow,
                EmailNotificationsEnabled = true,
                InAppNotificationsEnabled = true
            },
            new User
            {
                UserId = 2,
                Email = "camille.nicole@contoso.com",
                DisplayName = "Camille Nicole",
                Department = "Engineering",
                JobTitle = "Project Manager",
                Role = UserRole.ProjectManager,
                AvailabilityStatus = AvailabilityStatus.Available,
                CreatedDate = DateTime.UtcNow,
                EmailNotificationsEnabled = true,
                InAppNotificationsEnabled = true
            },
            new User
            {
                UserId = 3,
                Email = "floris.kregel@contoso.com",
                DisplayName = "Floris Kregel",
                Department = "Engineering",
                JobTitle = "Team Lead",
                Role = UserRole.TeamLead,
                AvailabilityStatus = AvailabilityStatus.Available,
                CreatedDate = DateTime.UtcNow,
                EmailNotificationsEnabled = true,
                InAppNotificationsEnabled = true
            },
            new User
            {
                UserId = 4,
                Email = "ni.kang@contoso.com",
                DisplayName = "Ni Kang",
                Department = "Engineering",
                JobTitle = "Software Engineer",
                Role = UserRole.Employee,
                AvailabilityStatus = AvailabilityStatus.Available,
                CreatedDate = DateTime.UtcNow,
                EmailNotificationsEnabled = true,
                InAppNotificationsEnabled = true
            },
            new User
            {
                UserId = 5,
                Email = "dana.whitfield@contoso.com",
                DisplayName = "Dana Whitfield",
                Department = "Engineering",
                JobTitle = "Software Engineer",
                Role = UserRole.Employee,
                AvailabilityStatus = AvailabilityStatus.Available,
                CreatedDate = DateTime.UtcNow,
                EmailNotificationsEnabled = true,
                InAppNotificationsEnabled = true
            },
            new User
            {
                UserId = 6,
                Email = "mateo.alvarez@contoso.com",
                DisplayName = "Mateo Alvarez",
                Department = "Marketing",
                JobTitle = "Marketing Specialist",
                Role = UserRole.Employee,
                AvailabilityStatus = AvailabilityStatus.Available,
                CreatedDate = DateTime.UtcNow,
                EmailNotificationsEnabled = true,
                InAppNotificationsEnabled = true
            },
            new User
            {
                UserId = 7,
                Email = "hana.sato@contoso.com",
                DisplayName = "Hana Sato",
                Department = "Marketing",
                JobTitle = "Team Lead",
                Role = UserRole.TeamLead,
                AvailabilityStatus = AvailabilityStatus.Available,
                CreatedDate = DateTime.UtcNow,
                EmailNotificationsEnabled = true,
                InAppNotificationsEnabled = true
            },
            new User
            {
                UserId = 8,
                Email = "rafael.costa@contoso.com",
                DisplayName = "Rafael Costa",
                Department = "Marketing",
                JobTitle = "Project Manager",
                Role = UserRole.ProjectManager,
                AvailabilityStatus = AvailabilityStatus.Available,
                CreatedDate = DateTime.UtcNow,
                EmailNotificationsEnabled = true,
                InAppNotificationsEnabled = true
            }
        );

        // Seed a sample project
        modelBuilder.Entity<Project>().HasData(
            new Project
            {
                ProjectId = 1,
                Name = "ContosoDashboard Development",
                Description = "Internal employee productivity dashboard",
                ProjectManagerId = 2,
                StartDate = DateTime.UtcNow.AddDays(-30),
                TargetCompletionDate = DateTime.UtcNow.AddDays(60),
                Status = ProjectStatus.Active,
                CreatedDate = DateTime.UtcNow.AddDays(-30),
                UpdatedDate = DateTime.UtcNow
            },
            new Project
            {
                ProjectId = 2,
                Name = "Marketing Website Refresh",
                Description = "Refresh of the public marketing website",
                ProjectManagerId = 8,
                StartDate = DateTime.UtcNow.AddDays(-10),
                TargetCompletionDate = DateTime.UtcNow.AddDays(50),
                Status = ProjectStatus.Active,
                CreatedDate = DateTime.UtcNow.AddDays(-10),
                UpdatedDate = DateTime.UtcNow
            }
        );

        // Seed sample tasks
        modelBuilder.Entity<TaskItem>().HasData(
            new TaskItem
            {
                TaskId = 1,
                Title = "Design database schema",
                Description = "Create entity relationship diagram and database design",
                Priority = TaskPriority.High,
                Status = Models.TaskStatus.Completed,
                DueDate = DateTime.UtcNow.AddDays(-20),
                AssignedUserId = 4,
                CreatedByUserId = 2,
                ProjectId = 1,
                CreatedDate = DateTime.UtcNow.AddDays(-30),
                UpdatedDate = DateTime.UtcNow.AddDays(-20)
            },
            new TaskItem
            {
                TaskId = 2,
                Title = "Implement authentication",
                Description = "Set up Microsoft Entra ID authentication",
                Priority = TaskPriority.Critical,
                Status = Models.TaskStatus.InProgress,
                DueDate = DateTime.UtcNow.AddDays(5),
                AssignedUserId = 4,
                CreatedByUserId = 2,
                ProjectId = 1,
                CreatedDate = DateTime.UtcNow.AddDays(-25),
                UpdatedDate = DateTime.UtcNow
            },
            new TaskItem
            {
                TaskId = 3,
                Title = "Create UI mockups",
                Description = "Design user interface mockups for all main pages",
                Priority = TaskPriority.Medium,
                Status = Models.TaskStatus.NotStarted,
                DueDate = DateTime.UtcNow.AddDays(10),
                AssignedUserId = 4,
                CreatedByUserId = 2,
                ProjectId = 1,
                CreatedDate = DateTime.UtcNow.AddDays(-20),
                UpdatedDate = DateTime.UtcNow.AddDays(-20)
            },
            new TaskItem
            {
                TaskId = 4,
                Title = "Prepare onboarding checklist",
                Description = "Personal task that belongs to no project",
                Priority = TaskPriority.Medium,
                Status = Models.TaskStatus.NotStarted,
                DueDate = DateTime.UtcNow.AddDays(7),
                AssignedUserId = 4,
                CreatedByUserId = 3,
                ProjectId = null,
                CreatedDate = DateTime.UtcNow.AddDays(-2),
                UpdatedDate = DateTime.UtcNow.AddDays(-2)
            },
            new TaskItem
            {
                TaskId = 5,
                Title = "Draft launch copy",
                Description = "Write the launch copy for the refreshed marketing website",
                Priority = TaskPriority.Medium,
                Status = Models.TaskStatus.NotStarted,
                DueDate = DateTime.UtcNow.AddDays(14),
                AssignedUserId = 6,
                CreatedByUserId = 8,
                ProjectId = 2,
                CreatedDate = DateTime.UtcNow.AddDays(-2),
                UpdatedDate = DateTime.UtcNow.AddDays(-2)
            }
        );

        // Seed project members
        modelBuilder.Entity<ProjectMember>().HasData(
            new ProjectMember
            {
                ProjectMemberId = 1,
                ProjectId = 1,
                UserId = 3,
                Role = "TeamLead",
                AssignedDate = DateTime.UtcNow.AddDays(-30)
            },
            new ProjectMember
            {
                ProjectMemberId = 2,
                ProjectId = 1,
                UserId = 4,
                Role = "Developer",
                AssignedDate = DateTime.UtcNow.AddDays(-30)
            },
            new ProjectMember
            {
                ProjectMemberId = 3,
                ProjectId = 2,
                UserId = 6,
                Role = "Developer",
                AssignedDate = DateTime.UtcNow.AddDays(-10)
            }
        );

        // Seed announcement
        modelBuilder.Entity<Announcement>().HasData(
            new Announcement
            {
                AnnouncementId = 1,
                Title = "Welcome to ContosoDashboard",
                Content = "Welcome to the new ContosoDashboard application. This platform will help you manage your tasks and projects more efficiently.",
                CreatedByUserId = 1,
                PublishDate = DateTime.UtcNow,
                ExpiryDate = DateTime.UtcNow.AddDays(30),
                IsActive = true
            }
        );
    }
}
