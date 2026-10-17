using ContosoDashboard.Models;

namespace ContosoDashboard.Tests;

/// <summary>
/// Arrange helpers that insert rows directly through ApplicationDbContext, so a story's tests
/// don't depend on another story's service.
/// </summary>
public sealed class DocumentTestData
{
    private readonly LocalDbFixture _fixture;

    public DocumentTestData(LocalDbFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Inserts a Document (and tags). When <paramref name="bytes"/> is provided, the bytes are written through
    /// LocalFileStorageService under the generated storage key first (file, then record).
    /// </summary>
    public async Task<Document> AddDocumentAsync(
        int uploaderId,
        int? projectId,
        string title,
        string category,
        IEnumerable<string>? tags = null,
        string fileType = "application/pdf",
        byte[]? bytes = null)
    {
        var extension = ExtensionFor(fileType);
        var key = $"{uploaderId}/{(projectId?.ToString() ?? "personal")}/{Guid.NewGuid():N}{extension}";

        if (bytes != null)
        {
            var storage = TestServiceFactory.CreateFileStorage(_fixture);
            using var stream = new MemoryStream(bytes);
            await storage.UploadAsync(stream, key, fileType);
        }

        await using var context = _fixture.CreateContext();
        var now = DateTime.UtcNow;
        var document = new Document
        {
            Title = title,
            Category = category,
            ProjectId = projectId,
            UploadedByUserId = uploaderId,
            UploadedDate = now,
            LastModifiedDate = now,
            OriginalFileName = $"test{extension}",
            FileSizeBytes = bytes?.Length ?? 1024,
            FileType = fileType,
            FilePath = key
        };

        foreach (var tag in (tags ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            document.Tags.Add(new DocumentTag { Tag = tag });
        }

        context.Documents.Add(document);
        await context.SaveChangesAsync();
        return document;
    }

    public async Task<DocumentShare> AddUserShareAsync(int documentId, int sharedByUserId, int sharedWithUserId)
    {
        await using var context = _fixture.CreateContext();
        var share = new DocumentShare
        {
            DocumentId = documentId,
            SharedByUserId = sharedByUserId,
            SharedWithUserId = sharedWithUserId,
            SharedDate = DateTime.UtcNow
        };
        context.DocumentShares.Add(share);
        await context.SaveChangesAsync();
        return share;
    }

    public async Task<DocumentShare> AddDepartmentShareAsync(int documentId, int sharedByUserId, string department)
    {
        await using var context = _fixture.CreateContext();
        var share = new DocumentShare
        {
            DocumentId = documentId,
            SharedByUserId = sharedByUserId,
            SharedWithDepartment = department,
            SharedDate = DateTime.UtcNow
        };
        context.DocumentShares.Add(share);
        await context.SaveChangesAsync();
        return share;
    }

    public async Task<TaskDocument> AddTaskAttachmentAsync(int taskId, int documentId, int attachedByUserId)
    {
        await using var context = _fixture.CreateContext();
        var attachment = new TaskDocument
        {
            TaskId = taskId,
            DocumentId = documentId,
            AttachedByUserId = attachedByUserId,
            AttachedDate = DateTime.UtcNow
        };
        context.TaskDocuments.Add(attachment);
        await context.SaveChangesAsync();
        return attachment;
    }

    /// <summary>Creates a throwaway user so tests can change department and role without mutating seed users.</summary>
    public async Task<User> AddUserAsync(UserRole role, string? department, bool inAppNotificationsEnabled = true)
    {
        await using var context = _fixture.CreateContext();
        var id = Guid.NewGuid().ToString("N");
        var user = new User
        {
            Email = $"test.{id}@contoso.com",
            DisplayName = $"Test User {id[..8]}",
            Department = department,
            JobTitle = "Test",
            Role = role,
            InAppNotificationsEnabled = inAppNotificationsEnabled
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    /// <summary>Creates a throwaway task, for example a Project 1 task assigned to a non-member.</summary>
    public async Task<TaskItem> AddTaskAsync(int? projectId, int assignedUserId, int createdByUserId, string title)
    {
        await using var context = _fixture.CreateContext();
        var task = new TaskItem
        {
            Title = title,
            ProjectId = projectId,
            AssignedUserId = assignedUserId,
            CreatedByUserId = createdByUserId
        };
        context.Tasks.Add(task);
        await context.SaveChangesAsync();
        return task;
    }

    /// <summary>Creates a throwaway project (extra helper, used to prove PM authority comes from ProjectManagerId).</summary>
    public async Task<Project> AddProjectAsync(int projectManagerId, string name)
    {
        await using var context = _fixture.CreateContext();
        var project = new Project
        {
            Name = name,
            ProjectManagerId = projectManagerId,
            Status = ProjectStatus.Active
        };
        context.Projects.Add(project);
        await context.SaveChangesAsync();
        return project;
    }

    /// <summary>Adds a project membership (extra helper).</summary>
    public async Task<ProjectMember> AddProjectMemberAsync(int projectId, int userId)
    {
        await using var context = _fixture.CreateContext();
        var member = new ProjectMember { ProjectId = projectId, UserId = userId, Role = "Developer" };
        context.ProjectMembers.Add(member);
        await context.SaveChangesAsync();
        return member;
    }

    /// <summary>
    /// Inserts <paramref name="count"/> documents for one owner with mixed categories, projects (none, 1, 2),
    /// upload dates over the last year, and tags. Returns the new document IDs.
    /// </summary>
    public async Task<IReadOnlyList<int>> SeedBulkDocumentsAsync(int ownerId, int count = 500)
    {
        await using var context = _fixture.CreateContext();
        var batch = Guid.NewGuid().ToString("N")[..8];
        var now = DateTime.UtcNow;
        var documents = new List<Document>(count);

        for (var i = 0; i < count; i++)
        {
            int? projectId = (i % 3) switch { 1 => 1, 2 => 2, _ => null };
            var uploaded = now.AddDays(-(i % 365)).AddMinutes(-i);
            var document = new Document
            {
                Title = $"Bulk {batch} document {i:D4}",
                Description = $"Bulk document {i} for performance tests",
                Category = DocumentCategories.All[i % DocumentCategories.All.Count],
                ProjectId = projectId,
                UploadedByUserId = ownerId,
                UploadedDate = uploaded,
                LastModifiedDate = uploaded,
                OriginalFileName = $"bulk-{i:D4}.pdf",
                FileSizeBytes = 1024 + i,
                FileType = "application/pdf",
                FilePath = $"{ownerId}/{(projectId?.ToString() ?? "personal")}/{Guid.NewGuid():N}.pdf"
            };
            document.Tags.Add(new DocumentTag { Tag = $"tag{i % 7}" });
            document.Tags.Add(new DocumentTag { Tag = $"bulk{batch}" });
            documents.Add(document);
        }

        context.Documents.AddRange(documents);
        await context.SaveChangesAsync();
        return documents.Select(d => d.DocumentId).ToList();
    }

    private static string ExtensionFor(string fileType) => fileType switch
    {
        "application/pdf" => ".pdf",
        "application/msword" => ".doc",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
        "application/vnd.ms-excel" => ".xls",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
        "application/vnd.ms-powerpoint" => ".ppt",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation" => ".pptx",
        "text/plain" => ".txt",
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        _ => ".bin"
    };
}
