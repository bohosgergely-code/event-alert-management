using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace EventAlertManagement.Tests.Api;

/// <summary>The third channel through the public API. Nothing in the API code mentions it: the registry makes it appear.</summary>
public class WebhookApiTests : IClassFixture<ApiFactory>
{
    private const string Secret = "0123456789abcdef-secret";
    private readonly ApiFactory _f;
    public WebhookApiTests(ApiFactory f) => _f = f;
    private static string Unique() => $"u-{Guid.NewGuid():N}@example.com";

    private static object Body(string url, string? secret = Secret, string name = "My hook") =>
        new { type = "Webhook", name, settings = new { url, secret } };

    [Fact]
    public async Task Webhook_is_offered_to_users_and_to_admins_automatically()
    {
        var user = await _f.UserAsync(Unique());
        var admin = await _f.AdminAsync();

        Assert.Contains("Webhook", await (await user.GetAsync("/api/channels/types")).BodyAsync());
        var types = (await (await admin.GetAsync("/api/admin/channel-types")).JsonAsync()).EnumerateArray().Select(t => t.GetProperty("type").GetString()).ToList();
        Assert.Contains("Webhook", types);
    }

    [Fact]
    public async Task A_valid_webhook_is_created_and_neither_the_secret_nor_the_path_is_ever_shown()
    {
        var user = await _f.UserAsync(Unique());
        var created = await user.PostAsJsonAsync("/api/channels", Body("https://hooks.example.com/in/PATH-TOKEN-123"));
        var list = await user.GetAsync("/api/channels");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        foreach (var text in new[] { await created.BodyAsync(), await list.BodyAsync() })
        {
            Assert.Contains("https://hooks.example.com/… (signed)", text);
            Assert.DoesNotContain("PATH-TOKEN-123", text);
            Assert.DoesNotContain(Secret, text);
        }
    }

    [Fact]
    public async Task Settings_are_stored_encrypted()
    {
        var user = await _f.UserAsync(Unique());
        var id = (await (await user.PostAsJsonAsync("/api/channels", Body("https://hooks.example.com/in/ENCRYPT-ME"))).JsonAsync()).GetProperty("id").GetGuid();
        var stored = _f.WithDb(db => db.ChannelConfigs.AsNoTracking().Single(c => c.Id == id).SettingsProtected);
        Assert.DoesNotContain("ENCRYPT-ME", stored);
        Assert.DoesNotContain(Secret, stored);
    }

    [Theory]
    [InlineData("http://hooks.example.com/x")]
    [InlineData("https://localhost/x")]
    [InlineData("https://127.0.0.1/x")]
    [InlineData("https://2130706433/x")]
    [InlineData("https://[::1]/x")]
    [InlineData("https://[::ffff:127.0.0.1]/x")]
    [InlineData("https://169.254.169.254/latest/meta-data/")]
    [InlineData("https://10.0.0.5/x")]
    [InlineData("https://intranet/x")]
    [InlineData("https://user:pass@hooks.example.com/x")]
    public async Task Addresses_that_reach_into_a_network_are_refused_with_a_clear_message(string url)
    {
        var user = await _f.UserAsync(Unique());
        var r = await user.PostAsJsonAsync("/api/channels", Body(url));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("webhook URL", await r.BodyAsync());
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("too-short")]
    public async Task A_missing_or_short_signing_secret_is_refused(string? secret)
    {
        var user = await _f.UserAsync(Unique());
        var r = await user.PostAsJsonAsync("/api/channels", Body("https://hooks.example.com/x", secret));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("secret", await r.BodyAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Another_users_webhook_looks_like_a_missing_one_and_can_be_updated_only_by_its_owner()
    {
        var alice = await _f.UserAsync(Unique());
        var bob = await _f.UserAsync(Unique());
        var id = (await (await alice.PostAsJsonAsync("/api/channels", Body("https://hooks.example.com/a"))).JsonAsync()).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/channels/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsync($"/api/channels/{id}/test", null)).StatusCode);

        // The owner changing the address is validated again, so a valid hook cannot be edited into an internal one.
        var edit = await alice.PutAsJsonAsync($"/api/channels/{id}", new { settings = new { url = "https://169.254.169.254/x", secret = Secret } });
        Assert.Equal(HttpStatusCode.BadRequest, edit.StatusCode);
    }

    [Fact]
    public async Task Admins_can_switch_the_webhook_type_off_for_everyone()
    {
        var user = await _f.UserAsync(Unique());
        var admin = await _f.AdminAsync();
        try
        {
            await admin.PutAsJsonAsync("/api/admin/channel-types/webhook", new { isEnabled = false });
            Assert.DoesNotContain("Webhook", await (await user.GetAsync("/api/channels/types")).BodyAsync());
        }
        finally { await admin.PutAsJsonAsync("/api/admin/channel-types/Webhook", new { isEnabled = true }); }
        Assert.Contains("Webhook", await (await user.GetAsync("/api/channels/types")).BodyAsync());
    }
}
