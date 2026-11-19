using EventAlertManagement.Core.Domain;
using EventAlertManagement.Core.Filters;
using EventAlertManagement.Core.Matching;
using static EventAlertManagement.Tests.Unit.TestData;

namespace EventAlertManagement.Tests.Unit;

// Written from the specification in docs/ARCHITECTURE.md (section 4, filters by category), not from the matcher code.
public class EventMatcherTests
{
    private readonly EventMatcher _matcher = new();

    // ---- common rules ----

    [Fact]
    public void Disabled_rule_never_matches()
    {
        var rule = Rule(EventCategory.News); rule.IsEnabled = false;
        Assert.False(_matcher.Matches(rule, News("anything")));
    }

    [Fact]
    public void Category_must_be_equal()
    {
        Assert.False(_matcher.Matches(Rule(EventCategory.News), Quake(7)));
    }

    [Theory]
    [InlineData(3, 3, true)]   // equal to minimum matches
    [InlineData(4, 3, true)]
    [InlineData(2, 3, false)]
    public void Minimum_severity_is_inclusive(int eventSeverity, int minSeverity, bool expected)
    {
        var rule = Rule(EventCategory.News, minSeverity: minSeverity);
        Assert.Equal(expected, _matcher.Matches(rule, News("x", severity: eventSeverity)));
    }

    [Fact]
    public void Corrupt_filter_json_never_matches()
    {
        var rule = Rule(EventCategory.News); rule.FiltersJson = "{not json";
        Assert.False(_matcher.Matches(rule, News("x")));
    }

    // ---- news ----

    [Fact]
    public void News_with_no_keywords_matches_any_news()
        => Assert.True(_matcher.Matches(Rule(EventCategory.News), News("Anything at all")));

    [Fact]
    public void News_include_keyword_is_case_insensitive_and_searches_title_and_summary()
    {
        var rule = Rule(EventCategory.News, new NewsFilters { IncludeKeywords = { "FLOOD" } });
        Assert.True(_matcher.Matches(rule, News("Flood warning issued")));
        Assert.True(_matcher.Matches(rule, News("Weather", "heavy flooding expected")));
        Assert.False(_matcher.Matches(rule, News("Sunny weekend")));
    }

    [Fact]
    public void News_exclude_keyword_wins_over_include()
    {
        var rule = Rule(EventCategory.News, new NewsFilters { IncludeKeywords = { "storm" }, ExcludeKeywords = { "sports" } });
        Assert.False(_matcher.Matches(rule, News("Storm stops sports final")));
        Assert.True(_matcher.Matches(rule, News("Storm approaches coast")));
    }

    [Fact]
    public void News_source_filter_restricts_sources()
    {
        var rule = Rule(EventCategory.News, new NewsFilters { SourceKeys = { "bbc" } });
        Assert.True(_matcher.Matches(rule, News("x", source: "BBC")));
        Assert.False(_matcher.Matches(rule, News("x", source: "other")));
    }

    [Fact]
    public void News_blank_keyword_does_not_match_everything()
    {
        var rule = Rule(EventCategory.News, new NewsFilters { IncludeKeywords = { "  " } });
        Assert.False(_matcher.Matches(rule, News("Anything")));
    }

    // ---- disasters ----

    [Theory]
    [InlineData(6.0, 6.0, true)]
    [InlineData(6.1, 6.0, true)]
    [InlineData(5.9, 6.0, false)]
    public void Disaster_magnitude_threshold_is_inclusive(double magnitude, double min, bool expected)
    {
        var rule = Rule(EventCategory.Disasters, new DisasterFilters { MinMagnitude = min });
        Assert.Equal(expected, _matcher.Matches(rule, Quake(magnitude)));
    }

    [Fact]
    public void Disaster_magnitude_filter_rejects_event_without_magnitude()
    {
        var rule = Rule(EventCategory.Disasters, new DisasterFilters { MinMagnitude = 1 });
        Assert.False(_matcher.Matches(rule, Quake(null)));
    }

    [Fact]
    public void Disaster_region_is_case_insensitive_substring_of_location()
    {
        var rule = Rule(EventCategory.Disasters, new DisasterFilters { Region = "japan" });
        Assert.True(_matcher.Matches(rule, Quake(5)));
        Assert.False(_matcher.Matches(rule, Quake(5, location: "Chile")));
        Assert.False(_matcher.Matches(rule, Quake(5, location: null)));
    }

    [Fact]
    public void Disaster_event_type_filter()
    {
        var rule = Rule(EventCategory.Disasters, new DisasterFilters { EventTypes = { "Volcano" } });
        Assert.False(_matcher.Matches(rule, Quake(5)));
        Assert.True(_matcher.Matches(rule, Quake(5, type: "volcano")));
    }

    // ---- markets ----

    [Theory]
    [InlineData("BTC", 5.0, 5.0, true)]
    [InlineData("btc", 5.1, 5.0, true)]
    [InlineData("BTC", 4.9, 5.0, false)]
    [InlineData("ETH", 9.0, 5.0, false)]
    public void Market_ticker_and_threshold(string ticker, double change, double min, bool expected)
    {
        var rule = Rule(EventCategory.Markets, new MarketFilters { Tickers = { "BTC" }, MinPercentMove = min });
        Assert.Equal(expected, _matcher.Matches(rule, Move(ticker, change)));
    }

    [Theory]
    [InlineData(MoveDirection.Up, 6.0, true)]
    [InlineData(MoveDirection.Up, -6.0, false)]
    [InlineData(MoveDirection.Down, -6.0, true)]
    [InlineData(MoveDirection.Down, 6.0, false)]
    [InlineData(MoveDirection.Either, -6.0, true)]
    public void Market_direction(MoveDirection direction, double change, bool expected)
    {
        var rule = Rule(EventCategory.Markets, new MarketFilters { Tickers = { "BTC" }, MinPercentMove = 5, Direction = direction });
        Assert.Equal(expected, _matcher.Matches(rule, Move("BTC", change)));
    }

    [Fact]
    public void Market_event_missing_ticker_or_change_never_matches()
    {
        var rule = Rule(EventCategory.Markets, new MarketFilters { Tickers = { "BTC" } });
        Assert.False(_matcher.Matches(rule, Move(null, 9)));
        Assert.False(_matcher.Matches(rule, Move("BTC", null)));
    }
}
