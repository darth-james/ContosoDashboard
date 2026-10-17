using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ContosoDashboard.Tests;

/// <summary>
/// Tests for NotificationService (T030). CreateNotificationsAsync is stubbed until T035, so its tests fail at
/// runtime (red); the tests of existing behavior may pass already.
/// </summary>
[Collection("LocalDb")]
[Trait("Story", "Foundation")]
public class NotificationServiceTests
{
    private readonly LocalDbFixture _fixture;
    private readonly DocumentTestData _data;

    public NotificationServiceTests(LocalDbFixture fixture)
    {
        _fixture = fixture;
        _data = new DocumentTestData(fixture);
    }

    private static Notification NewNotification(int userId, string title, NotificationType type = NotificationType.SystemAnnouncement, int? documentId = null)
    {
        return new Notification
        {
            UserId = userId,
            Title = title,
            Message = "Test message",
            Type = type,
            Priority = NotificationPriority.Informational,
            DocumentId = documentId
        };
    }

    [Fact]
    public async Task CreateNotificationsAsync_InsertsAllNotificationsInOneSaveAndReturnsTheCount()
    {
        var title = $"Batch {Guid.NewGuid():N}";
        var saves = new SaveCountingInterceptor();
        await using var context = _fixture.CreateContext(saves);
        var service = TestServiceFactory.CreateNotificationService(context);

        var inserted = await service.CreateNotificationsAsync(new[]
        {
            NewNotification(4, title),
            NewNotification(5, title),
            NewNotification(6, title)
        });

        Assert.Equal(3, inserted);
        Assert.Equal(1, saves.SaveCount);

        await using var verify = _fixture.CreateContext();
        Assert.Equal(3, await verify.Notifications.CountAsync(n => n.Title == title));
    }

    [Fact]
    public async Task CreateNotificationsAsync_SkipsRecipientsWithInAppNotificationsDisabled()
    {
        var title = $"Preference {Guid.NewGuid():N}";
        var disabled = await _data.AddUserAsync(UserRole.Employee, "Engineering", inAppNotificationsEnabled: false);
        await using var context = _fixture.CreateContext();
        var service = TestServiceFactory.CreateNotificationService(context);

        var inserted = await service.CreateNotificationsAsync(new[]
        {
            NewNotification(4, title),
            NewNotification(disabled.UserId, title),
            NewNotification(5, title)
        });

        Assert.Equal(2, inserted);

        await using var verify = _fixture.CreateContext();
        Assert.Equal(0, await verify.Notifications.CountAsync(n => n.Title == title && n.UserId == disabled.UserId));
        Assert.Equal(2, await verify.Notifications.CountAsync(n => n.Title == title));
    }

    [Fact]
    public async Task CreateNotificationsAsync_PersistsDocumentIdAndTheNewNotificationTypes()
    {
        var title = $"Types {Guid.NewGuid():N}";
        var document = await _data.AddDocumentAsync(4, null, $"Notified doc {Guid.NewGuid():N}", "Reports");
        await using var context = _fixture.CreateContext();
        var service = TestServiceFactory.CreateNotificationService(context);

        var inserted = await service.CreateNotificationsAsync(new[]
        {
            NewNotification(5, title, NotificationType.DocumentShared, document.DocumentId),
            NewNotification(6, title, NotificationType.DocumentAdded, document.DocumentId)
        });

        Assert.Equal(2, inserted);

        await using var verify = _fixture.CreateContext();
        var stored = await verify.Notifications.Where(n => n.Title == title).OrderBy(n => n.UserId).ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.All(stored, n => Assert.Equal(document.DocumentId, n.DocumentId));
        Assert.Equal(NotificationType.DocumentShared, stored[0].Type);
        Assert.Equal(NotificationType.DocumentAdded, stored[1].Type);
    }

    [Fact]
    public async Task CreateNotificationsAsync_SetsCreatedDateToUtcAndLeavesThemUnread()
    {
        var title = $"Dates {Guid.NewGuid():N}";
        var before = DateTime.UtcNow.AddSeconds(-5);
        await using var context = _fixture.CreateContext();
        var service = TestServiceFactory.CreateNotificationService(context);

        var notification = NewNotification(4, title);
        notification.CreatedDate = DateTime.UtcNow.AddYears(-1);
        await service.CreateNotificationsAsync(new[] { notification });

        await using var verify = _fixture.CreateContext();
        var stored = await verify.Notifications.SingleAsync(n => n.Title == title);
        Assert.InRange(stored.CreatedDate, before, DateTime.UtcNow.AddSeconds(5));
        Assert.False(stored.IsRead);
    }

    [Fact]
    public async Task CreateNotificationsAsync_WithNoNotificationsInsertsNothing()
    {
        await using var context = _fixture.CreateContext();
        var service = TestServiceFactory.CreateNotificationService(context);

        Assert.Equal(0, await service.CreateNotificationsAsync(Array.Empty<Notification>()));
    }

    // ---- Existing behavior (unchanged by this feature) ----

    [Fact]
    public async Task CreateNotificationAsync_ExistingBehaviorIsUnchanged()
    {
        var title = $"Single {Guid.NewGuid():N}";
        await using var context = _fixture.CreateContext();
        var service = TestServiceFactory.CreateNotificationService(context);

        var created = await service.CreateNotificationAsync(NewNotification(4, title, NotificationType.TaskAssignment));

        Assert.True(created.NotificationId > 0);
        Assert.False(created.IsRead);
        Assert.InRange(created.CreatedDate, DateTime.UtcNow.AddSeconds(-10), DateTime.UtcNow.AddSeconds(10));
        Assert.Null(created.DocumentId);
    }

    [Fact]
    public void NotificationType_ExistingIntegerValuesAreUnchangedAndNewOnesAreAppended()
    {
        Assert.Equal(0, (int)NotificationType.TaskAssignment);
        Assert.Equal(1, (int)NotificationType.TaskUpdate);
        Assert.Equal(2, (int)NotificationType.TaskDueSoon);
        Assert.Equal(3, (int)NotificationType.TaskCompleted);
        Assert.Equal(4, (int)NotificationType.TaskComment);
        Assert.Equal(5, (int)NotificationType.ProjectUpdate);
        Assert.Equal(6, (int)NotificationType.SystemAnnouncement);
        Assert.Equal(7, (int)NotificationType.DocumentShared);
        Assert.Equal(8, (int)NotificationType.DocumentAdded);
    }

    /// <summary>Counts how many times SaveChanges ran on the context.</summary>
    private sealed class SaveCountingInterceptor : SaveChangesInterceptor
    {
        public int SaveCount { get; private set; }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            SaveCount++;
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
