using System.Globalization;
using System.Text.Json;
using EventAlertManagement.Core.Contracts;
using EventAlertManagement.Core.Domain;
using EventAlertManagement.Core.Matching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EventAlertManagement.Infrastructure.Sources;

public sealed class UsgsOptions
{
    /// <summary>USGS GeoJSON summary feed. Verified against the real service, see fixtures/usgs.</summary>
    public string FeedUrl { get; set; } = "https://earthquake.usgs.gov/earthquakes/feed/v1.0/summary/2.5_day.geojson";

    /// <summary>
    /// Earthquakes that happened longer ago than this are ignored. Without it, the first poll would send
    /// alerts for everything in the past day. Events missed during a longer outage are lost (documented limit).
    /// </summary>
    public TimeSpan MaxEventAge { get; set; } = TimeSpan.FromHours(3);
}

/// <summary>Reads the USGS earthquake GeoJSON feed and turns each quake into a normalized event.</summary>
public sealed class UsgsEarthquakeSource(
    IHttpClientFactory httpFactory, IOptions<UsgsOptions> options, IClock clock, ILogger<UsgsEarthquakeSource> log)
    : IEventSource
{
    public const string SourceKey = "usgs-earthquakes";
    public const string HttpClientName = "usgs";

    public string Key => SourceKey;
    public EventCategory? Category => EventCategory.Disasters;

    public async Task<SourceFetchResult> FetchAsync(string? cursor, CancellationToken ct)
    {
        var http = httpFactory.CreateClient(HttpClientName);
        using var response = await http.GetAsync(options.Value.FeedUrl, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);

        var events = Parse(body, clock.UtcNow, options.Value.MaxEventAge, log);
        // No cursor needed: de-duplication by USGS event id and the age limit already prevent repeats.
        return new SourceFetchResult(events, cursor);
    }

    /// <summary>Parses a feed document. Throws <see cref="InvalidDataException"/> if it is not the expected shape.</summary>
    public static IReadOnlyList<NormalizedEvent> Parse(string json, DateTime nowUtc, TimeSpan maxAge, ILogger? log = null)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new InvalidDataException("USGS feed is not valid JSON.", ex); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var type) || type.GetString() != "FeatureCollection" ||
                !root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("USGS feed does not look like a GeoJSON FeatureCollection.");

            var result = new List<NormalizedEvent>();
            var skipped = 0;
            foreach (var feature in features.EnumerateArray())
            {
                var ev = TryMap(feature);
                if (ev is null) { skipped++; continue; }
                if (nowUtc - ev.OccurredAt > maxAge) continue;
                result.Add(ev);
            }
            if (skipped > 0) log?.LogWarning("USGS feed: {Skipped} malformed feature(s) skipped", skipped);
            return result;
        }
    }

    private static NormalizedEvent? TryMap(JsonElement feature)
    {
        try
        {
            if (!feature.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String) return null;
            var id = idEl.GetString();
            if (string.IsNullOrWhiteSpace(id)) return null;
            if (!feature.TryGetProperty("properties", out var p) || p.ValueKind != JsonValueKind.Object) return null;
            if (!p.TryGetProperty("time", out var timeEl) || timeEl.ValueKind != JsonValueKind.Number) return null;

            var occurredAt = DateTimeOffset.FromUnixTimeMilliseconds(timeEl.GetInt64()).UtcDateTime;
            double? mag = p.TryGetProperty("mag", out var magEl) && magEl.ValueKind == JsonValueKind.Number ? magEl.GetDouble() : null;
            var place = Str(p, "place");
            var magType = Str(p, "magType");
            var status = Str(p, "status");
            var title = Str(p, "title") ?? (mag is null ? place : FormattableString.Invariant($"M {mag:0.0} - {place}")) ?? "Earthquake";
            var tsunami = p.TryGetProperty("tsunami", out var t) && t.ValueKind == JsonValueKind.Number && t.GetInt32() == 1;

            double? depthKm = null;
            if (feature.TryGetProperty("geometry", out var g) && g.ValueKind == JsonValueKind.Object &&
                g.TryGetProperty("coordinates", out var c) && c.ValueKind == JsonValueKind.Array && c.GetArrayLength() >= 3 &&
                c[2].ValueKind == JsonValueKind.Number)
                depthKm = c[2].GetDouble();

            var parts = new List<string>();
            parts.Add(mag is null ? "Magnitude unknown" : string.Create(CultureInfo.InvariantCulture, $"Magnitude {mag:0.0}") + (magType is null ? "" : $" ({magType})"));
            if (depthKm is not null) parts.Add(string.Create(CultureInfo.InvariantCulture, $"depth {depthKm:0} km"));
            if (status is not null) parts.Add($"status: {status}");
            var summary = string.Join(", ", parts) + "." + (tsunami ? " Tsunami flag set." : "");

            return new NormalizedEvent(
                ExternalId: id,
                Category: EventCategory.Disasters,
                Severity: mag is null ? 1 : SeverityMapper.FromMagnitude(mag.Value),
                Title: title,
                Summary: summary,
                Url: Str(p, "url"),
                Location: place,
                Attributes: new EventAttributes { Magnitude = mag, EventType = Str(p, "type") },
                OccurredAt: occurredAt);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or OverflowException or ArgumentOutOfRangeException)
        {
            return null; // One bad feature must not hide all the others.
        }
    }

    private static string? Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
