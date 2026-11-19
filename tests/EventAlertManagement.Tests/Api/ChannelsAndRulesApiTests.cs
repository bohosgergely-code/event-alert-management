using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EventAlertManagement.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace EventAlertManagement.Tests.Api;

public class ChannelsApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    public ChannelsApiTests(ApiFactory f) => _f = f;
    private static string Unique() => $"u-{Guid.NewGuid():N}@example.com";

    private static object EmailChannel(string name = "Work", string address = "person@example.com") =>
        new { type = "Email", name, settings = new { address } };

    private async Task<JsonElement> CreateOk(HttpClient c, object body)
    {
        var r = await c.PostAsJsonAsync("/api/channels", body);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return await r.JsonAsync();
    }

    [Fact]
    public async Task Created_channel_shows_a_masked_address_and_never_the_real_one()
    {
        var c = await _f.UserAsync(Unique());
        var created = await c.PostAsJsonAsync("/api/channels", EmailChannel(address: "secretperson@example.com"));
        var list = await c.GetAsync("/api/channels");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        foreach (var body in new[] { await created.BodyAsync(), await list.BodyAsync() })
        {
            Assert.DoesNotContain("secretperson", body);
            Assert.Contains("s***@example.com", body);
        }
    }

    [Fact]
    public async Task Settings_are_encrypted_in_the_database()
    {
        var c = await _f.UserAsync(Unique());
        var id = (await CreateOk(c, EmailChannel(address: "findme-in-db@example.com"))).GetProperty("id").GetGuid();
        var stored = _f.WithDb(db => db.ChannelConfigs.AsNoTracking().Single(x => x.Id == id).SettingsProtected);
        Assert.DoesNotContain("findme-in-db", stored);
    }

    [Theory]
    [InlineData("Carrier-Pigeon", "{\"address\":\"a@b.com\"}")]                           // unknown type
    [InlineData("Email", "{\"address\":\"not-an-email\"}")]                                // invalid address
    [InlineData("Email", "{}")]                                                           // missing address
    [InlineData("Slack", "{\"webhookUrl\":\"https://evil.example.com/services/a/b/c\"}")]  // wrong host (SSRF)
    [InlineData("Slack", "{\"webhookUrl\":\"http://hooks.slack.com/services/a/b/c\"}")]    // not https
    [InlineData("Slack", "{\"webhookUrl\":\"https://169.254.169.254/services/a\"}")]       // metadata address
    [InlineData("Email", "[1,2,3]")]                                                      // not an object
    [InlineData("Email", "\"text\"")]
    public async Task Invalid_channels_are_rejected_with_400(string type, string settingsJson)
    {
        var c = await _f.UserAsync(Unique());
        var body = $"{{\"type\":\"{type}\",\"name\":\"x\",\"settings\":{settingsJson}}}";
        var r = await c.PostAsync("/api/channels", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Oversized_settings_are_rejected()
    {
        var c = await _f.UserAsync(Unique());
        var r = await c.PostAsJsonAsync("/api/channels", new { type = "Email", name = "x", settings = new { address = "a@b.com", junk = new string('x', 5000) } });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Type_names_are_stored_in_their_canonical_spelling()
    {
        var c = await _f.UserAsync(Unique());
        var created = await CreateOk(c, new { type = "eMaIl", name = "x", settings = new { address = "a@b.com" } });
        Assert.Equal("Email", created.GetProperty("type").GetString());
    }

    [Fact]
    public async Task A_user_can_have_at_most_twenty_channels()
    {
        var c = await _f.UserAsync(Unique());
        for (var i = 0; i < 20; i++) await CreateOk(c, EmailChannel($"c{i}"));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/channels", EmailChannel("one-too-many"))).StatusCode);
    }

    [Fact]
    public async Task Another_users_channel_looks_exactly_like_a_missing_one()
    {
        var alice = await _f.UserAsync(Unique());
        var bob = await _f.UserAsync(Unique());
        var id = (await CreateOk(alice, EmailChannel())).GetProperty("id").GetGuid();
        var missing = Guid.NewGuid();

        foreach (var target in new[] { id, missing })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/channels/{target}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync($"/api/channels/{target}", new { name = "hijack" })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/channels/{target}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsync($"/api/channels/{target}/test", null)).StatusCode);
        }
        Assert.DoesNotContain(id.ToString(), await (await bob.GetAsync("/api/channels")).BodyAsync());
        // Alice's channel is untouched.
        Assert.Equal("Work", (await (await alice.GetAsync($"/api/channels/{id}")).JsonAsync()).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Update_changes_only_what_is_sent_and_revalidates_settings()
    {
        var c = await _f.UserAsync(Unique());
        var id = (await CreateOk(c, EmailChannel("Old", "keep@example.com"))).GetProperty("id").GetGuid();

        var renamed = await c.PutAsJsonAsync($"/api/channels/{id}", new { name = "New" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var body = await renamed.JsonAsync();
        Assert.Equal("New", body.GetProperty("name").GetString());
        Assert.Equal("k***@example.com", body.GetProperty("settingsMasked").GetString()); // settings kept

        var bad = await c.PutAsJsonAsync($"/api/channels/{id}", new { settings = new { address = "nope" } });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"/api/channels/{id}", new { name = "  " })).StatusCode);
    }

    [Fact]
    public async Task A_channel_used_by_a_rule_cannot_be_deleted_until_the_rule_is_gone()
    {
        var c = await _f.UserAsync(Unique());
        var channelId = (await CreateOk(c, EmailChannel())).GetProperty("id").GetGuid();
        var rule = await c.PostAsJsonAsync("/api/rules", new { name = "r", category = "News", minSeverity = 1, filters = new { }, cooldownSeconds = 0, isEnabled = true, channelIds = new[] { channelId } });
        var ruleId = (await rule.JsonAsync()).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/channels/{channelId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/rules/{ruleId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/channels/{channelId}")).StatusCode);
    }

    [Fact]
    public async Task Test_message_goes_through_the_real_channel_and_records_the_result()
    {
        var c = await _f.UserAsync(Unique());
        var id = (await CreateOk(c, EmailChannel(address: "tester@example.com"))).GetProperty("id").GetGuid();
        var before = _f.Email.Messages.Count;

        var r = await c.PostAsync($"/api/channels/{id}/test", null);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.True((await r.JsonAsync()).GetProperty("success").GetBoolean());
        Assert.Equal(before + 1, _f.Email.Messages.Count);
        Assert.Equal("tester@example.com", _f.Email.Messages[^1].To.Mailboxes.Single().Address);
        var shown = await (await c.GetAsync($"/api/channels/{id}")).JsonAsync();
        Assert.Equal("OK", shown.GetProperty("lastTestResult").GetString());
    }

    [Fact]
    public async Task Failed_test_reports_a_sanitized_error_and_testing_is_rate_limited()
    {
        var c = await _f.UserAsync(Unique());
        var id = (await CreateOk(c, new { type = "Fake", name = "f", settings = new { } })).GetProperty("id").GetGuid();
        _f.Fake.Then(ChannelSendResult.Permanent("boom posting to https://hooks.slack.com/services/T0/B0/SECRETTOKEN"));

        var r = await c.PostAsync($"/api/channels/{id}/test", null);
        var body = await r.BodyAsync();

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.DoesNotContain("SECRETTOKEN", body);
        Assert.Contains("\"success\":false", body);
        Assert.Equal((HttpStatusCode)429, (await c.PostAsync($"/api/channels/{id}/test", null)).StatusCode);
    }
}

public class RulesApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    public RulesApiTests(ApiFactory f) => _f = f;
    private static string Unique() => $"u-{Guid.NewGuid():N}@example.com";

    private async Task<Guid> Channel(HttpClient c)
    {
        var r = await c.PostAsJsonAsync("/api/channels", new { type = "Email", name = "ch", settings = new { address = "a@b.com" } });
        return (await r.JsonAsync()).GetProperty("id").GetGuid();
    }

    private static object Rule(Guid[] channels, string category = "News", object? filters = null, int minSeverity = 1, int cooldown = 0, string name = "My rule") =>
        new { name, category, minSeverity, filters = filters ?? new { }, cooldownSeconds = cooldown, isEnabled = true, channelIds = channels };

    [Fact]
    public async Task Rule_lifecycle_create_read_update_delete()
    {
        var c = await _f.UserAsync(Unique());
        var ch = await Channel(c);

        var created = await c.PostAsJsonAsync("/api/rules", Rule(new[] { ch }, "Disasters", new { minMagnitude = 6.0, region = "Japan" }, 2, 600));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var rule = await created.JsonAsync();
        var id = rule.GetProperty("id").GetGuid();
        Assert.Equal("Disasters", rule.GetProperty("category").GetString());
        Assert.Equal(6.0, rule.GetProperty("filters").GetProperty("minMagnitude").GetDouble());

        var updated = await c.PutAsJsonAsync($"/api/rules/{id}", Rule(new[] { ch }, "Disasters", new { minMagnitude = 7.0 }, 3, 60, "Renamed"));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var shown = await (await c.GetAsync($"/api/rules/{id}")).JsonAsync();
        Assert.Equal("Renamed", shown.GetProperty("name").GetString());
        Assert.Equal(7.0, shown.GetProperty("filters").GetProperty("minMagnitude").GetDouble());
        Assert.False(shown.GetProperty("filters").TryGetProperty("region", out var region) && region.ValueKind == JsonValueKind.String);

        Assert.Single((await (await c.GetAsync("/api/rules")).JsonAsync()).EnumerateArray());
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/rules/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/rules/{id}")).StatusCode);
    }

    [Fact]
    public async Task Filters_that_do_not_fit_the_category_are_rejected_not_silently_dropped()
    {
        var c = await _f.UserAsync(Unique());
        var ch = await Channel(c);
        // Without strict parsing this would become a disaster rule with NO filter, matching every earthquake.
        var r = await c.PostAsJsonAsync("/api/rules", Rule(new[] { ch }, "Disasters", new { tickers = new[] { "BTC" } }));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Empty((await (await c.GetAsync("/api/rules")).JsonAsync()).EnumerateArray());
    }

    [Theory]
    [InlineData("News", 0, 0, true)]            // severity too low
    [InlineData("News", 6, 0, true)]            // severity too high
    [InlineData("News", 1, -1, true)]           // negative cooldown
    [InlineData("News", 1, 99999999, true)]     // cooldown beyond 30 days
    [InlineData("News", 1, 0, false)]           // no channel
    [InlineData("Markets", 1, 0, true)]         // markets rule without a ticker
    [InlineData("Weather", 1, 0, true)]         // unknown category
    public async Task Invalid_rules_are_rejected(string category, int severity, int cooldown, bool withChannel)
    {
        var c = await _f.UserAsync(Unique());
        var ch = await Channel(c);
        var body = $"{{\"name\":\"r\",\"category\":\"{category}\",\"minSeverity\":{severity},\"filters\":{{}},\"cooldownSeconds\":{cooldown},\"isEnabled\":true,\"channelIds\":[{(withChannel ? $"\"{ch}\"" : "")}]}}";
        var r = await c.PostAsync("/api/rules", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Filters_must_be_an_object()
    {
        var c = await _f.UserAsync(Unique());
        var ch = await Channel(c);
        var body = $"{{\"name\":\"r\",\"category\":\"News\",\"minSeverity\":1,\"filters\":[1,2],\"cooldownSeconds\":0,\"isEnabled\":true,\"channelIds\":[\"{ch}\"]}}";
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/rules", new StringContent(body, System.Text.Encoding.UTF8, "application/json"))).StatusCode);
    }

    [Fact]
    public async Task A_rule_cannot_use_someone_elses_channel_and_the_error_does_not_reveal_it_exists()
    {
        var alice = await _f.UserAsync(Unique());
        var bob = await _f.UserAsync(Unique());
        var alicesChannel = await Channel(alice);

        var forbidden = await bob.PostAsJsonAsync("/api/rules", Rule(new[] { alicesChannel }));
        var missing = await bob.PostAsJsonAsync("/api/rules", Rule(new[] { Guid.NewGuid() }));

        Assert.Equal(HttpStatusCode.BadRequest, forbidden.StatusCode);
        Assert.Equal(await missing.BodyAsync(), await forbidden.BodyAsync()); // identical answer
    }

    [Fact]
    public async Task Another_users_rule_looks_exactly_like_a_missing_one()
    {
        var alice = await _f.UserAsync(Unique());
        var bob = await _f.UserAsync(Unique());
        var bobsChannel = await Channel(bob);
        var rule = await alice.PostAsJsonAsync("/api/rules", Rule(new[] { await Channel(alice) }));
        var id = (await rule.JsonAsync()).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/rules/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync($"/api/rules/{id}", Rule(new[] { bobsChannel }, name: "hijack"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/rules/{id}")).StatusCode);
        Assert.Empty((await (await bob.GetAsync("/api/rules")).JsonAsync()).EnumerateArray());
        Assert.Equal("My rule", (await (await alice.GetAsync($"/api/rules/{id}")).JsonAsync()).GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_user_can_have_at_most_fifty_rules()
    {
        var c = await _f.UserAsync(Unique());
        var ch = await Channel(c);
        for (var i = 0; i < 50; i++) Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/rules", Rule(new[] { ch }, name: $"r{i}"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/rules", Rule(new[] { ch }, name: "too-many"))).StatusCode);
    }

    [Fact]
    public async Task Every_preset_can_be_turned_into_a_valid_rule_as_is()
    {
        var c = await _f.UserAsync(Unique());
        var ch = await Channel(c);
        var presets = (await (await c.GetAsync("/api/rules/presets")).JsonAsync()).EnumerateArray().ToList();
        Assert.True(presets.Count >= 3);

        foreach (var p in presets)
        {
            var t = p.GetProperty("template");
            var body = new Dictionary<string, object?>
            {
                ["name"] = p.GetProperty("name").GetString(), ["category"] = t.GetProperty("category").GetString(),
                ["minSeverity"] = t.GetProperty("minSeverity").GetInt32(), ["filters"] = t.GetProperty("filters"),
                ["cooldownSeconds"] = t.GetProperty("cooldownSeconds").GetInt32(), ["isEnabled"] = true, ["channelIds"] = new[] { ch },
            };
            var r = await c.PostAsJsonAsync("/api/rules", body);
            Assert.True(r.StatusCode == HttpStatusCode.Created, $"preset '{p.GetProperty("name").GetString()}' gave {(int)r.StatusCode}: {await r.BodyAsync()}");
        }
    }

    [Fact]
    public async Task Preview_counts_matches_among_stored_events_and_changes_nothing()
    {
        var c = await _f.UserAsync(Unique());
        var admin = await _f.AdminAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        foreach (var (title, mag) in new[] { ($"Preview quake A {tag}", 7.2), ($"Preview quake B {tag}", 5.1), ($"Preview quake C {tag}", 6.5) })
            Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/admin/events/inject", new { category = "Disasters", severity = 3, title, magnitude = mag, location = $"Preview land {tag}", eventType = "earthquake" })).StatusCode);
        var notificationsBefore = _f.WithDb(db => db.Notifications.Count());

        var r = await c.PostAsJsonAsync("/api/rules/preview", Rule(Array.Empty<Guid>(), "Disasters", new { minMagnitude = 6.0, region = $"preview land {tag}" }));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.JsonAsync();
        Assert.Equal(2, body.GetProperty("matches").GetInt32());
        Assert.Equal(new[] { $"Preview quake A {tag}", $"Preview quake C {tag}" }, body.GetProperty("samples").EnumerateArray().Select(s => s.GetProperty("title").GetString()!).OrderBy(x => x).ToArray());
        Assert.Equal(notificationsBefore, _f.WithDb(db => db.Notifications.Count()));
        Assert.Empty((await (await c.GetAsync("/api/rules")).JsonAsync()).EnumerateArray());
    }
}
