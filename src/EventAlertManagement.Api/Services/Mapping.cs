using System.Text.Json;
using EventAlertManagement.Api.Dtos;
using EventAlertManagement.Core.Contracts;
using EventAlertManagement.Core.Domain;
using EventAlertManagement.Infrastructure.Security;

namespace EventAlertManagement.Api.Services;

public static class Mapping
{
    public static JsonElement ParseJson(string? json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        return doc.RootElement.Clone();
    }

    /// <summary>Channel settings are never returned. Only the channel's own masked summary is.</summary>
    public static ChannelResponse ToResponse(ChannelConfig c, IChannelRegistry registry, ISettingsProtector protector)
    {
        string masked;
        try
        {
            var channel = registry.Find(c.Type);
            masked = channel is null ? "(unknown channel type)" : channel.MaskSettings(protector.Unprotect(c.SettingsProtected));
        }
        catch (Exception) { masked = "(unreadable)"; }
        return new ChannelResponse(c.Id, c.Type, c.Name, c.IsEnabled, masked, c.LastTestedAt, c.LastTestResult);
    }

    public static RuleResponse ToResponse(AlertRule r) =>
        new(r.Id, r.Name, r.Category, r.MinSeverity, ParseJson(r.FiltersJson), r.CooldownSeconds, r.IsEnabled,
            r.LastNotifiedAt, r.Channels.Select(c => c.Id).ToList());

    public static EventSummary ToSummary(Event e) =>
        new(e.Id, e.Category, e.Severity, e.Title, e.Summary, e.Url, e.Location, e.OccurredAt, e.SourceKey, e.IsSimulated);

    public static string Health(Source s) =>
        !s.IsEnabled ? "Disabled"
        : s.LastPollAt is null ? "NeverPolled"
        : s.ConsecutiveFailures >= 3 ? "Failing"
        : s.ConsecutiveFailures > 0 ? "Degraded"
        : "Healthy";

    public static SourceResponse ToResponse(Source s) =>
        new(s.Id, s.Key, s.Name, s.Category, s.IsEnabled, s.PollIntervalSeconds, s.LastPollAt, s.LastSuccessAt,
            s.LastError, s.ConsecutiveFailures, Health(s));

    public static AdminNotificationResponse ToAdmin(Notification n) =>
        new(n.Id, n.Status, n.SuppressedReason, n.CreatedAt, n.SentAt, n.AttemptCount, n.NextAttemptAt,
            n.RuleId, n.Rule?.Name ?? "", n.Rule?.User?.Email ?? "", n.ChannelConfigId,
            n.ChannelConfig?.Type ?? "", n.ChannelConfig?.Name ?? "", n.EventId, n.Event?.Title ?? "",
            n.Attempts.OrderBy(a => a.AttemptNumber)
                .Select(a => new AttemptResponse(a.AttemptNumber, a.StartedAt, a.DurationMs, a.Outcome, a.ErrorMessage)).ToList());
}
