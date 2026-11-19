using EventAlertManagement.Core.Domain;

namespace EventAlertManagement.Core.Matching;

public static class CooldownPolicy
{
    /// <summary>True if the rule was notified recently enough that a new notification should be suppressed.</summary>
    public static bool IsInCooldown(AlertRule rule, DateTime nowUtc, out DateTime? until)
    {
        until = null;
        if (rule.CooldownSeconds <= 0 || rule.LastNotifiedAt is null) return false;

        var end = rule.LastNotifiedAt.Value.AddSeconds(rule.CooldownSeconds);
        if (nowUtc >= end) return false;

        until = end;
        return true;
    }
}
