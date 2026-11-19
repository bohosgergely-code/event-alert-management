using System.Net;
using System.Text.Json;
using EventAlertManagement.Core.Contracts;
using EventAlertManagement.Core.Domain;
using EventAlertManagement.Core.Filters;
using EventAlertManagement.Infrastructure.Sources;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EventAlertManagement.Tests.Integration;

/// <summary>
/// Uses a REAL response captured from the USGS feed (fixtures/usgs/2.5_day.raw.geojson).
/// Expected numbers were computed independently with a Python script from the same file, not with this code.
/// </summary>
public class UsgsSourceTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "usgs", "2.5_day.raw.geojson");
    private static string Json => File.ReadAllText(FixturePath);

    // metadata.generated of the captured file (milliseconds since epoch)
    private static readonly DateTime Generated = DateTimeOffset.FromUnixTimeMilliseconds(1795087917000).UtcDateTime;

    [Fact]
    public void Real_feed_without_age_limit_yields_all_36_features()
    {
        var events = UsgsEarthquakeSource.Parse(Json, Generated, TimeSpan.FromDays(2));
        Assert.Equal(36, events.Count);
        Assert.Equal(36, events.Select(e => e.ExternalId).Distinct().Count());
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(3, 4)]
    [InlineData(24, 36)]
    public void Age_limit_drops_old_quakes(int hours, int expected)
    {
        var events = UsgsEarthquakeSource.Parse(Json, Generated, TimeSpan.FromHours(hours));
        Assert.Equal(expected, events.Count);
    }

    [Fact]
    public void First_real_feature_is_mapped_field_by_field()
    {
        var ev = UsgsEarthquakeSource.Parse(Json, Generated, TimeSpan.FromDays(2)).Single(e => e.ExternalId == "us6000ubz1");

        Assert.Equal(EventCategory.Disasters, ev.Category);
        Assert.Equal("M 5.0 - 31 km WSW of Rabaul, Papua New Guinea", ev.Title);
        Assert.Equal("31 km WSW of Rabaul, Papua New Guinea", ev.Location);
        Assert.Equal("https://earthquake.usgs.gov/earthquakes/eventpage/us6000ubz1", ev.Url);
        Assert.Equal(5.0, ev.Attributes.Magnitude);
        Assert.Equal("earthquake", ev.Attributes.EventType);
        Assert.Equal(2, ev.Severity);                                    // magnitude 5.0 falls in 4.5 to 5.4
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1795085587767).UtcDateTime, ev.OccurredAt);
        Assert.Equal(DateTimeKind.Utc, ev.OccurredAt.Kind);
        Assert.Contains("Magnitude 5.0 (mb)", ev.Summary);
        Assert.Contains("depth 195 km", ev.Summary);
        Assert.DoesNotContain("Tsunami", ev.Summary);
        Assert.False(ev.IsSimulated);
    }

    [Fact]
    public void Severity_distribution_matches_the_independent_calculation()
    {
        var events = UsgsEarthquakeSource.Parse(Json, Generated, TimeSpan.FromDays(2));
        var dist = events.GroupBy(e => e.Severity).OrderBy(g => g.Key).Select(g => (g.Key, g.Count())).ToArray();
        Assert.Equal(new[] { (1, 22), (2, 13), (3, 1) }, dist);
    }

    // ---- robustness against odd or broken input ----

    private static string Feed(params string[] features) =>
        "{\"type\":\"FeatureCollection\",\"features\":[" + string.Join(",", features) + "]}";

    private static string Feature(string id = "x1", string props = "\"mag\":6.0,\"place\":\"Somewhere\",\"time\":1795087000000,\"type\":\"earthquake\"") =>
        $"{{\"type\":\"Feature\",\"id\":\"{id}\",\"properties\":{{{props}}},\"geometry\":{{\"type\":\"Point\",\"coordinates\":[1,2,10]}}}}";

    [Fact]
    public void Null_magnitude_is_kept_with_lowest_severity_and_no_magnitude_attribute()
    {
        var events = UsgsEarthquakeSource.Parse(Feed(Feature(props: "\"mag\":null,\"place\":\"P\",\"time\":1795087000000")), Generated, TimeSpan.FromDays(1));
        var ev = Assert.Single(events);
        Assert.Null(ev.Attributes.Magnitude);
        Assert.Equal(1, ev.Severity);
        Assert.Contains("Magnitude unknown", ev.Summary);
    }

    [Fact]
    public void Malformed_features_are_skipped_without_hiding_the_good_ones()
    {
        var feed = Feed(
            Feature("good"),
            "{\"type\":\"Feature\",\"properties\":{\"time\":1795087000000}}",                 // no id
            "{\"type\":\"Feature\",\"id\":\"no-props\"}",                                       // no properties
            Feature("no-time", props: "\"mag\":5.0"),                                          // no time
            Feature("bad-time", props: "\"mag\":5.0,\"time\":\"yesterday\""),                  // time is a string
            "42", "null");
        var events = UsgsEarthquakeSource.Parse(feed, Generated, TimeSpan.FromDays(1));
        Assert.Equal("good", Assert.Single(events).ExternalId);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("{\"type\":\"Something\",\"features\":[]}")]
    [InlineData("{\"type\":\"FeatureCollection\"}")]
    [InlineData("{\"type\":\"FeatureCollection\",\"features\":{}}")]
    public void Unexpected_document_shape_is_an_error_not_an_empty_result(string body)
    {
        // Treating a changed format as "no earthquakes" would hide the problem. It must surface as a failed poll.
        Assert.Throws<InvalidDataException>(() => UsgsEarthquakeSource.Parse(body, Generated, TimeSpan.FromDays(1)));
    }

    [Fact]
    public void Empty_feed_is_valid_and_returns_nothing()
        => Assert.Empty(UsgsEarthquakeSource.Parse(Feed(), Generated, TimeSpan.FromDays(1)));

    [Fact]
    public void Hostile_text_is_passed_through_unchanged_for_the_channels_to_escape()
    {
        var feed = Feed(Feature(props: "\"mag\":6.0,\"time\":1795087000000,\"place\":\"<script>x</script>\",\"title\":\"<b>M 6</b>\""));
        var ev = Assert.Single(UsgsEarthquakeSource.Parse(feed, Generated, TimeSpan.FromDays(1)));
        Assert.Equal("<b>M 6</b>", ev.Title);
    }

    // ---- HTTP behaviour ----

    private sealed class StubFactory(Func<HttpResponseMessage> respond) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(respond));
        private sealed class Handler(Func<HttpResponseMessage> respond) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(respond());
        }
    }

    private static UsgsEarthquakeSource Source(Func<HttpResponseMessage> respond, FakeClock? clock = null) =>
        new(new StubFactory(respond), Options.Create(new UsgsOptions { MaxEventAge = TimeSpan.FromDays(2) }),
            clock ?? new FakeClock { UtcNow = Generated }, NullLogger<UsgsEarthquakeSource>.Instance);

    [Fact]
    public async Task Fetch_returns_events_for_a_good_response()
    {
        var src = Source(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Json) });
        var result = await src.FetchAsync(null, default);
        Assert.Equal(36, result.Events.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Http_errors_are_thrown_so_the_poll_is_recorded_as_failed(HttpStatusCode status)
    {
        var src = Source(() => new HttpResponseMessage(status));
        await Assert.ThrowsAsync<HttpRequestException>(() => src.FetchAsync(null, default));
    }

    // ---- through the whole pipeline ----

    [Fact]
    public async Task Real_quakes_flow_through_matching_and_only_strong_ones_notify()
    {
        using var h = new Harness();
        using (var db = h.NewDb())
        {
            Assert.Single(db.Sources, s => s.Key == UsgsEarthquakeSource.SourceKey); // seeded by DbInitializer
            var u = h.AddUser(db);
            h.AddRule(db, u, EventCategory.Disasters, new DisasterFilters { MinMagnitude = 5.0 }, 1, 0, h.AddChannel(db, u));
        }
        h.Clock.UtcNow = Generated;
        var adapter = Source(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Json) }, h.Clock);

        using (var db = h.NewDb())
        {
            var sourceId = db.Sources.Single(s => s.Key == UsgsEarthquakeSource.SourceKey).Id;
            var poll = await h.Poller(db, adapter).PollAsync(sourceId, default);
            Assert.True(poll.Success);
            Assert.Equal(36, poll.NewEvents);
        }
        using (var db = h.NewDb()) await h.Dispatcher(db).RunOnceAsync(100, default);

        Assert.Equal(7, h.Channel.Sent.Count); // 7 quakes of magnitude 5.0 or more, counted independently in Python

        // Polling the same feed again must not create anything new.
        using (var db = h.NewDb())
        {
            var sourceId = db.Sources.Single(s => s.Key == UsgsEarthquakeSource.SourceKey).Id;
            Assert.Equal(0, (await h.Poller(db, adapter).PollAsync(sourceId, default)).NewEvents);
        }
        using (var db = h.NewDb()) await h.Dispatcher(db).RunOnceAsync(100, default);
        Assert.Equal(7, h.Channel.Sent.Count);
    }
}

/// <summary>Runs only when EAM_LIVE_TESTS=1. Detects format drift in the real USGS service.</summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("EAM_LIVE_TESTS") != "1") Skip = "Set EAM_LIVE_TESTS=1 to call real external services.";
    }
}

public class UsgsLiveTests
{
    private sealed class RealFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new() { Timeout = TimeSpan.FromSeconds(30) };
    }

    [LiveFact]
    public async Task Real_usgs_feed_is_fetched_and_parsed_with_the_production_code_path()
    {
        var src = new UsgsEarthquakeSource(new RealFactory(),
            Options.Create(new UsgsOptions { MaxEventAge = TimeSpan.FromDays(2) }),
            new SystemClock(), NullLogger<UsgsEarthquakeSource>.Instance);

        var result = await src.FetchAsync(null, default);

        Assert.NotEmpty(result.Events);
        Assert.All(result.Events, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.ExternalId));
            Assert.False(string.IsNullOrWhiteSpace(e.Title));
            Assert.InRange(e.Severity, 1, 5);
            Assert.Equal(DateTimeKind.Utc, e.OccurredAt.Kind);
            Assert.True(e.OccurredAt <= DateTime.UtcNow.AddMinutes(5), "event in the future");
            Assert.StartsWith("https://earthquake.usgs.gov/", e.Url);
        });
        Console.WriteLine($"LIVE: {result.Events.Count} events, strongest M{result.Events.Max(e => e.Attributes.Magnitude):0.0}");
    }
}
