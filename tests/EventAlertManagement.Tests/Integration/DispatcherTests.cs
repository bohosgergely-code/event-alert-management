using EventAlertManagement.Core.Contracts;
using EventAlertManagement.Core.Domain;
using EventAlertManagement.Infrastructure.Channels;
using EventAlertManagement.Infrastructure.Dispatch;
using Microsoft.EntityFrameworkCore;

namespace EventAlertManagement.Tests.Integration;

public class DispatcherTests : IDisposable
{
    private readonly Harness _h = new();
    public void Dispose() => _h.Dispose();

    /// <summary>One user, one channel, one rule, one matching event already turned into a Pending notification.</summary>
    private async Task<Guid> PendingNotification(string channelType = "Fake", bool channelEnabled = true)
    {
        using var db = _h.NewDb();
        var u = _h.AddUser(db);
        var ch = _h.AddChannel(db, u, channelType, channelEnabled);
        _h.AddRule(db, u, EventCategory.News, null, 1, 0, ch);
        await _h.Ingestion(db).IngestAsync(_h.SimSource(db), new[] { Harness.NewsItem("a") }, null, default);
        return db.Notifications.AsNoTracking().Single().Id;
    }

    private async Task<int> Run(IChannelRegistry? registry = null)
    {
        using var db = _h.NewDb();
        return await _h.Dispatcher(db, registry).RunOnceAsync(10, default);
    }

    private Notification Load(Guid id)
    {
        using var db = _h.NewDb();
        return db.Notifications.AsNoTracking().Include(n => n.Attempts).Single(n => n.Id == id);
    }

    [Fact]
    public async Task Successful_send_marks_notification_sent_and_records_one_attempt()
    {
        var id = await PendingNotification();
        Assert.Equal(1, await Run());

        var n = Load(id);
        Assert.Equal(NotificationStatus.Sent, n.Status);
        Assert.NotNull(n.SentAt);
        Assert.Single(n.Attempts);
        Assert.Equal(AttemptOutcome.Success, n.Attempts[0].Outcome);
        Assert.Single(_h.Channel.Sent);
        Assert.Equal("Big storm hits coast", _h.Channel.Sent[0].Event.Title);
    }

    [Fact]
    public async Task Sent_notification_is_never_sent_again()
    {
        await PendingNotification();
        await Run();
        Assert.Equal(0, await Run());
        Assert.Single(_h.Channel.Sent);
    }

    [Fact]
    public async Task Transient_failure_retries_with_backoff_and_ends_failed_after_max_attempts()
    {
        _h.Channel.Then(ChannelSendResult.Transient("down")).Then(ChannelSendResult.Transient("down")).Then(ChannelSendResult.Transient("down"));
        var id = await PendingNotification();

        await Run(); // attempt 1
        var n = Load(id);
        Assert.Equal(NotificationStatus.Retrying, n.Status);
        Assert.Equal(_h.Clock.UtcNow.AddMinutes(1), n.NextAttemptAt);

        Assert.Equal(0, await Run()); // not due yet: nothing happens
        Assert.Single(_h.Channel.Sent);

        _h.Clock.Advance(TimeSpan.FromMinutes(1)); await Run(); // attempt 2
        n = Load(id);
        Assert.Equal(NotificationStatus.Retrying, n.Status);
        Assert.Equal(_h.Clock.UtcNow.AddMinutes(5), n.NextAttemptAt);

        _h.Clock.Advance(TimeSpan.FromMinutes(5)); await Run(); // attempt 3 = MaxAttempts
        n = Load(id);
        Assert.Equal(NotificationStatus.Failed, n.Status);
        Assert.Equal(3, n.AttemptCount);
        Assert.Equal(3, n.Attempts.Count);
        Assert.Equal(3, _h.Channel.Sent.Count);

        _h.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, await Run()); // failed stays failed
    }

    [Fact]
    public async Task Transient_failure_followed_by_success_ends_sent()
    {
        _h.Channel.Then(ChannelSendResult.Transient("blip"));
        var id = await PendingNotification();

        await Run();
        _h.Clock.Advance(TimeSpan.FromMinutes(1));
        await Run();

        var n = Load(id);
        Assert.Equal(NotificationStatus.Sent, n.Status);
        Assert.Equal(new[] { AttemptOutcome.TransientFailure, AttemptOutcome.Success }, n.Attempts.OrderBy(a => a.AttemptNumber).Select(a => a.Outcome));
    }

    [Fact]
    public async Task Permanent_failure_fails_immediately_without_retry()
    {
        _h.Channel.Then(ChannelSendResult.Permanent("webhook removed"));
        var id = await PendingNotification();

        await Run();
        var n = Load(id);
        Assert.Equal(NotificationStatus.Failed, n.Status);
        Assert.Null(n.NextAttemptAt);

        _h.Clock.Advance(TimeSpan.FromDays(1));
        await Run();
        Assert.Single(_h.Channel.Sent);
    }

    [Fact]
    public async Task Manual_retry_revives_a_failed_notification_but_not_a_sent_one()
    {
        _h.Channel.Then(ChannelSendResult.Permanent("nope"));
        var id = await PendingNotification();
        await Run();

        using (var db = _h.NewDb())
        {
            Assert.True(await new NotificationRetryService(db, _h.Clock).RetryAsync(id, default));
        }
        Assert.Equal(NotificationStatus.Pending, Load(id).Status);

        await Run();
        Assert.Equal(NotificationStatus.Sent, Load(id).Status);
        Assert.Equal(2, Load(id).Attempts.Count);

        using var db2 = _h.NewDb();
        Assert.False(await new NotificationRetryService(db2, _h.Clock).RetryAsync(id, default));
    }

    [Fact]
    public async Task Unknown_channel_type_fails_permanently()
    {
        var id = await PendingNotification(channelType: "Carrier-Pigeon");
        await Run();
        var n = Load(id);
        Assert.Equal(NotificationStatus.Failed, n.Status);
        Assert.Contains("Unknown channel", n.Attempts.Single().ErrorMessage);
    }

    [Fact]
    public async Task Channel_disabled_after_matching_is_not_sent_to()
    {
        var id = await PendingNotification();
        using (var db = _h.NewDb())
        {
            db.ChannelConfigs.ExecuteUpdate(s => s.SetProperty(c => c.IsEnabled, false));
        }
        await Run();
        var n = Load(id);
        Assert.Equal(NotificationStatus.Suppressed, n.Status); // a choice someone made, not a delivery fault
        Assert.Contains("disabled", n.SuppressedReason);
        Assert.Empty(n.Attempts);
        Assert.Empty(_h.Channel.Sent);
    }

    [Fact]
    public async Task Unexpected_exception_is_transient_and_its_message_is_not_stored()
    {
        _h.Channel.ThenThrow(new InvalidOperationException("boom calling https://hooks.slack.com/services/T000/B000/SECRETTOKEN"));
        var id = await PendingNotification();

        await Run();
        var n = Load(id);
        Assert.Equal(NotificationStatus.Retrying, n.Status);
        var error = n.Attempts.Single().ErrorMessage!;
        Assert.DoesNotContain("SECRETTOKEN", error);
        Assert.DoesNotContain("hooks.slack.com", error);
    }

    [Fact]
    public async Task Channel_error_text_with_a_url_is_sanitized_before_storage()
    {
        _h.Channel.Then(ChannelSendResult.Permanent("failed posting to https://hooks.slack.com/services/T0/B0/TOKEN123 today"));
        var id = await PendingNotification();
        await Run();
        var error = Load(id).Attempts.Single().ErrorMessage!;
        Assert.DoesNotContain("TOKEN123", error);
        Assert.Contains("[url removed]", error);
    }

    [Fact]
    public async Task Expired_lease_is_reclaimed_but_a_live_lease_is_respected()
    {
        var id = await PendingNotification();
        using (var db = _h.NewDb())
        {
            var future = _h.Clock.UtcNow.AddMinutes(2);
            db.Notifications.ExecuteUpdate(s => s.SetProperty(n => n.Status, NotificationStatus.Sending).SetProperty(n => n.LeaseUntil, future));
        }
        Assert.Equal(0, await Run()); // another dispatcher still owns it

        _h.Clock.Advance(TimeSpan.FromMinutes(3)); // lease expired: that dispatcher presumably crashed
        Assert.Equal(1, await Run());
        Assert.Equal(NotificationStatus.Sent, Load(id).Status);
    }

    [Fact]
    public async Task Two_dispatchers_running_at_once_send_each_notification_only_once()
    {
        using (var db = _h.NewDb())
        {
            var u = _h.AddUser(db);
            var ch = _h.AddChannel(db, u);
            _h.AddRule(db, u, EventCategory.News, null, 1, 0, ch);
            var items = Enumerable.Range(0, 15).Select(i => Harness.NewsItem($"e{i}")).ToArray();
            await _h.Ingestion(db).IngestAsync(_h.SimSource(db), items, null, default);
        }

        // Each competing dispatcher keeps going until nothing is due. Batch size is 10, so several rounds are needed.
        var all = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            var total = 0; int n;
            while ((n = await Run()) > 0) total += n;
            return total;
        })));

        Assert.Equal(15, _h.Channel.Sent.Count);
        Assert.Equal(15, all.Sum());
        using var check = _h.NewDb();
        Assert.All(check.Notifications, n => Assert.Equal(NotificationStatus.Sent, n.Status));
    }

    [Fact]
    public async Task Stored_channel_settings_are_encrypted_at_rest()
    {
        await PendingNotification();
        using var db = _h.NewDb();
        var stored = db.ChannelConfigs.AsNoTracking().Single().SettingsProtected;
        Assert.DoesNotContain("super-secret-value", stored);
        Assert.Contains("super-secret-value", _h.Protector.Unprotect(stored));
    }

    [Fact]
    public async Task A_third_channel_can_be_added_without_touching_the_pipeline()
    {
        // The extensibility claim in ADR-004: a new channel is one class plus registration.
        var webhook = new FakeChannel("Webhook");
        var id = await PendingNotification(channelType: "Webhook");

        await Run(new ChannelRegistry(new INotificationChannel[] { _h.Channel, webhook }));

        Assert.Single(webhook.Sent);
        Assert.Empty(_h.Channel.Sent);
        Assert.Equal(NotificationStatus.Sent, Load(id).Status);
    }
}

public class ChannelTypeSwitchTests : IDisposable
{
    private readonly Harness _h = new();
    public void Dispose() => _h.Dispose();

    private void SwitchOff(string type)
    {
        using var db = _h.NewDb();
        db.ChannelTypeSettings.Add(new ChannelTypeSetting { Type = type, IsEnabled = false, UpdatedAt = _h.Clock.UtcNow });
        db.SaveChanges();
    }

    private async Task Setup()
    {
        using var db = _h.NewDb();
        var u = _h.AddUser(db);
        _h.AddRule(db, u, EventCategory.News, null, 1, 0, _h.AddChannel(db, u, "Fake"));
    }

    [Fact]
    public async Task Switched_off_type_suppresses_new_matches_with_a_visible_reason()
    {
        await Setup(); SwitchOff("Fake");
        using var db = _h.NewDb();
        var r = await _h.Ingestion(db).IngestAsync(_h.SimSource(db), new[] { Harness.NewsItem("a") }, null, default);

        Assert.Equal(0, r.NotificationsCreated);
        using var check = _h.NewDb();
        var n = check.Notifications.Single();
        Assert.Equal(NotificationStatus.Suppressed, n.Status);
        Assert.Contains("administrator", n.SuppressedReason);
    }

    [Fact]
    public async Task Type_switched_off_after_matching_is_held_back_and_can_be_sent_on_request_once_switched_on()
    {
        await Setup();
        using (var db = _h.NewDb()) await _h.Ingestion(db).IngestAsync(_h.SimSource(db), new[] { Harness.NewsItem("a") }, null, default);
        SwitchOff("fake"); // type comparison must not depend on letter case in the switch table

        using (var db = _h.NewDb()) await _h.Dispatcher(db).RunOnceAsync(10, default);
        Assert.Empty(_h.Channel.Sent);

        using (var db = _h.NewDb())
        {
            var n = db.Notifications.Single();
            Assert.Equal(NotificationStatus.Suppressed, n.Status);          // same status and wording as ingestion uses
            Assert.Contains("administrator", n.SuppressedReason);
            db.ChannelTypeSettings.ExecuteDelete();
            Assert.True(await new EventAlertManagement.Infrastructure.Dispatch.NotificationRetryService(db, _h.Clock).RetryAsync(n.Id, default));
        }
        using (var db = _h.NewDb()) await _h.Dispatcher(db).RunOnceAsync(10, default);
        Assert.Single(_h.Channel.Sent);
    }

}
