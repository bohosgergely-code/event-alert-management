using EventAlertManagement.Core.Contracts;
using EventAlertManagement.Core.Domain;
using EventAlertManagement.Core.Matching;
using EventAlertManagement.Infrastructure.Channels;
using EventAlertManagement.Infrastructure.Dispatch;
using Microsoft.EntityFrameworkCore;

namespace EventAlertManagement.Tests.Integration;

/// <summary>
/// Each test pins down a defect that an independent reviewer reported and that was reproduced before it was fixed
/// (evidence/independent-review). They fail on the code as it was.
/// </summary>
public class IndependentReviewRegressionTests : IDisposable
{
    private readonly Harness _h = new();
    public void Dispose() => _h.Dispose();

    private (Guid rule, Guid channel) Setup(int cooldown = 0)
    {
        using var db = _h.NewDb();
        var u = _h.AddUser(db); var ch = _h.AddChannel(db, u);
        var r = _h.AddRule(db, u, EventCategory.News, null, 1, cooldown, ch);
        return (r.Id, ch.Id);
    }

    private static DateTime At(int h, int m) => new(2026, 6, 1, h, m, 0, DateTimeKind.Utc);

    // ---- cooldown must not hide the more important event ----

    [Fact]
    public async Task In_one_batch_the_most_severe_event_gets_through_the_cooldown_not_the_earliest()
    {
        Setup(cooldown: 3600);
        using var db = _h.NewDb();
        await _h.Ingestion(db).IngestAsync(_h.SimSource(db), new[]
        {
            Harness.NewsItem("minor", "minor thing", severity: 1, at: At(11, 0)),
            Harness.NewsItem("big", "CATASTROPHE", severity: 5, at: At(11, 5)),
        }, null, default);

        using var check = _h.NewDb();
        var byTitle = check.Notifications.Include(n => n.Event).ToDictionary(n => n.Event!.Title, n => n.Status);
        Assert.Equal(NotificationStatus.Pending, byTitle["CATASTROPHE"]);
        Assert.Equal(NotificationStatus.Suppressed, byTitle["minor thing"]);
    }

    [Fact]
    public async Task A_more_severe_event_later_escalates_through_a_running_cooldown_but_an_equal_or_lower_one_does_not()
    {
        Setup(cooldown: 3600);
        async Task<IngestSummary> Ingest(string id, int severity)
        {
            using var db = _h.NewDb();
            var r = await _h.Ingestion(db).IngestAsync(_h.SimSource(db), new[] { Harness.NewsItem(id, id, severity) }, null, default);
            return new(r.NotificationsCreated, r.NotificationsSuppressed);
        }

        Assert.Equal(new IngestSummary(1, 0), await Ingest("first", 2));      // starts the window at severity 2
        _h.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(new IngestSummary(0, 1), await Ingest("same", 2));       // not worse: held back
        Assert.Equal(new IngestSummary(0, 1), await Ingest("lower", 1));      // less severe: held back
        Assert.Equal(new IngestSummary(1, 0), await Ingest("worse", 4));      // more severe: escalates
        Assert.Equal(new IngestSummary(0, 1), await Ingest("again", 3));      // below the new level of 4: held back
    }

    private sealed record IngestSummary(int Created, int Suppressed);

    [Fact]
    public void Cooldown_policy_unit_rules_for_escalation()
    {
        var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var rule = new AlertRule { CooldownSeconds = 600, LastNotifiedAt = now, LastNotifiedSeverity = 3 };
        Assert.True(CooldownPolicy.IsInCooldown(rule, now.AddMinutes(1), out _, eventSeverity: 3));
        Assert.True(CooldownPolicy.IsInCooldown(rule, now.AddMinutes(1), out _, eventSeverity: 2));
        Assert.False(CooldownPolicy.IsInCooldown(rule, now.AddMinutes(1), out _, eventSeverity: 4));
        Assert.True(CooldownPolicy.IsInCooldown(rule, now.AddMinutes(1), out _));   // severity unknown: old behaviour
    }

    // ---- queued alerts must follow the current state of the rule and the channel ----

    [Fact]
    public async Task A_rule_switched_off_after_an_alert_was_queued_does_not_send_it()
    {
        Setup();
        using (var db = _h.NewDb()) await _h.Ingestion(db).IngestAsync(_h.SimSource(db), new[] { Harness.NewsItem("a") }, null, default);
        using (var db = _h.NewDb()) db.AlertRules.ExecuteUpdate(s => s.SetProperty(r => r.IsEnabled, false));

        using (var db = _h.NewDb()) await _h.Dispatcher(db).RunOnceAsync(10, default);

        Assert.Empty(_h.Channel.Sent);
        using var check = _h.NewDb();
        var n = check.Notifications.Single();
        Assert.Equal(NotificationStatus.Suppressed, n.Status);
        Assert.Contains("rule was switched off", n.SuppressedReason);
    }

    // ---- two things happening to the same item at once ----

    [Fact]
    public async Task Two_overlapping_ingests_of_the_same_item_both_succeed_and_store_it_once()
    {
        Setup();
        var failures = 0;
        for (var round = 0; round < 25; round++)
        {
            var item = Harness.NewsItem("race" + round);
            var outcomes = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
            {
                try { using var db = _h.NewDb(); return (await _h.Ingestion(db).IngestAsync(_h.SimSource(db), new[] { item }, null, default)).NewEvents; }
                catch (Exception) { return -1; }
            })));
            if (outcomes.Contains(-1)) failures++;
            Assert.Equal(1, outcomes.Sum(o => Math.Max(o, 0))); // exactly one of them stored it
        }
        Assert.Equal(0, failures);
        using var check = _h.NewDb();
        Assert.Equal(25, check.Events.Count());
        Assert.Equal(25, check.Notifications.Count());
    }

    // ---- a deletion in the middle of a delivery ----

    [Fact]
    public async Task Deleting_a_rule_while_its_alert_is_being_sent_does_not_abort_the_rest_of_the_batch()
    {
        Guid ruleA, ruleB;
        using (var db = _h.NewDb()) { var u = _h.AddUser(db, "a@example.com"); var ch = _h.AddChannel(db, u); ruleA = _h.AddRule(db, u, EventCategory.News, new EventAlertManagement.Core.Filters.NewsFilters { IncludeKeywords = { "alpha" } }, 1, 0, ch).Id; }
        using (var db = _h.NewDb()) { var u = _h.AddUser(db, "b@example.com"); var ch = _h.AddChannel(db, u); ruleB = _h.AddRule(db, u, EventCategory.News, new EventAlertManagement.Core.Filters.NewsFilters { IncludeKeywords = { "beta" } }, 1, 0, ch).Id; }
        using (var db = _h.NewDb()) await _h.Ingestion(db).IngestAsync(_h.SimSource(db), new[]
        {
            Harness.NewsItem("1", "alpha first", at: At(10, 0)), Harness.NewsItem("2", "beta second", at: At(10, 1)),
        }, null, default);

        var gate = new TaskCompletionSource(); var reached = new TaskCompletionSource();
        var slow = new GatedChannel(reached, gate);
        var registry = new ChannelRegistry(new INotificationChannel[] { slow });
        var run = Task.Run(async () => { using var db = _h.NewDb(); return await _h.Dispatcher(db, registry).RunOnceAsync(10, default); });
        await reached.Task;                                    // one alert is being sent right now (which one is not fixed)
        var inFlight = slow.FirstTitle!.Contains("alpha") ? ruleA : ruleB;
        using (var db = _h.NewDb()) db.AlertRules.Where(r => r.Id == inFlight).ExecuteDelete();
        gate.SetResult();

        var handled = await run;                               // must not throw
        Assert.Equal(2, handled);
        Assert.Equal(2, slow.Sent);                            // the second alert was still delivered
    }

    private sealed class GatedChannel(TaskCompletionSource reached, TaskCompletionSource gate) : INotificationChannel
    {
        private int _calls; public int Sent => _calls;
        public string? FirstTitle { get; private set; }
        public string Type => "Fake";
        public IReadOnlyList<string> ValidateSettings(string s) => Array.Empty<string>();
        public string MaskSettings(string s) => "";
        public async Task<ChannelSendResult> SendAsync(NotificationMessage m, string s, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _calls) == 1) { FirstTitle = m.Event.Title; reached.TrySetResult(); await gate.Task; }
            return ChannelSendResult.Ok();
        }
    }

    // ---- history after a manual retry ----

    [Fact]
    public async Task Attempt_numbers_keep_counting_across_a_manual_retry()
    {
        Setup(); _h.Channel.Then(ChannelSendResult.Permanent("nope"));
        using (var db = _h.NewDb()) await _h.Ingestion(db).IngestAsync(_h.SimSource(db), new[] { Harness.NewsItem("a") }, null, default);
        using (var db = _h.NewDb()) await _h.Dispatcher(db).RunOnceAsync(10, default);
        Guid id; using (var db = _h.NewDb()) id = db.Notifications.Single().Id;
        using (var db = _h.NewDb()) Assert.True(await new NotificationRetryService(db, _h.Clock).RetryAsync(id, default));
        using (var db = _h.NewDb()) await _h.Dispatcher(db).RunOnceAsync(10, default);

        using var check = _h.NewDb();
        var numbers = check.DeliveryAttempts.OrderBy(a => a.StartedAt).ThenBy(a => a.AttemptNumber).Select(a => a.AttemptNumber).ToList();
        Assert.Equal(new[] { 1, 2 }, numbers);
        Assert.Equal(NotificationStatus.Sent, check.Notifications.Single().Status);
    }
}
