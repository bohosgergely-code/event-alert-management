using EventAlertManagement.Core.Domain;
using EventAlertManagement.Core.Filters;
using Microsoft.EntityFrameworkCore;

namespace EventAlertManagement.Tests.Integration;

public class IngestionTests : IDisposable
{
    private readonly Harness _h = new();
    public void Dispose() => _h.Dispose();

    private async Task<(User user, ChannelConfig ch1, ChannelConfig ch2)> TwoChannelSetup()
    {
        using var db = _h.NewDb();
        var u = _h.AddUser(db);
        return (u, _h.AddChannel(db, u, "Fake"), _h.AddChannel(db, u, "Fake"));
    }

    [Fact]
    public async Task Matching_event_creates_one_pending_notification_per_channel()
    {
        var (u, c1, c2) = await TwoChannelSetup();
        using var db = _h.NewDb();
        _h.AddRule(db, u, EventCategory.News, null, 1, 0, db.ChannelConfigs.Find(c1.Id)!, db.ChannelConfigs.Find(c2.Id)!);

        var result = await _h.Ingestion(db).IngestAsync(_h.SimSource(db), new[] { Harness.NewsItem("a") }, null, default);

        Assert.Equal(1, result.NewEvents);
        Assert.Equal(2, result.NotificationsCreated);
        using var check = _h.NewDb();
        Assert.All(check.Notifications, n => Assert.Equal(NotificationStatus.Pending, n.Status));
        Assert.Equal(2, check.Notifications.Count());
    }

    [Fact]
    public async Task Same_event_ingested_twice_gives_one_event_and_one_notification()
    {
        var (u, c1, _) = await TwoChannelSetup();
        using var db = _h.NewDb();
        _h.AddRule(db, u, EventCategory.News, null, 1, 0, db.ChannelConfigs.Find(c1.Id)!);
        var item = Harness.NewsItem("same-id");

        await _h.Ingestion(db).IngestAsync(_h.SimSource(db), new[] { item }, null, default);
        var second = await _h.Ingestion(db).IngestAsync(_h.SimSource(db), new[] { item }, null, default);

        Assert.Equal(0, second.NewEvents);
        Assert.Equal(1, second.Duplicates);
        using var check = _h.NewDb();
        Assert.Equal(1, check.Events.Count());
        Assert.Equal(1, check.Notifications.Count());
    }

    [Fact]
    public async Task Duplicates_inside_one_batch_are_collapsed()
    {
        var (u, c1, _) = await TwoChannelSetup();
        using var db = _h.NewDb();
        _h.AddRule(db, u, EventCategory.News, null, 1, 0, db.ChannelConfigs.Find(c1.Id)!);

        var r = await _h.Ingestion(db).IngestAsync(_h.SimSource(db), new[] { Harness.NewsItem("x"), Harness.NewsItem("x") }, null, default);

        Assert.Equal(1, r.NewEvents);
        Assert.Equal(1, r.NotificationsCreated);
    }

    [Fact]
    public async Task Database_itself_rejects_a_duplicate_rule_event_channel_notification()
    {
        // The unique index is the last line of defence if application logic ever fails.
        var (u, c1, _) = await TwoChannelSetup();
        using var db = _h.NewDb();
        _h.AddRule(db, u, EventCategory.News, null, 1, 0, db.ChannelConfigs.Find(c1.Id)!);
        await _h.Ingestion(db).IngestAsync(_h.SimSource(db), new[] { Harness.NewsItem("a") }, null, default);

        using var db2 = _h.NewDb();
        var existing = db2.Notifications.AsNoTracking().Single();
        db2.Notifications.Add(new Notification { RuleId = existing.RuleId, EventId = existing.EventId, ChannelConfigId = existing.ChannelConfigId, CreatedAt = _h.Clock.UtcNow });

        await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
    }

    [Fact]
    public async Task Non_matching_event_is_stored_but_creates_no_notification()
    {
        var (u, c1, _) = await TwoChannelSetup();
        using var db = _h.NewDb();
        _h.AddRule(db, u, EventCategory.News, new NewsFilters { IncludeKeywords = { "volcano" } }, 1, 0, db.ChannelConfigs.Find(c1.Id)!);

        var r = await _h.Ingestion(db).IngestAsync(_h.SimSource(db), new[] { Harness.NewsItem("a", "Sunny day") }, null, default);

        Assert.Equal(1, r.NewEvents);
        Assert.Equal(0, r.NotificationsCreated);
        using var check = _h.NewDb();
        Assert.Empty(check.Notifications);
    }

    [Fact]
    public async Task Disabled_rule_creates_nothing()
    {
        var (u, c1, _) = await TwoChannelSetup();
        using var db = _h.NewDb();
        var rule = _h.AddRule(db, u, EventCategory.News, null, 1, 0, db.ChannelConfigs.Find(c1.Id)!);
        rule.IsEnabled = false; db.SaveChanges();

        var r = await _h.Ingestion(db).IngestAsync(_h.SimSource(db), new[] { Harness.NewsItem("a") }, null, default);

        Assert.Equal(0, r.NotificationsCreated + r.NotificationsSuppressed);
    }

    [Fact]
    public async Task Disabled_channel_produces_a_suppressed_record_with_a_reason()
    {
        var (u, _, _) = await TwoChannelSetup();
        using var db = _h.NewDb();
        var off = _h.AddChannel(db, u, "Fake", enabled: false);
        _h.AddRule(db, u, EventCategory.News, null, 1, 0, off);

        await _h.Ingestion(db).IngestAsync(_h.SimSource(db), new[] { Harness.NewsItem("a") }, null, default);

        using var check = _h.NewDb();
        var n = check.Notifications.Single();
        Assert.Equal(NotificationStatus.Suppressed, n.Status);
        Assert.Contains("disabled", n.SuppressedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cooldown_suppresses_followup_events_and_records_why_then_allows_again_after_it_ends()
    {
        var (u, c1, _) = await TwoChannelSetup();
        using var db = _h.NewDb();
        _h.AddRule(db, u, EventCategory.News, null, 1, 600, db.ChannelConfigs.Find(c1.Id)!);

        // Two events in one batch: first notifies, second falls inside the cooldown.
        var r1 = await _h.Ingestion(db).IngestAsync(_h.SimSource(db),
            new[] { Harness.NewsItem("a"), Harness.NewsItem("b", at: new DateTime(2026, 6, 1, 11, 5, 0, DateTimeKind.Utc)) }, null, default);
        Assert.Equal(1, r1.NotificationsCreated);
        Assert.Equal(1, r1.NotificationsSuppressed);

        _h.Clock.Advance(TimeSpan.FromSeconds(599));
        using var db2 = _h.NewDb();
        var r2 = await _h.Ingestion(db2).IngestAsync(_h.SimSource(db2), new[] { Harness.NewsItem("c") }, null, default);
        Assert.Equal(1, r2.NotificationsSuppressed);

        _h.Clock.Advance(TimeSpan.FromSeconds(2)); // now past the window
        using var db3 = _h.NewDb();
        var r3 = await _h.Ingestion(db3).IngestAsync(_h.SimSource(db3), new[] { Harness.NewsItem("d") }, null, default);
        Assert.Equal(1, r3.NotificationsCreated);

        using var check = _h.NewDb();
        Assert.Contains(check.Notifications, n => n.Status == NotificationStatus.Suppressed && n.SuppressedReason!.Contains("cooldown", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Severity_outside_range_is_clamped()
    {
        using var db = _h.NewDb();
        await _h.Ingestion(db).IngestAsync(_h.SimSource(db), new[] { Harness.NewsItem("hi", severity: 99), Harness.NewsItem("lo", severity: -4) }, null, default);

        using var check = _h.NewDb();
        Assert.Equal(new[] { 1, 5 }, check.Events.Select(e => e.Severity).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Event_text_is_stored_as_received_not_pre_escaped()
    {
        using var db = _h.NewDb();
        await _h.Ingestion(db).IngestAsync(_h.SimSource(db), new[] { Harness.NewsItem("a", "<b>Fish & chips</b>") }, null, default);

        using var check = _h.NewDb();
        Assert.Equal("<b>Fish & chips</b>", check.Events.Single().Title);
    }

    [Fact]
    public async Task Timestamps_round_trip_as_utc()
    {
        using var db = _h.NewDb();
        await _h.Ingestion(db).IngestAsync(_h.SimSource(db), new[] { Harness.NewsItem("a") }, null, default);

        using var check = _h.NewDb();
        var e = check.Events.Single();
        Assert.Equal(DateTimeKind.Utc, e.OccurredAt.Kind);
        Assert.Equal(DateTimeKind.Utc, e.IngestedAt.Kind);
        Assert.Equal(new DateTime(2026, 6, 1, 11, 0, 0, DateTimeKind.Utc), e.OccurredAt);
    }
}
