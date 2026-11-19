using EventAlertManagement.Api.Auth;
using EventAlertManagement.Api.Dtos;
using EventAlertManagement.Core.Domain;
using EventAlertManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventAlertManagement.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize(Roles = Roles.User)]
public class NotificationsController(AppDbContext db) : ControllerBase
{
    /// <summary>The signed-in user's own notification history, newest first.</summary>
    [HttpGet]
    public async Task<PagedResult<NotificationResponse>> List(
        NotificationStatus? status, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        var uid = User.UserId();

        var query = db.Notifications.AsNoTracking().Where(n => n.Rule!.UserId == uid);
        if (status is not null) query = query.Where(n => n.Status == status);

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(n => new NotificationResponse(
                n.Id, n.Status, n.SuppressedReason, n.CreatedAt, n.SentAt, n.AttemptCount, n.NextAttemptAt,
                n.Attempts.OrderByDescending(a => a.AttemptNumber).Select(a => a.ErrorMessage).FirstOrDefault(),
                n.RuleId, n.Rule!.Name, n.ChannelConfigId, n.ChannelConfig!.Name, n.ChannelConfig.Type,
                n.EventId, n.Event!.Title, n.Event.Severity))
            .ToListAsync(ct);
        return new PagedResult<NotificationResponse>(items, total, page, pageSize);
    }
}

public static class Paging
{
    public static (int Page, int PageSize) Clamp(int page, int pageSize) =>
        (Math.Max(page, 1), Math.Clamp(pageSize, 1, 100));
}
