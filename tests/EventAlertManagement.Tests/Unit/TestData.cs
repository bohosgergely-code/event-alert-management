using EventAlertManagement.Core.Domain;
using EventAlertManagement.Core.Filters;

namespace EventAlertManagement.Tests.Unit;

internal static class TestData
{
    public static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    public static AlertRule Rule(EventCategory category, RuleFilters? filters = null, int minSeverity = 1) => new()
    {
        Name = "test rule",
        Category = category,
        MinSeverity = minSeverity,
        FiltersJson = RuleFilterSerializer.Serialize(
            filters ?? category switch
            {
                EventCategory.News => new NewsFilters(),
                EventCategory.Disasters => new DisasterFilters(),
                _ => new MarketFilters { Tickers = { "BTC" } },
            }),
    };

    public static Event News(string title, string summary = "", int severity = 2, string source = "rss-test") => new()
    {
        Category = EventCategory.News, Severity = severity, Title = title, Summary = summary, SourceKey = source,
    };

    public static Event Quake(double? magnitude, string? location = "Near Tokyo, Japan", int severity = 3, string? type = "earthquake") => new()
    {
        Category = EventCategory.Disasters, Severity = severity, Title = "Quake", Location = location,
        AttributesJson = new EventAttributes { Magnitude = magnitude, EventType = type }.ToJson(),
    };

    public static Event Move(string? ticker, double? changePercent, int severity = 3) => new()
    {
        Category = EventCategory.Markets, Severity = severity, Title = "Move",
        AttributesJson = new EventAttributes { Ticker = ticker, ChangePercent = changePercent }.ToJson(),
    };
}
