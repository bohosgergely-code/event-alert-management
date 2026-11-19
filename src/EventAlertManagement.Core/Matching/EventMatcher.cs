using EventAlertManagement.Core.Domain;
using EventAlertManagement.Core.Filters;

namespace EventAlertManagement.Core.Matching;

/// <summary>Decides whether an event satisfies a rule. Pure logic: no I/O, no clock.</summary>
public interface IEventMatcher
{
    bool Matches(AlertRule rule, Event ev);
}

public sealed class EventMatcher : IEventMatcher
{
    public bool Matches(AlertRule rule, Event ev)
    {
        if (!rule.IsEnabled) return false;
        if (rule.Category != ev.Category) return false;
        if (ev.Severity < rule.MinSeverity) return false;

        RuleFilters filters;
        try { filters = RuleFilterSerializer.Parse(rule.Category, rule.FiltersJson); }
        catch (FormatException) { return false; } // A corrupt rule must never notify.

        var attrs = EventAttributes.FromJson(ev.AttributesJson);
        return filters switch
        {
            NewsFilters f => MatchesNews(f, ev),
            DisasterFilters f => MatchesDisaster(f, ev, attrs),
            MarketFilters f => MatchesMarket(f, attrs),
            _ => false,
        };
    }

    private static bool MatchesNews(NewsFilters f, Event ev)
    {
        if (f.SourceKeys.Count > 0 &&
            !f.SourceKeys.Any(k => string.Equals(k, ev.SourceKey, StringComparison.OrdinalIgnoreCase)))
            return false;

        var text = $"{ev.Title} {ev.Summary}";
        if (f.ExcludeKeywords.Any(k => Contains(text, k))) return false;
        return f.IncludeKeywords.Count == 0 || f.IncludeKeywords.Any(k => Contains(text, k));
    }

    private static bool MatchesDisaster(DisasterFilters f, Event ev, EventAttributes attrs)
    {
        // A magnitude filter cannot be satisfied by an event with no magnitude.
        if (f.MinMagnitude is { } min && (attrs.Magnitude is null || attrs.Magnitude < min)) return false;

        if (!string.IsNullOrWhiteSpace(f.Region) && !Contains(ev.Location ?? "", f.Region)) return false;

        if (f.EventTypes.Count > 0 &&
            !f.EventTypes.Any(t => string.Equals(t, attrs.EventType, StringComparison.OrdinalIgnoreCase)))
            return false;
        return true;
    }

    private static bool MatchesMarket(MarketFilters f, EventAttributes attrs)
    {
        if (string.IsNullOrWhiteSpace(attrs.Ticker) || attrs.ChangePercent is null) return false;

        if (f.Tickers.Count > 0 &&
            !f.Tickers.Any(t => string.Equals(t, attrs.Ticker, StringComparison.OrdinalIgnoreCase)))
            return false;

        var change = attrs.ChangePercent.Value;
        if (Math.Abs(change) < f.MinPercentMove) return false;

        return f.Direction switch
        {
            MoveDirection.Up => change > 0,
            MoveDirection.Down => change < 0,
            _ => true,
        };
    }

    private static bool Contains(string text, string needle) =>
        !string.IsNullOrWhiteSpace(needle) && text.Contains(needle.Trim(), StringComparison.OrdinalIgnoreCase);
}
