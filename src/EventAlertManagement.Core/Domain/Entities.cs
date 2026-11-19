namespace EventAlertManagement.Core.Domain;

// All timestamps are UTC. Entities are plain data holders; behaviour lives in services.

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.User;
    public DateTime CreatedAt { get; set; }
}

public class ChannelConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Channel type key, for example "Email" or "Slack". Must match a registered channel.</summary>
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>Channel settings (JSON), stored encrypted. Never expose in full.</summary>
    public string SettingsProtected { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public DateTime? LastTestedAt { get; set; }
    public string? LastTestResult { get; set; }
}

public class AlertRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string Name { get; set; } = "";
    public EventCategory Category { get; set; }

    /// <summary>1-5. Events below this severity do not match.</summary>
    public int MinSeverity { get; set; } = 1;

    /// <summary>Category-specific filters as versioned JSON. See <c>RuleFilterSerializer</c>.</summary>
    public string FiltersJson { get; set; } = "{}";

    /// <summary>Minimum seconds between notifications for this rule. 0 disables the cooldown.</summary>
    public int CooldownSeconds { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime? LastNotifiedAt { get; set; }
    public List<ChannelConfig> Channels { get; set; } = new();
}

public class Source
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Stable key matching an <c>IEventSource.Key</c>, for example "simulator" or "usgs-earthquakes".</summary>
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>Null for sources that produce several categories, such as the simulator.</summary>
    public EventCategory? Category { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 300;

    /// <summary>Opaque position the adapter uses to fetch only new items.</summary>
    public string? Cursor { get; set; }
    public DateTime? LastPollAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public string? LastError { get; set; }
    public int ConsecutiveFailures { get; set; }
}

public class Event
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceId { get; set; }
    public Source? Source { get; set; }
    public string SourceKey { get; set; } = "";

    /// <summary>Source key + the source's own stable identifier. Unique.</summary>
    public string DedupeKey { get; set; } = "";
    public EventCategory Category { get; set; }
    public int Severity { get; set; }

    // Title, Summary and Url come from outside and are untrusted. Escape at output time.
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string? Url { get; set; }
    public string? Location { get; set; }

    /// <summary>Category-specific facts as JSON. See <c>EventAttributes</c>.</summary>
    public string AttributesJson { get; set; } = "{}";
    public DateTime OccurredAt { get; set; }
    public DateTime IngestedAt { get; set; }
    public bool IsSimulated { get; set; }
}

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RuleId { get; set; }
    public AlertRule? Rule { get; set; }
    public Guid EventId { get; set; }
    public Event? Event { get; set; }
    public Guid ChannelConfigId { get; set; }
    public ChannelConfig? ChannelConfig { get; set; }

    public NotificationStatus Status { get; set; }
    public string? SuppressedReason { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>Set while the dispatcher owns this notification. An expired lease means the dispatcher crashed.</summary>
    public DateTime? LeaseUntil { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public List<DeliveryAttempt> Attempts { get; set; } = new();
}

public class DeliveryAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NotificationId { get; set; }
    public Notification? Notification { get; set; }
    public int AttemptNumber { get; set; }
    public DateTime StartedAt { get; set; }
    public int DurationMs { get; set; }
    public AttemptOutcome Outcome { get; set; }

    /// <summary>Sanitized: must never contain secrets or webhook URLs.</summary>
    public string? ErrorMessage { get; set; }
}
