using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EventAlertManagement.Core.Contracts;
using EventAlertManagement.Core.Domain;

namespace EventAlertManagement.Tests.Api;

/// <summary>The whole product through its public API: set up, inject an event, deliver, observe, recover.</summary>
public class PipelineAndAdminApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    public PipelineAndAdminApiTests(ApiFactory f) => _f = f;
    private static string Unique() => $"u-{Guid.NewGuid():N}@example.com";
    private static string Tag() => Guid.NewGuid().ToString("N")[..10];

    private async Task<(HttpClient user, Guid channel, Guid rule, string keyword)> Setup(string channelType = "Fake", int cooldown = 0)
    {
        var user = await _f.UserAsync(Unique());
        var keyword = "kw" + Tag();
        var ch = await user.PostAsJsonAsync("/api/channels", new { type = channelType, name = "c", settings = channelType == "Email" ? (object)new { address = "x@example.com" } : new { } });
        var channelId = (await ch.JsonAsync()).GetProperty("id").GetGuid();
        var rule = await user.PostAsJsonAsync("/api/rules", new
        {
            name = "kw rule", category = "News", minSeverity = 1, filters = new { includeKeywords = new[] { keyword } },
            cooldownSeconds = cooldown, isEnabled = true, channelIds = new[] { channelId },
        });
        Assert.Equal(HttpStatusCode.Created, rule.StatusCode);
        return (user, channelId, (await rule.JsonAsync()).GetProperty("id").GetGuid(), keyword);
    }

    private static Task<HttpResponseMessage> Inject(HttpClient admin, string title, string category = "News", int severity = 3) =>
        admin.PostAsJsonAsync("/api/admin/events/inject", new { category, severity, title, summary = "details", url = "https://example.com/x" });

    [Fact]
    public async Task Injected_event_is_matched_delivered_and_visible_to_user_and_admin()
    {
        var (user, _, _, keyword) = await Setup();
        var admin = await _f.AdminAsync();
        var title = $"Storm warning {keyword}";

        var inject = await Inject(admin, title);
        Assert.Equal(HttpStatusCode.OK, inject.StatusCode);
        var result = await inject.JsonAsync();
        Assert.Equal(1, result.GetProperty("newEvents").GetInt32());
        Assert.Equal(1, result.GetProperty("notificationsCreated").GetInt32());

        await _f.DispatchAsync();

        Assert.Single(_f.Fake.Sent, m => m.Event.Title == title);
        var mine = await (await user.GetAsync("/api/notifications")).JsonAsync();
        var n = Assert.Single(mine.GetProperty("items").EnumerateArray());
        Assert.Equal("Sent", n.GetProperty("status").GetString());
        Assert.Equal(title, n.GetProperty("eventTitle").GetString());

        var adminView = await (await admin.GetAsync($"/api/admin/notifications?status=Sent&pageSize=100")).JsonAsync();
        var row = adminView.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("eventTitle").GetString() == title);
        Assert.Equal(1, row.GetProperty("attempts").GetArrayLength());
    }

    [Fact]
    public async Task Event_that_matches_nothing_creates_no_notification()
    {
        var (user, _, _, _) = await Setup();
        var admin = await _f.AdminAsync();
        var r = await (await Inject(admin, "Completely unrelated headline " + Tag())).JsonAsync();
        Assert.Equal(0, r.GetProperty("notificationsCreated").GetInt32());
        Assert.Empty((await (await user.GetAsync("/api/notifications")).JsonAsync()).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Real_email_channel_receives_an_escaped_message_for_a_hostile_headline()
    {
        var (user, _, _, keyword) = await Setup("Email");
        var admin = await _f.AdminAsync();
        var before = _f.Email.Messages.Count;

        await Inject(admin, $"<script>alert('x')</script> {keyword}");
        await _f.DispatchAsync();

        Assert.Equal(before + 1, _f.Email.Messages.Count);
        var mail = _f.Email.Messages[^1];
        Assert.DoesNotContain("<script>", mail.HtmlBody);
        Assert.Contains("&lt;script&gt;", mail.HtmlBody);
        Assert.Equal("x@example.com", mail.To.Mailboxes.Single().Address);
    }

    [Fact]
    public async Task Failed_delivery_is_visible_with_a_clean_error_and_can_be_retried_by_an_admin()
    {
        var (user, _, _, keyword) = await Setup();
        var admin = await _f.AdminAsync();
        var title = "Failing " + keyword;
        _f.Fake.ForTitle(title, ChannelSendResult.Permanent("rejected by https://hooks.slack.com/services/T0/B0/LEAKYTOKEN"));

        await Inject(admin, title);
        await _f.DispatchAsync();

        var failed = (await (await admin.GetAsync("/api/admin/notifications?status=Failed&pageSize=100")).JsonAsync())
            .GetProperty("items").EnumerateArray().Single(x => x.GetProperty("eventTitle").GetString() == title);
        var id = failed.GetProperty("id").GetGuid();
        var error = failed.GetProperty("attempts")[0].GetProperty("errorMessage").GetString();
        Assert.DoesNotContain("LEAKYTOKEN", error);

        // The user sees the failure too, with the same clean message.
        var mine = (await (await user.GetAsync("/api/notifications")).JsonAsync()).GetProperty("items")[0];
        Assert.Equal("Failed", mine.GetProperty("status").GetString());
        Assert.DoesNotContain("LEAKYTOKEN", mine.ToString());

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/notifications/{id}/retry", null)).StatusCode);
        await _f.DispatchAsync();

        var after = (await (await user.GetAsync("/api/notifications")).JsonAsync()).GetProperty("items")[0];
        Assert.Equal("Sent", after.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/admin/notifications/{id}/retry", null)).StatusCode); // already sent
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/admin/notifications/{Guid.NewGuid()}/retry", null)).StatusCode);
    }

    [Fact]
    public async Task Cooldown_suppression_is_visible_with_its_reason()
    {
        var (user, _, _, keyword) = await Setup(cooldown: 3600);
        var admin = await _f.AdminAsync();
        await Inject(admin, "first " + keyword);
        var second = await (await Inject(admin, "second " + keyword)).JsonAsync();

        Assert.Equal(0, second.GetProperty("notificationsCreated").GetInt32());
        Assert.Equal(1, second.GetProperty("notificationsSuppressed").GetInt32());
        var suppressed = (await (await user.GetAsync("/api/notifications?status=Suppressed")).JsonAsync()).GetProperty("items").EnumerateArray().Single();
        Assert.Contains("cooldown", suppressed.GetProperty("suppressedReason").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Admin_can_switch_a_channel_type_off_and_on_and_users_see_the_effect()
    {
        var (user, _, _, keyword) = await Setup();
        var admin = await _f.AdminAsync();

        // case-insensitive input, canonical output
        var off = await admin.PutAsJsonAsync("/api/admin/channel-types/fake", new { isEnabled = false });
        Assert.Equal("Fake", (await off.JsonAsync()).GetProperty("type").GetString());
        Assert.DoesNotContain("Fake", await (await user.GetAsync("/api/channels/types")).BodyAsync());

        var r = await (await Inject(admin, "while off " + keyword)).JsonAsync();
        Assert.Equal(0, r.GetProperty("notificationsCreated").GetInt32());
        var n = (await (await user.GetAsync("/api/notifications?status=Suppressed")).JsonAsync()).GetProperty("items").EnumerateArray().Single();
        Assert.Contains("administrator", n.GetProperty("suppressedReason").GetString());

        await admin.PutAsJsonAsync("/api/admin/channel-types/Fake", new { isEnabled = true });
        var on = await (await Inject(admin, "while on " + keyword)).JsonAsync();
        Assert.Equal(1, on.GetProperty("notificationsCreated").GetInt32());
    }

    [Fact]
    public async Task Users_only_ever_see_their_own_notifications_and_paging_is_bounded()
    {
        var (alice, _, _, kwA) = await Setup();
        var (bob, _, _, kwB) = await Setup();
        var admin = await _f.AdminAsync();
        await Inject(admin, "for alice " + kwA);
        await Inject(admin, "for bob " + kwB);

        var a = (await (await alice.GetAsync("/api/notifications")).JsonAsync()).GetProperty("items");
        var b = (await (await bob.GetAsync("/api/notifications")).JsonAsync()).GetProperty("items");
        Assert.Contains("alice", Assert.Single(a.EnumerateArray()).GetProperty("eventTitle").GetString());
        Assert.Contains("bob", Assert.Single(b.EnumerateArray()).GetProperty("eventTitle").GetString());

        var paged = await (await alice.GetAsync("/api/notifications?page=-5&pageSize=100000")).JsonAsync();
        Assert.Equal(1, paged.GetProperty("page").GetInt32());
        Assert.Equal(100, paged.GetProperty("pageSize").GetInt32());
    }

    // ---- admin-only behaviour ----

    [Theory]
    [InlineData("{\"category\":\"News\",\"severity\":9,\"title\":\"x\"}")]                                  // severity out of range
    [InlineData("{\"category\":\"News\",\"severity\":3,\"title\":\"\"}")]                                    // empty title
    [InlineData("{\"category\":\"News\",\"severity\":3,\"title\":\"x\",\"url\":\"javascript:alert(1)\"}")]   // unsafe link
    [InlineData("{\"category\":\"Weather\",\"severity\":3,\"title\":\"x\"}")]                                // unknown category
    public async Task Inject_validates_its_input(string body)
    {
        var admin = await _f.AdminAsync();
        var r = await admin.PostAsync("/api/admin/events/inject", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Injected_events_are_marked_simulated_and_can_be_filtered()
    {
        var admin = await _f.AdminAsync();
        var title = "marker " + Tag();
        await Inject(admin, title);
        var events = await (await admin.GetAsync("/api/admin/events?simulated=true&category=News&pageSize=100")).JsonAsync();
        var ev = events.GetProperty("items").EnumerateArray().Single(e => e.GetProperty("title").GetString() == title);
        Assert.True(ev.GetProperty("isSimulated").GetBoolean());
        Assert.Equal("simulator", ev.GetProperty("sourceKey").GetString());
        Assert.DoesNotContain(events.GetProperty("items").EnumerateArray(), e => !e.GetProperty("isSimulated").GetBoolean());
    }

    [Fact]
    public async Task Sources_show_health_and_can_be_polled_and_configured()
    {
        var admin = await _f.AdminAsync();
        var sources = (await (await admin.GetAsync("/api/admin/sources")).JsonAsync()).EnumerateArray().ToList();
        var keys = sources.Select(s => s.GetProperty("key").GetString()).ToList();
        Assert.Contains("simulator", keys);
        Assert.Contains("usgs-earthquakes", keys);

        var sim = sources.Single(s => s.GetProperty("key").GetString() == "simulator").GetProperty("id").GetGuid();
        var polled = await (await admin.PostAsync($"/api/admin/sources/{sim}/poll", null)).JsonAsync();
        Assert.True(polled.GetProperty("success").GetBoolean());
        var health = (await (await admin.GetAsync("/api/admin/sources")).JsonAsync()).EnumerateArray().Single(s => s.GetProperty("key").GetString() == "simulator");
        Assert.Equal("Healthy", health.GetProperty("health").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/admin/sources/{sim}", new { pollIntervalSeconds = 1 })).StatusCode);
        var ok = await admin.PutAsJsonAsync($"/api/admin/sources/{sim}", new { pollIntervalSeconds = 30, isEnabled = true });
        Assert.Equal(30, (await ok.JsonAsync()).GetProperty("pollIntervalSeconds").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/admin/sources/{Guid.NewGuid()}/poll", null)).StatusCode);
    }

    [Fact]
    public async Task A_failing_source_shows_as_failing_in_health_and_the_overview_counts_it()
    {
        var admin = await _f.AdminAsync();
        // The seeded USGS source has no working network in tests: point its adapter at nothing by polling it repeatedly.
        var usgs = (await (await admin.GetAsync("/api/admin/sources")).JsonAsync()).EnumerateArray().Single(s => s.GetProperty("key").GetString() == "usgs-earthquakes").GetProperty("id").GetGuid();
        _f.WithDb(db => { var s = db.Sources.Single(x => x.Id == usgs); s.ConsecutiveFailures = 3; s.LastPollAt = DateTime.UtcNow; s.LastError = "HttpRequestException: could not reach feed"; db.SaveChanges(); return 0; });

        var s = (await (await admin.GetAsync("/api/admin/sources")).JsonAsync()).EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == usgs);
        Assert.Equal("Failing", s.GetProperty("health").GetString());
        var overview = await (await admin.GetAsync("/api/admin/overview")).JsonAsync();
        Assert.True(overview.GetProperty("unhealthySources").GetInt32() >= 1);
    }

    [Fact]
    public async Task Channel_types_lists_registered_types_and_rejects_unknown_ones()
    {
        var admin = await _f.AdminAsync();
        var types = (await (await admin.GetAsync("/api/admin/channel-types")).JsonAsync()).EnumerateArray().Select(t => t.GetProperty("type").GetString()).ToList();
        Assert.Contains("Email", types);
        Assert.Contains("Slack", types);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("/api/admin/channel-types/Pigeon", new { isEnabled = false })).StatusCode);
    }

    [Fact]
    public async Task Admin_lists_show_users_and_rules_without_secrets()
    {
        var (user, _, _, _) = await Setup();
        var admin = await _f.AdminAsync();
        var email = (await (await user.GetAsync("/api/me")).JsonAsync()).GetProperty("email").GetString();

        var users = await (await admin.GetAsync("/api/admin/users")).BodyAsync();
        var rules = await (await admin.GetAsync("/api/admin/rules")).BodyAsync();
        Assert.Contains(email!, users);
        Assert.Contains(email!, rules);
        Assert.DoesNotContain("settings", users + rules, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Overview_reports_counts_and_recent_failures()
    {
        var admin = await _f.AdminAsync();
        var o = await (await admin.GetAsync("/api/admin/overview")).JsonAsync();
        Assert.True(o.GetProperty("users").GetInt32() >= 0);
        Assert.Equal(JsonValueKind.Object, o.GetProperty("notificationsLast24h").ValueKind);
        Assert.Equal(JsonValueKind.Array, o.GetProperty("recentFailures").ValueKind);
    }

    [Fact]
    public async Task Server_errors_never_reveal_internals()
    {
        // A request the server cannot make sense of must not produce a stack trace or exception text.
        var user = await _f.UserAsync(Unique());
        var r = await user.PostAsync("/api/rules", new StringContent("{\"name\":", System.Text.Encoding.UTF8, "application/json"));
        var body = await r.BodyAsync();
        Assert.True((int)r.StatusCode is 400 or 500);
        Assert.DoesNotContain("   at ", body);
        Assert.DoesNotContain("Exception", body);
    }
}
