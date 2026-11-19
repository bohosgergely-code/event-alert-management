using System.Text.Json;

namespace EventAlertManagement.Core.Domain;

/// <summary>Typed category-specific facts about an event. Stored as JSON on the event.</summary>
public sealed record EventAttributes
{
    /// <summary>Disasters: earthquake magnitude or equivalent.</summary>
    public double? Magnitude { get; init; }

    /// <summary>Disasters: for example "earthquake".</summary>
    public string? EventType { get; init; }

    /// <summary>Markets: ticker symbol, upper case.</summary>
    public string? Ticker { get; init; }

    /// <summary>Markets: signed percentage change, for example -6.2.</summary>
    public double? ChangePercent { get; init; }

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static EventAttributes FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new EventAttributes();
        try { return JsonSerializer.Deserialize<EventAttributes>(json, Options) ?? new EventAttributes(); }
        catch (JsonException) { return new EventAttributes(); }
    }
}
