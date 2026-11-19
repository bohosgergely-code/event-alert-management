using System.Text.Json;
using EventAlertManagement.Core.Domain;

namespace EventAlertManagement.Core.Filters;

public enum MoveDirection { Either = 0, Up = 1, Down = 2 }

public abstract record RuleFilters
{
    /// <summary>Shape version, so older stored filters can still be read after changes.</summary>
    public int Version { get; init; } = 1;
}

public sealed record NewsFilters : RuleFilters
{
    /// <summary>Event matches if any keyword appears in title or summary. Empty means any news.</summary>
    public List<string> IncludeKeywords { get; init; } = new();
    public List<string> ExcludeKeywords { get; init; } = new();

    /// <summary>Restrict to these source keys. Empty means all sources.</summary>
    public List<string> SourceKeys { get; init; } = new();
}

public sealed record DisasterFilters : RuleFilters
{
    public double? MinMagnitude { get; init; }

    /// <summary>Case-insensitive text that must appear in the event location.</summary>
    public string? Region { get; init; }
    public List<string> EventTypes { get; init; } = new();
}

public sealed record MarketFilters : RuleFilters
{
    public List<string> Tickers { get; init; } = new();

    /// <summary>Minimum absolute percentage move.</summary>
    public double MinPercentMove { get; init; }
    public MoveDirection Direction { get; init; } = MoveDirection.Either;
}

public static class RuleFilterSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public static string Serialize(RuleFilters filters) =>
        JsonSerializer.Serialize(filters, filters.GetType(), Options);

    /// <summary>Parses stored filters for a category. Throws <see cref="FormatException"/> on invalid JSON.</summary>
    public static RuleFilters Parse(EventCategory category, string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) json = "{}";
        try
        {
            return category switch
            {
                EventCategory.News => JsonSerializer.Deserialize<NewsFilters>(json, Options) ?? new NewsFilters(),
                EventCategory.Disasters => JsonSerializer.Deserialize<DisasterFilters>(json, Options) ?? new DisasterFilters(),
                EventCategory.Markets => JsonSerializer.Deserialize<MarketFilters>(json, Options) ?? new MarketFilters(),
                _ => throw new FormatException($"Unknown category {category}."),
            };
        }
        catch (JsonException ex)
        {
            throw new FormatException("Rule filters are not valid JSON for this category.", ex);
        }
    }
}
