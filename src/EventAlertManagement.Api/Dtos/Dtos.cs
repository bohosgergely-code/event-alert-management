using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using EventAlertManagement.Core.Domain;

namespace EventAlertManagement.Api.Dtos;

// Entities are never returned directly: DTOs make sure secrets and internals cannot leak by accident.

public sealed record LoginRequest([Required, StringLength(320)] string Email);
public sealed record AdminLoginRequest([Required, StringLength(200)] string Key);
public sealed record TokenResponse(string Token, DateTime ExpiresAt, string Role);
public sealed record MeResponse(Guid Id, string Email, string DisplayName, string Role);

public sealed record ChannelResponse(
    Guid Id, string Type, string Name, bool IsEnabled, string SettingsMasked,
    DateTime? LastTestedAt, string? LastTestResult);

public sealed record CreateChannelRequest(
    [Required, StringLength(50)] string Type,
    [Required, StringLength(100)] string Name,
    [Required] JsonElement Settings,
    bool IsEnabled = true);

/// <summary>Every field is optional. Omitted fields keep their current value.</summary>
public sealed record UpdateChannelRequest(
    [StringLength(100)] string? Name, JsonElement? Settings, bool? IsEnabled);

public sealed record TestResultResponse(bool Success, string Outcome, string? Error);

public sealed record RuleRequest(
    [Required, StringLength(100)] string Name,
    EventCategory Category,
    int MinSeverity,
    JsonElement? Filters,
    int CooldownSeconds,
    bool IsEnabled,
    [Required] List<Guid> ChannelIds);

public sealed record RuleResponse(
    Guid Id, string Name, EventCategory Category, int MinSeverity, JsonElement Filters,
    int CooldownSeconds, bool IsEnabled, DateTime? LastNotifiedAt, List<Guid> ChannelIds);

public sealed record PreviewResponse(int EventsChecked, int Matches, List<EventSummary> Samples);

public sealed record EventSummary(
    Guid Id, EventCategory Category, int Severity, string Title, string Summary, string? Url,
    string? Location, DateTime OccurredAt, string SourceKey, bool IsSimulated);

public sealed record PresetResponse(string Name, string Description, RuleRequestTemplate Template);
public sealed record RuleRequestTemplate(EventCategory Category, int MinSeverity, JsonElement Filters, int CooldownSeconds);

public sealed record NotificationResponse(
    Guid Id, NotificationStatus Status, string? SuppressedReason, DateTime CreatedAt, DateTime? SentAt,
    int AttemptCount, DateTime? NextAttemptAt, string? LastError,
    Guid RuleId, string RuleName, Guid ChannelId, string ChannelName, string ChannelType,
    Guid EventId, string EventTitle, int EventSeverity);

public sealed record PagedResult<T>(List<T> Items, int Total, int Page, int PageSize);

// ---- admin ----

public sealed record InjectEventRequest(
    EventCategory Category,
    [Range(1, 5)] int Severity,
    [Required, StringLength(300)] string Title,
    [StringLength(2000)] string? Summary,
    [StringLength(2000)] string? Url,
    [StringLength(200)] string? Location,
    double? Magnitude, string? EventType, string? Ticker, double? ChangePercent);

public sealed record InjectEventResponse(int NewEvents, int NotificationsCreated, int NotificationsSuppressed);

public sealed record AdminNotificationResponse(
    Guid Id, NotificationStatus Status, string? SuppressedReason, DateTime CreatedAt, DateTime? SentAt,
    int AttemptCount, DateTime? NextAttemptAt,
    Guid RuleId, string RuleName, string UserEmail, Guid ChannelId, string ChannelType, string ChannelName,
    Guid EventId, string EventTitle, List<AttemptResponse> Attempts);

public sealed record AttemptResponse(int AttemptNumber, DateTime StartedAt, int DurationMs, AttemptOutcome Outcome, string? ErrorMessage);

public sealed record AdminUserResponse(Guid Id, string Email, DateTime CreatedAt, int Rules, int Channels);
public sealed record AdminRuleResponse(Guid Id, string Name, string UserEmail, EventCategory Category, int MinSeverity, bool IsEnabled, int CooldownSeconds, DateTime? LastNotifiedAt, int ChannelCount);

public sealed record SourceResponse(
    Guid Id, string Key, string Name, EventCategory? Category, bool IsEnabled, int PollIntervalSeconds,
    DateTime? LastPollAt, DateTime? LastSuccessAt, string? LastError, int ConsecutiveFailures, string Health);

public sealed record UpdateSourceRequest(bool? IsEnabled, [Range(10, 86400)] int? PollIntervalSeconds);
public sealed record PollResponse(bool Success, int NewEvents, string? Error);

public sealed record ChannelTypeResponse(string Type, bool IsEnabled, bool IsRegistered);
public sealed record UpdateChannelTypeRequest(bool IsEnabled);

public sealed record OverviewResponse(
    int Users, int Rules, int Events, Dictionary<string, int> NotificationsLast24h,
    int FailedNotifications, int UnhealthySources, List<AdminNotificationResponse> RecentFailures);
