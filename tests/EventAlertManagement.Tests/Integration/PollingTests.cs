using EventAlertManagement.Core.Contracts;
using EventAlertManagement.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace EventAlertManagement.Tests.Integration;

public class PollingTests : IDisposable
{
    private readonly Harness _h = new();
    public void Dispose() => _h.Dispose();

    private sealed class ThrowingSource(string key) : IEventSource
    {
        public string Key => key;
        public EventCategory? Category => null;
        public Task<SourceFetchResult> FetchAsync(string? cursor, CancellationToken ct) =>
            throw new HttpRequestException("failed calling https://feed.example/secret?token=ABC");
    }

    private sealed class FixedSource(string key, params NormalizedEvent[] events) : IEventSource
    {
        public string Key => key;
        public EventCategory? Category => null;
        public Task<SourceFetchResult> FetchAsync(string? cursor, CancellationToken ct) =>
            Task.FromResult(new SourceFetchResult(events, "cursor-2"));
    }

    [Fact]
    public async Task Simulated_event_flows_through_the_real_pipeline_end_to_end()
    {
        using (var db = _h.NewDb())
        {
            var u = _h.AddUser(db);
            _h.AddRule(db, u, EventCategory.News, null, 1, 0, _h.AddChannel(db, u));
        }
        _h.Simulator.Enqueue(Harness.NewsItem("sim-1"));

        using (var db = _h.NewDb())
        {
            var result = await _h.Poller(db, _h.Simulator).PollAsync(_h.SimSource(db).Id, default);
            Assert.True(result.Success);
            Assert.Equal(1, result.NewEvents);
        }
        using (var db = _h.NewDb())
        {
            await _h.Dispatcher(db).RunOnceAsync(10, default);
        }

        Assert.Single(_h.Channel.Sent);
        using var check = _h.NewDb();
        Assert.True(check.Events.Single().IsSimulated);
    }

    [Fact]
    public async Task Failing_source_records_health_stores_nothing_and_does_not_leak_the_url()
    {
        using var db = _h.NewDb();
        var bad = new Source { Key = "bad-feed", Name = "Bad", PollIntervalSeconds = 60 };
        db.Sources.Add(bad); db.SaveChanges();

        var result = await _h.Poller(db, new ThrowingSource("bad-feed")).PollAsync(bad.Id, default);

        Assert.False(result.Success);
        using var check = _h.NewDb();
        var s = check.Sources.Single(x => x.Key == "bad-feed");
        Assert.Equal(1, s.ConsecutiveFailures);
        Assert.NotNull(s.LastError);
        Assert.DoesNotContain("token=ABC", s.LastError);
        Assert.Null(s.LastSuccessAt);
        Assert.Empty(check.Events);
    }

    [Fact]
    public async Task One_failing_source_does_not_stop_the_others_in_the_same_cycle()
    {
        using (var db = _h.NewDb())
        {
            db.Sources.Add(new Source { Key = "aaa-bad", Name = "Bad", PollIntervalSeconds = 60 });
            db.Sources.Add(new Source { Key = "zzz-good", Name = "Good", PollIntervalSeconds = 60 });
            db.SaveChanges();
        }

        using (var db = _h.NewDb())
        {
            var poller = _h.Poller(db, new ThrowingSource("aaa-bad"), new FixedSource("zzz-good", Harness.NewsItem("g1")));
            await poller.PollDueAsync(default);
        }

        using var check = _h.NewDb();
        Assert.Equal(1, check.Events.Count());
        var good = check.Sources.Single(s => s.Key == "zzz-good");
        Assert.Equal("cursor-2", good.Cursor);
        Assert.NotNull(good.LastSuccessAt);
        Assert.Equal(1, check.Sources.Single(s => s.Key == "aaa-bad").ConsecutiveFailures);
    }

    [Fact]
    public async Task Source_is_not_polled_again_before_its_interval_has_passed()
    {
        using (var db = _h.NewDb()) { db.Sources.Single(s => s.Key == "simulator").PollIntervalSeconds = 60; db.SaveChanges(); }

        using (var db = _h.NewDb()) Assert.Equal(1, await _h.Poller(db, _h.Simulator).PollDueAsync(default));
        _h.Clock.Advance(TimeSpan.FromSeconds(30));
        using (var db = _h.NewDb()) Assert.Equal(0, await _h.Poller(db, _h.Simulator).PollDueAsync(default));
        _h.Clock.Advance(TimeSpan.FromSeconds(31));
        using (var db = _h.NewDb()) Assert.Equal(1, await _h.Poller(db, _h.Simulator).PollDueAsync(default));
    }

    [Fact]
    public async Task Source_without_an_adapter_reports_a_clear_error()
    {
        using var db = _h.NewDb();
        var orphan = new Source { Key = "no-adapter", Name = "Orphan" };
        db.Sources.Add(orphan); db.SaveChanges();

        var r = await _h.Poller(db).PollAsync(orphan.Id, default);

        Assert.False(r.Success);
        Assert.Contains("adapter", r.Error, StringComparison.OrdinalIgnoreCase);
    }
}
