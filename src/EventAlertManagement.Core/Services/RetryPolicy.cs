namespace EventAlertManagement.Core.Services;

public sealed class RetryOptions
{
    /// <summary>Total attempts including the first one.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Delay before attempt 2, 3, ... The last value is reused if more are needed.</summary>
    public List<TimeSpan> Delays { get; set; } = new()
    {
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
    };

    /// <summary>How long the dispatcher owns a notification before another cycle may take it over.</summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(2);
}

public static class RetryPolicy
{
    /// <summary>
    /// Time of the next attempt after <paramref name="attemptsMade"/> failed attempts,
    /// or null if no attempts remain.
    /// </summary>
    public static DateTime? NextAttemptAt(RetryOptions options, int attemptsMade, DateTime nowUtc)
    {
        if (attemptsMade >= options.MaxAttempts) return null;
        if (options.Delays.Count == 0) return nowUtc;

        var index = Math.Min(Math.Max(attemptsMade - 1, 0), options.Delays.Count - 1);
        return nowUtc + options.Delays[index];
    }
}
