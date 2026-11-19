using EventAlertManagement.Core.Domain;
using EventAlertManagement.Core.Filters;
using EventAlertManagement.Core.Matching;
using EventAlertManagement.Core.Services;
using static EventAlertManagement.Tests.Unit.TestData;

namespace EventAlertManagement.Tests.Unit;

public class SeverityMapperTests
{
    // Boundaries from docs/ARCHITECTURE.md "Severity mapping".
    [Theory]
    [InlineData(3.0, 1)] [InlineData(4.4, 1)]
    [InlineData(4.5, 2)] [InlineData(5.4, 2)]
    [InlineData(5.5, 3)] [InlineData(6.4, 3)]
    [InlineData(6.5, 4)] [InlineData(7.4, 4)]
    [InlineData(7.5, 5)] [InlineData(9.1, 5)]
    public void Magnitude_mapping(double magnitude, int expected)
        => Assert.Equal(expected, SeverityMapper.FromMagnitude(magnitude));

    [Theory]
    [InlineData(0.5, 1)] [InlineData(2.9, 1)]
    [InlineData(3.0, 2)] [InlineData(4.9, 2)]
    [InlineData(5.0, 3)] [InlineData(7.9, 3)]
    [InlineData(8.0, 4)] [InlineData(11.9, 4)]
    [InlineData(12.0, 5)] [InlineData(40, 5)]
    public void Percent_move_mapping_uses_absolute_value(double change, int expected)
    {
        Assert.Equal(expected, SeverityMapper.FromPercentMove(change));
        Assert.Equal(expected, SeverityMapper.FromPercentMove(-change));
    }
}

public class CooldownPolicyTests
{
    [Fact]
    public void No_cooldown_configured_never_suppresses()
    {
        var rule = new AlertRule { CooldownSeconds = 0, LastNotifiedAt = Now };
        Assert.False(CooldownPolicy.IsInCooldown(rule, Now, out _));
    }

    [Fact]
    public void Never_notified_is_not_in_cooldown()
    {
        var rule = new AlertRule { CooldownSeconds = 600 };
        Assert.False(CooldownPolicy.IsInCooldown(rule, Now, out _));
    }

    [Fact]
    public void Inside_window_is_suppressed_and_reports_end_time()
    {
        var rule = new AlertRule { CooldownSeconds = 600, LastNotifiedAt = Now };
        Assert.True(CooldownPolicy.IsInCooldown(rule, Now.AddSeconds(599), out var until));
        Assert.Equal(Now.AddSeconds(600), until);
    }

    [Fact]
    public void Exactly_at_window_end_is_allowed()
        => Assert.False(CooldownPolicy.IsInCooldown(new AlertRule { CooldownSeconds = 600, LastNotifiedAt = Now }, Now.AddSeconds(600), out _));
}

public class RetryPolicyTests
{
    private readonly RetryOptions _options = new()
    {
        MaxAttempts = 4,
        Delays = { }, // replaced below
    };

    public RetryPolicyTests()
    {
        _options.Delays = new() { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5) };
    }

    [Fact]
    public void Delay_follows_the_list_and_reuses_the_last_value()
    {
        Assert.Equal(Now.AddMinutes(1), RetryPolicy.NextAttemptAt(_options, 1, Now));
        Assert.Equal(Now.AddMinutes(5), RetryPolicy.NextAttemptAt(_options, 2, Now));
        Assert.Equal(Now.AddMinutes(5), RetryPolicy.NextAttemptAt(_options, 3, Now));
    }

    [Fact]
    public void No_retry_once_attempts_are_exhausted()
    {
        Assert.Null(RetryPolicy.NextAttemptAt(_options, 4, Now));
        Assert.Null(RetryPolicy.NextAttemptAt(_options, 9, Now));
    }
}

public class RuleValidatorTests
{
    private static AlertRule Valid(EventCategory c = EventCategory.News)
    {
        var rule = Rule(c);
        rule.Channels.Add(new ChannelConfig());
        return rule;
    }

    [Fact] public void Valid_rule_has_no_problems() => Assert.Empty(RuleValidator.Validate(Valid()));

    [Fact]
    public void Rule_needs_a_channel()
    {
        var rule = Valid(); rule.Channels.Clear();
        Assert.Contains(RuleValidator.Validate(rule), p => p.Contains("channel", StringComparison.OrdinalIgnoreCase));
    }

    [Theory] [InlineData(0)] [InlineData(6)]
    public void Severity_must_be_1_to_5(int severity)
    {
        var rule = Valid(); rule.MinSeverity = severity;
        Assert.NotEmpty(RuleValidator.Validate(rule));
    }

    [Fact]
    public void Markets_rule_needs_a_ticker()
    {
        var rule = Valid(EventCategory.Markets);
        rule.FiltersJson = RuleFilterSerializer.Serialize(new MarketFilters());
        Assert.Contains(RuleValidator.Validate(rule), p => p.Contains("ticker", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Invalid_filter_json_is_reported_not_thrown()
    {
        var rule = Valid(); rule.FiltersJson = "[1,2";
        Assert.NotEmpty(RuleValidator.Validate(rule));
    }

    [Fact]
    public void Negative_cooldown_is_rejected()
    {
        var rule = Valid(); rule.CooldownSeconds = -1;
        Assert.NotEmpty(RuleValidator.Validate(rule));
    }
}

public class RuleFilterStrictnessTests
{
    [Theory]
    [InlineData(EventCategory.Disasters, "{\"tickers\":[\"BTC\"]}")]            // market field on a disaster rule
    [InlineData(EventCategory.Markets, "{\"includeKeywords\":[\"storm\"]}")]    // news field on a market rule
    [InlineData(EventCategory.News, "{\"minMagnitude\":6}")]                    // disaster field on a news rule
    [InlineData(EventCategory.News, "{\"includeKeyword\":[\"typo\"]}")]         // misspelled property
    public void Filters_for_the_wrong_category_or_with_typos_are_rejected(EventCategory category, string json)
        => Assert.Throws<FormatException>(() => RuleFilterSerializer.Parse(category, json));

    [Fact]
    public void Stored_filters_round_trip_through_serialize_and_parse()
    {
        var original = new MarketFilters { Tickers = { "BTC", "ETH" }, MinPercentMove = 4.5, Direction = MoveDirection.Down };
        var parsed = Assert.IsType<MarketFilters>(RuleFilterSerializer.Parse(EventCategory.Markets, RuleFilterSerializer.Serialize(original)));
        Assert.Equal(original.Tickers, parsed.Tickers);
        Assert.Equal(4.5, parsed.MinPercentMove);
        Assert.Equal(MoveDirection.Down, parsed.Direction);
    }

    [Fact]
    public void A_stored_rule_with_unknown_filter_fields_never_matches_instead_of_matching_everything()
    {
        var rule = Rule(EventCategory.Disasters);
        rule.FiltersJson = "{\"tickers\":[\"BTC\"]}";
        Assert.False(new EventMatcher().Matches(rule, Quake(8)));
    }

    [Fact]
    public void Excessive_cooldown_is_rejected()
    {
        var rule = Rule(EventCategory.News); rule.Channels.Add(new ChannelConfig());
        rule.CooldownSeconds = RuleValidator.MaxCooldownSeconds + 1;
        Assert.NotEmpty(RuleValidator.Validate(rule));
    }
}
