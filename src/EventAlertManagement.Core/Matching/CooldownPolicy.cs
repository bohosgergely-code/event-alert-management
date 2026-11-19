using EventAlertManagement.Core.Domain;

namespace EventAlertManagement.Core.Matching;

public static class CooldownPolicy
{
    /// <summary>
    /// True if the rule was notified recently enough that a new notification should be suppressed.
    /// A cooldown exists to reduce noise, not to hide important news: an event MORE severe than the one that
    /// started the window is never held back (escalation).
    /// </summary>
    public static bool IsInCooldown(AlertRule rule, DateTime nowUtc, out DateTime? until, int? eventSeverity = null)
    {
        until = null;
        if (rule.CooldownSeconds <= 0 || rule.LastNotifiedAt is null) return false;
        if (eventSeverity is { } sev && rule.LastNotifiedSeverity is { } windowSeverity && sev > windowSeverity) return false;

        var end = rule.LastNotifiedAt.Value.AddSeconds(rule.CooldownSeconds);
        if (nowUtc >= end) return false;

        until = end;
        return true;
    }
}
