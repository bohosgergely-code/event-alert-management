using EventAlertManagement.Core.Contracts;
using EventAlertManagement.Core.Domain;
using EventAlertManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventAlertManagement.Infrastructure.Dispatch;

/// <summary>Manual retry used by admins. Only failed notifications can be retried.</summary>
public sealed class NotificationRetryService(AppDbContext db, IClock clock)
{
    public async Task<bool> RetryAsync(Guid notificationId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var rows = await db.Notifications
            .Where(n => n.Id == notificationId && n.Status == NotificationStatus.Failed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.Status, NotificationStatus.Pending)
                .SetProperty(n => n.AttemptCount, 0)
                .SetProperty(n => n.NextAttemptAt, now), ct);
        return rows == 1;
    }
}
