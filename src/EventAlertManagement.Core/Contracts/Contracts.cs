using EventAlertManagement.Core.Domain;

namespace EventAlertManagement.Core.Contracts;

public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>An event as produced by a source adapter, before it is stored.</summary>
public sealed record NormalizedEvent(
    string ExternalId,
    EventCategory Category,
    int Severity,
    string Title,
    string Summary,
    string? Url,
    string? Location,
    EventAttributes Attributes,
    DateTime OccurredAt,
    bool IsSimulated = false);

public sealed record SourceFetchResult(IReadOnlyList<NormalizedEvent> Events, string? NewCursor);

/// <summary>Fetches items from the outside world. Adapters never touch the database.</summary>
public interface IEventSource
{
    string Key { get; }
    /// <summary>Null when the source can produce several categories.</summary>
    EventCategory? Category { get; }
    Task<SourceFetchResult> FetchAsync(string? cursor, CancellationToken ct);
}

public enum ChannelSendOutcome
{
    Success,
    TransientFailure,
    PermanentFailure,
}

/// <summary>Result of one send. <see cref="Error"/> must be safe to store and show: no secrets.</summary>
public sealed record ChannelSendResult(ChannelSendOutcome Outcome, string? Error = null)
{
    public static ChannelSendResult Ok() => new(ChannelSendOutcome.Success);
    public static ChannelSendResult Transient(string error) => new(ChannelSendOutcome.TransientFailure, error);
    public static ChannelSendResult Permanent(string error) => new(ChannelSendOutcome.PermanentFailure, error);
}

/// <summary>What a channel needs to build a message.</summary>
public sealed record NotificationMessage(Event Event, string RuleName);

/// <summary>A delivery method. Adding a channel means one class and one registration.</summary>
public interface INotificationChannel
{
    /// <summary>Type key stored on channel configs, for example "Email".</summary>
    string Type { get; }

    /// <summary>Returns problems with the settings, or an empty list if valid. Called before settings are stored and before sending.</summary>
    IReadOnlyList<string> ValidateSettings(string settingsJson);

    /// <summary>Returns a display-safe version of the settings with secrets masked.</summary>
    string MaskSettings(string settingsJson);

    Task<ChannelSendResult> SendAsync(NotificationMessage message, string settingsJson, CancellationToken ct);
}

public interface IChannelRegistry
{
    INotificationChannel? Find(string type);
    IReadOnlyCollection<string> Types { get; }
}

/// <summary>Encrypts and decrypts channel settings at rest.</summary>
public interface ISettingsProtector
{
    string Protect(string plain);
    string Unprotect(string protectedValue);
}
