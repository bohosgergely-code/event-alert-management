namespace EventAlertManagement.Core.Domain;

public enum EventCategory
{
    News = 1,
    Markets = 2,
    Disasters = 3,
}

public enum UserRole
{
    User = 0,
    Admin = 1,
}

public enum NotificationStatus
{
    Pending = 0,
    Sending = 1,
    Retrying = 2,
    Sent = 3,
    Failed = 4,
    Suppressed = 5,
}

public enum AttemptOutcome
{
    Success = 0,
    TransientFailure = 1,
    PermanentFailure = 2,
}
