using System.Text.Json;
using EventAlertManagement.Api.Auth;
using EventAlertManagement.Api.Dtos;
using EventAlertManagement.Api.Services;
using EventAlertManagement.Core.Contracts;
using EventAlertManagement.Core.Domain;
using EventAlertManagement.Infrastructure.Persistence;
using EventAlertManagement.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventAlertManagement.Api.Controllers;

[ApiController]
[Route("api/channels")]
[Authorize(Roles = Roles.User)]
public class ChannelsController(
    AppDbContext db, IChannelRegistry registry, ISettingsProtector protector, IClock clock) : ControllerBase
{
    public const int MaxChannelsPerUser = 20;
    private const int MaxSettingsLength = 4000;

    // A foreign channel is reported as "not found", never as "forbidden": its existence is not revealed.
    private Task<ChannelConfig?> Own(Guid id, CancellationToken ct) =>
        db.ChannelConfigs.FirstOrDefaultAsync(c => c.Id == id && c.UserId == User.UserId(), ct);

    [HttpGet]
    public async Task<List<ChannelResponse>> List(CancellationToken ct)
    {
        var uid = User.UserId();
        var channels = await db.ChannelConfigs.AsNoTracking().Where(c => c.UserId == uid).OrderBy(c => c.Name).ToListAsync(ct);
        return channels.Select(c => Mapping.ToResponse(c, registry, protector)).ToList();
    }

    [HttpGet("types")]
    public async Task<List<string>> Types(CancellationToken ct)
    {
        var off = (await db.ChannelTypeSettings.Where(t => !t.IsEnabled).Select(t => t.Type).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return registry.Types.Where(t => !off.Contains(t)).OrderBy(t => t).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ChannelResponse>> Get(Guid id, CancellationToken ct)
    {
        var c = await Own(id, ct);
        return c is null ? NotFound() : Mapping.ToResponse(c, registry, protector);
    }

    [HttpPost]
    public async Task<ActionResult<ChannelResponse>> Create(CreateChannelRequest request, CancellationToken ct)
    {
        var channel = registry.Find(request.Type);
        if (channel is null) return Invalid("type", $"Unknown channel type. Available: {string.Join(", ", registry.Types.OrderBy(t => t))}.");

        var problems = CheckSettings(channel, request.Settings, out var settingsJson);
        if (problems is not null) return Invalid("settings", problems);

        var uid = User.UserId();
        if (await db.ChannelConfigs.CountAsync(c => c.UserId == uid, ct) >= MaxChannelsPerUser)
            return Invalid("type", $"You can have at most {MaxChannelsPerUser} channels.");

        var entity = new ChannelConfig
        {
            UserId = uid, Type = channel.Type, Name = request.Name.Trim(), IsEnabled = request.IsEnabled,
            SettingsProtected = protector.Protect(settingsJson!),
        };
        db.ChannelConfigs.Add(entity);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = entity.Id }, Mapping.ToResponse(entity, registry, protector));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ChannelResponse>> Update(Guid id, UpdateChannelRequest request, CancellationToken ct)
    {
        var entity = await Own(id, ct);
        if (entity is null) return NotFound();

        if (request.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Name)) return Invalid("name", "Name cannot be empty.");
            entity.Name = request.Name.Trim();
        }
        if (request.IsEnabled is not null) entity.IsEnabled = request.IsEnabled.Value;
        if (request.Settings is { } settings && settings.ValueKind != JsonValueKind.Undefined && settings.ValueKind != JsonValueKind.Null)
        {
            var channel = registry.Find(entity.Type);
            if (channel is null) return Invalid("settings", "This channel type is no longer available.");
            var problems = CheckSettings(channel, settings, out var settingsJson);
            if (problems is not null) return Invalid("settings", problems);
            entity.SettingsProtected = protector.Protect(settingsJson!);
            entity.LastTestedAt = null; entity.LastTestResult = null; // The old test result no longer applies.
        }
        await db.SaveChangesAsync(ct);
        return Mapping.ToResponse(entity, registry, protector);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var entity = await Own(id, ct);
        if (entity is null) return NotFound();

        // Deleting silently would leave rules that can never notify anyone.
        if (await db.AlertRules.AnyAsync(r => r.Channels.Any(c => c.Id == id), ct))
            return Conflict(new ProblemDetails { Title = "This channel is used by one or more rules. Remove it from those rules first.", Status = 409 });

        db.ChannelConfigs.Remove(entity);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Sends one real test message through the channel and reports the real result.</summary>
    [HttpPost("{id:guid}/test")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("test")]
    public async Task<ActionResult<TestResultResponse>> Test(Guid id, CancellationToken ct)
    {
        var entity = await Own(id, ct);
        if (entity is null) return NotFound();

        // Claim the right to test with ONE atomic update. Reading the time first and writing it after the send let
        // parallel requests all pass the check, so one user could make the server send many messages to a third party.
        var now = clock.UtcNow;
        var cutoff = now.AddSeconds(-10);
        var uid = User.UserId();
        var claimed = await db.ChannelConfigs
            .Where(c => c.Id == id && c.UserId == uid && (c.LastTestedAt == null || c.LastTestedAt <= cutoff))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastTestedAt, now), ct);
        if (claimed == 0)
            return StatusCode(429, new ProblemDetails { Title = "Please wait a few seconds between tests.", Status = 429 });

        var channel = registry.Find(entity.Type);
        ChannelSendResult result;
        if (channel is null) result = ChannelSendResult.Permanent("This channel type is not available.");
        else if (await TypeSwitchedOff(entity.Type, ct)) result = ChannelSendResult.Permanent("This channel type is switched off by an administrator.");
        else
        {
            try
            {
                var evt = new Event
                {
                    Title = "Test message from EventAlertManagement",
                    Summary = "If you can read this, the channel works.",
                    OccurredAt = now, IsSimulated = true,
                };
                result = await channel.SendAsync(new NotificationMessage(evt, "Channel test"), protector.Unprotect(entity.SettingsProtected), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { result = ChannelSendResult.Transient($"Unexpected error: {ex.GetType().Name}"); }
        }

        var error = result.Outcome == ChannelSendOutcome.Success ? null : ErrorSanitizer.Sanitize(result.Error);
        await db.ChannelConfigs.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.LastTestResult, error ?? "OK"), ct);
        return new TestResultResponse(result.Outcome == ChannelSendOutcome.Success, result.Outcome.ToString(), error);
    }

    private async Task<bool> TypeSwitchedOff(string type, CancellationToken ct) =>
        (await db.ChannelTypeSettings.Where(t => !t.IsEnabled).Select(t => t.Type).ToListAsync(ct)).Contains(type, StringComparer.OrdinalIgnoreCase);

    private static string? CheckSettings(INotificationChannel channel, JsonElement settings, out string? json)
    {
        json = null;
        if (settings.ValueKind != JsonValueKind.Object) return "Settings must be a JSON object.";
        var raw = settings.GetRawText();
        if (raw.Length > MaxSettingsLength) return "Settings are too large.";
        var problems = channel.ValidateSettings(raw);
        if (problems.Count > 0) return string.Join(" ", problems);
        json = raw;
        return null;
    }

    private ActionResult Invalid(string field, string message) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = new[] { message } }));
}
