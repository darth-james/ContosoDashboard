using Microsoft.EntityFrameworkCore;
using ContosoDashboard.Data;
using ContosoDashboard.Models;

namespace ContosoDashboard.Services;

public interface INotificationService
{
    Task<List<Notification>> GetUserNotificationsAsync(int userId, bool unreadOnly = false);
    Task<Notification> CreateNotificationAsync(Notification notification);
    Task<bool> MarkAsReadAsync(int notificationId, int requestingUserId);
    Task<int> GetUnreadCountAsync(int userId);
    Task<int> CreateNotificationsAsync(IEnumerable<Notification> notifications);
}

public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _context;

    public NotificationService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<Notification>> GetUserNotificationsAsync(int userId, bool unreadOnly = false)
    {
        var query = _context.Notifications
            .Where(n => n.UserId == userId);

        if (unreadOnly)
            query = query.Where(n => !n.IsRead);

        return await query
            .OrderByDescending(n => n.Priority)
            .ThenByDescending(n => n.CreatedDate)
            .Take(50)
            .ToListAsync();
    }

    public async Task<Notification> CreateNotificationAsync(Notification notification)
    {
        notification.CreatedDate = DateTime.UtcNow;
        notification.IsRead = false;

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        return notification;
    }

    public async Task<bool> MarkAsReadAsync(int notificationId, int requestingUserId)
    {
        var notification = await _context.Notifications.FindAsync(notificationId);
        if (notification == null) return false;

        // Authorization: Users can only mark their own notifications as read
        if (notification.UserId != requestingUserId)
        {
            return false; // User not authorized to mark this notification as read
        }

        notification.IsRead = true;
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<int> GetUnreadCountAsync(int userId)
    {
        return await _context.Notifications
            .CountAsync(n => n.UserId == userId && !n.IsRead);
    }

    // Batch insert for document notifications (R-12): one query for the recipients' in-app preference,
    // one SaveChangesAsync(). Recipients with in-app notifications disabled (or unknown users) are skipped.
    public async Task<int> CreateNotificationsAsync(IEnumerable<Notification> notifications)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        var pending = notifications.ToList();
        if (pending.Count == 0)
        {
            return 0;
        }

        var recipientIds = pending.Select(n => n.UserId).Distinct().ToList();
        var enabledRecipients = (await _context.Users
                .Where(u => recipientIds.Contains(u.UserId) && u.InAppNotificationsEnabled)
                .Select(u => u.UserId)
                .ToListAsync())
            .ToHashSet();

        var toInsert = pending.Where(n => enabledRecipients.Contains(n.UserId)).ToList();
        if (toInsert.Count == 0)
        {
            return 0;
        }

        var now = DateTime.UtcNow;
        foreach (var notification in toInsert)
        {
            notification.CreatedDate = now;
            notification.IsRead = false;
        }

        _context.Notifications.AddRange(toInsert);
        await _context.SaveChangesAsync();

        return toInsert.Count;
    }
}
