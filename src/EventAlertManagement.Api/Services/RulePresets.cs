using System.Text.Json;
using EventAlertManagement.Api.Dtos;
using EventAlertManagement.Core.Domain;
using EventAlertManagement.Core.Filters;

namespace EventAlertManagement.Api.Services;

/// <summary>Starting points so users do not face an empty form (see ASSUMPTIONS, devil's advocate section).</summary>
public static class RulePresets
{
    private static JsonElement J(RuleFilters f) => Mapping.ParseJson(RuleFilterSerializer.Serialize(f));

    public static IReadOnlyList<PresetResponse> All { get; } = new List<PresetResponse>
    {
        new("Major earthquakes", "Earthquakes of magnitude 6 or more anywhere in the world.",
            new(EventCategory.Disasters, 1, J(new DisasterFilters { MinMagnitude = 6.0 }), 0)),
        new("Strong earthquakes in a region", "Earthquakes of magnitude 5 or more. Add a region name such as Japan.",
            new(EventCategory.Disasters, 1, J(new DisasterFilters { MinMagnitude = 5.0, EventTypes = { "earthquake" } }), 1800)),
        new("Bitcoin big moves", "Bitcoin moving 5 percent or more in either direction.",
            new(EventCategory.Markets, 1, J(new MarketFilters { Tickers = { "BTC" }, MinPercentMove = 5, Direction = MoveDirection.Either }), 3600)),
        new("Breaking news keywords", "News mentioning evacuation, outbreak or state of emergency.",
            new(EventCategory.News, 1, J(new NewsFilters { IncludeKeywords = { "evacuation", "outbreak", "state of emergency" } }), 900)),
    };
}
