using EventAlertManagement.Api.Auth;
using EventAlertManagement.Api.Dtos;
using EventAlertManagement.Api.Services;
using EventAlertManagement.Core.Contracts;
using EventAlertManagement.Core.Domain;
using EventAlertManagement.Infrastructure.Dispatch;
using EventAlertManagement.Infrastructure.Persistence;
using EventAlertManagement.Infrastructure.Pipeline;
using EventAlertManagement.Infrastructure.Sources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventAlertManagement.Api.Controllers;

/// <summary>Operating the pipeline. The most useful question it answers: "why did this alert not arrive?"</summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = Roles.Admin)]
public class AdminController(
    AppDbContext db, IChannelRegistry registry, EventIngestionService ingestion,
    SourcePollService poller, NotificationRetryService retry, IClock clock) : ControllerBase
{
    private IQueryable<Notification> NotificationsWithDetails() =>
        db.Notifications.AsNoTracking().AsSplitQuery()
            .Include(n => n.Rule).ThenInclude(r => r!.User)
            .Include(n => n.ChannelConfig).Include(n => n.Event).Include(n => n.Attempts);

    [HttpGet("overview")]
    public async Task<OverviewResponse> Overview(CancellationToken ct)
    {
        var since = clock.UtcNow.AddHours(-24);
        var byStatus = await db.Notifications.Where(n => n.CreatedAt >= since)
            .GroupBy(n => n.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var failures = await NotificationsWithDetails().Where(n => n.Status == NotificationStatus.Failed)
            .OrderByDescending(n => n.CreatedAt).Take(5).ToListAsync(ct);
        var sources = await db.Sources.AsNoTracking().ToListAsync(ct);

        return new OverviewResponse(
            await db.Users.CountAsync(ct), await db.AlertRules.CountAsync(ct), await db.Events.CountAsync(ct),
            byStatus.ToDictionary(x => x.Key.ToString(), x => x.Count),
            await db.Notifications.CountAsync(n => n.Status == NotificationStatus.Failed, ct),
            sources.Count(s => Mapping.Health(s) is "Failing" or "Degraded"),
            failures.Select(Mapping.ToAdmin).ToList());
    }

    // ---- events ----

    [HttpGet("events")]
    public async Task<PagedResult<EventSummary>> Events(
        EventCategory? category, string? source, bool? simulated, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        var q = db.Events.AsNoTracking().AsQueryable();
        if (category is not null) q = q.Where(e => e.Category == category);
        if (!string.IsNullOrWhiteSpace(source)) q = q.Where(e => e.SourceKey == source);
        if (simulated is not null) q = q.Where(e => e.IsSimulated == simulated);

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(e => e.OccurredAt).ThenBy(e => e.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<EventSummary>(items.Select(Mapping.ToSummary).ToList(), total, page, pageSize);
    }

    /// <summary>Creates a simulated event that goes through the real pipeline (matching, notifications, delivery).</summary>
    [HttpPost("events/inject")]
    public async Task<ActionResult<InjectEventResponse>> Inject(InjectEventRequest request, CancellationToken ct)
    {
        if (request.Url is { Length: > 0 } && !(Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["url"] = new[] { "The link must be an http or https address." } }));
        if (!Enum.IsDefined(request.Category))
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["category"] = new[] { "Category is not valid." } }));

        var sim = await db.Sources.FirstOrDefaultAsync(s => s.Key == SimulatorSource.SourceKey, ct);
        if (sim is null) return Problem("The simulator source is missing.", statusCode: 500);

        var now = clock.UtcNow;
        var ev = new NormalizedEvent(
            ExternalId: $"inject-{Guid.NewGuid():N}", Category: request.Category, Severity: request.Severity,
            Title: request.Title.Trim(), Summary: request.Summary?.Trim() ?? "", Url: string.IsNullOrWhiteSpace(request.Url) ? null : request.Url,
            Location: request.Location, OccurredAt: now, IsSimulated: true,
            Attributes: new EventAttributes
            {
                Magnitude = request.Magnitude, EventType = request.EventType,
                Ticker = request.Ticker?.Trim().ToUpperInvariant(), ChangePercent = request.ChangePercent,
            });

        var result = await ingestion.IngestAsync(sim, new[] { ev }, null, ct);
        return new InjectEventResponse(result.NewEvents, result.NotificationsCreated, result.NotificationsSuppressed);
    }

    // ---- notifications ----

    [HttpGet("notifications")]
    public async Task<PagedResult<AdminNotificationResponse>> Notifications(
        NotificationStatus? status, string? userEmail, string? channelType, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        var q = NotificationsWithDetails();
        if (status is not null) q = q.Where(n => n.Status == status);
        if (!string.IsNullOrWhiteSpace(userEmail)) q = q.Where(n => n.Rule!.User!.Email == userEmail.Trim().ToLower());
        if (!string.IsNullOrWhiteSpace(channelType)) q = q.Where(n => n.ChannelConfig!.Type == channelType);

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<AdminNotificationResponse>(items.Select(Mapping.ToAdmin).ToList(), total, page, pageSize);
    }

    [HttpPost("notifications/{id:guid}/retry")]
    public async Task<IActionResult> Retry(Guid id, CancellationToken ct)
    {
        if (!await db.Notifications.AnyAsync(n => n.Id == id, ct)) return NotFound();
        return await retry.RetryAsync(id, ct)
            ? NoContent()
            : Conflict(new ProblemDetails { Title = "Only failed notifications can be retried.", Status = 409 });
    }

    // ---- read-only lists ----

    [HttpGet("rules")]
    public async Task<List<AdminRuleResponse>> Rules(CancellationToken ct) =>
        await db.AlertRules.AsNoTracking().OrderBy(r => r.User!.Email).ThenBy(r => r.Name)
            .Select(r => new AdminRuleResponse(r.Id, r.Name, r.User!.Email, r.Category, r.MinSeverity, r.IsEnabled,
                r.CooldownSeconds, r.LastNotifiedAt, r.Channels.Count)).Take(500).ToListAsync(ct);

    [HttpGet("users")]
    public async Task<List<AdminUserResponse>> Users(CancellationToken ct) =>
        await db.Users.AsNoTracking().OrderBy(u => u.Email)
            .Select(u => new AdminUserResponse(u.Id, u.Email, u.CreatedAt,
                db.AlertRules.Count(r => r.UserId == u.Id), db.ChannelConfigs.Count(c => c.UserId == u.Id))).Take(500).ToListAsync(ct);

    // ---- sources ----

    [HttpGet("sources")]
    public async Task<List<SourceResponse>> Sources(CancellationToken ct) =>
        (await db.Sources.AsNoTracking().OrderBy(s => s.Key).ToListAsync(ct)).Select(Mapping.ToResponse).ToList();

    [HttpPut("sources/{id:guid}")]
    public async Task<ActionResult<SourceResponse>> UpdateSource(Guid id, UpdateSourceRequest request, CancellationToken ct)
    {
        var source = await db.Sources.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (source is null) return NotFound();
        if (request.IsEnabled is not null) source.IsEnabled = request.IsEnabled.Value;
        if (request.PollIntervalSeconds is not null) source.PollIntervalSeconds = request.PollIntervalSeconds.Value;
        await db.SaveChangesAsync(ct);
        return Mapping.ToResponse(source);
    }

    [HttpPost("sources/{id:guid}/poll")]
    public async Task<ActionResult<PollResponse>> PollNow(Guid id, CancellationToken ct)
    {
        if (!await db.Sources.AnyAsync(s => s.Id == id, ct)) return NotFound();
        var r = await poller.PollAsync(id, ct);
        return new PollResponse(r.Success, r.NewEvents, r.Error);
    }

    // ---- channel types ----

    [HttpGet("channel-types")]
    public async Task<List<ChannelTypeResponse>> ChannelTypes(CancellationToken ct)
    {
        var settings = await db.ChannelTypeSettings.AsNoTracking().ToListAsync(ct);
        var names = registry.Types.Concat(settings.Select(s => s.Type)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t);
        return names.Select(t => new ChannelTypeResponse(
            registry.Find(t)?.Type ?? t,
            settings.FirstOrDefault(s => string.Equals(s.Type, t, StringComparison.OrdinalIgnoreCase))?.IsEnabled ?? true,
            registry.Find(t) is not null)).ToList();
    }

    [HttpPut("channel-types/{type}")]
    public async Task<ActionResult<ChannelTypeResponse>> SetChannelType(string type, UpdateChannelTypeRequest request, CancellationToken ct)
    {
        var channel = registry.Find(type);
        if (channel is null) return NotFound(); // Only registered types can be switched, so names cannot differ by letter case.

        var row = await db.ChannelTypeSettings.FirstOrDefaultAsync(t => t.Type == channel.Type, ct);
        if (row is null) db.ChannelTypeSettings.Add(row = new ChannelTypeSetting { Type = channel.Type });
        row.IsEnabled = request.IsEnabled;
        row.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return new ChannelTypeResponse(channel.Type, row.IsEnabled, true);
    }
}
