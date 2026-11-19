using System.Net;
using System.Text.Json;
using EventAlertManagement.Core.Contracts;
using EventAlertManagement.Core.Domain;
using EventAlertManagement.Infrastructure.Channels;
using EventAlertManagement.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace EventAlertManagement.Tests.Integration;

/// <summary>
/// The extensibility proof (ADR-004): the real ingestion and dispatcher, which were written before this channel
/// existed and were not changed for it, deliver through the new Webhook channel. Only the HTTP wire is replaced.
/// </summary>
public class WebhookPipelineTests : IDisposable
{
    private const string Secret = "0123456789abcdef";
    private const string Url = "https://hooks.example.com/in/TOKEN-IN-URL";
    private readonly Harness _h = new();
    public void Dispose() => _h.Dispose();

    private sealed class Wire(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler, IHttpClientFactory
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = new();
        public HttpClient CreateClient(string name) => new(this);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls.Add((r, await r.Content!.ReadAsStringAsync(ct)));
            return respond(r);
        }
    }

    private (ChannelRegistry Registry, Wire Wire) Registry(Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        var wire = new Wire(respond ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)));
        return (new ChannelRegistry(new INotificationChannel[] { new WebhookChannel(wire, _h.Clock) }), wire);
    }

    private async Task<Guid> Arrange(string title = "Big storm hits coast")
    {
        using var db = _h.NewDb();
        var u = _h.AddUser(db);
        var ch = _h.AddChannel(db, u, "Webhook", settings: JsonSerializer.Serialize(new { url = Url, secret = Secret }));
        _h.AddRule(db, u, EventCategory.News, null, 1, 0, ch);
        await _h.Ingestion(db).IngestAsync(_h.SimSource(db), new[] { Harness.NewsItem("w1", title) }, null, default);
        return db.Notifications.AsNoTracking().Single().Id;
    }

    private Notification Load(Guid id) { using var db = _h.NewDb(); return db.Notifications.AsNoTracking().Include(n => n.Attempts).Single(n => n.Id == id); }
    private async Task Dispatch(ChannelRegistry r) { using var db = _h.NewDb(); await _h.Dispatcher(db, r).RunOnceAsync(10, default); }

    [Fact]
    public async Task An_event_flows_from_matching_to_a_signed_post_without_any_change_to_the_pipeline()
    {
        var id = await Arrange("Storm warning for the coast");
        var (registry, wire) = Registry();

        await Dispatch(registry);

        Assert.Equal(NotificationStatus.Sent, Load(id).Status);
        var (request, body) = Assert.Single(wire.Calls);
        Assert.Equal(Url, request.RequestUri!.ToString());
        Assert.Equal("Storm warning for the coast", JsonDocument.Parse(body).RootElement.GetProperty("title").GetString());
        Assert.StartsWith("sha256=", request.Headers.GetValues("X-EventAlert-Signature").Single());
    }

    [Fact]
    public async Task A_server_error_is_retried_with_backoff_and_then_succeeds()
    {
        var id = await Arrange();
        var calls = 0;
        var (registry, wire) = Registry(_ => new HttpResponseMessage(++calls == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));

        await Dispatch(registry);
        var n = Load(id);
        Assert.Equal(NotificationStatus.Retrying, n.Status);
        Assert.Equal(_h.Clock.UtcNow.AddMinutes(1), n.NextAttemptAt);

        _h.Clock.Advance(TimeSpan.FromMinutes(1));
        await Dispatch(registry);
        Assert.Equal(NotificationStatus.Sent, Load(id).Status);
        Assert.Equal(2, wire.Calls.Count);
    }

    [Fact]
    public async Task A_missing_endpoint_fails_at_once_and_the_stored_error_does_not_contain_the_url_or_secret()
    {
        var id = await Arrange();
        var (registry, wire) = Registry(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await Dispatch(registry);

        var n = Load(id);
        Assert.Equal(NotificationStatus.Failed, n.Status);
        var error = n.Attempts.Single().ErrorMessage!;
        Assert.DoesNotContain("TOKEN-IN-URL", error);
        Assert.DoesNotContain(Secret, error);
        _h.Clock.Advance(TimeSpan.FromDays(1));
        await Dispatch(registry);
        Assert.Single(wire.Calls); // permanent failures are not retried
    }

    [Fact]
    public async Task An_address_that_turns_out_to_be_internal_is_failed_once_and_never_retried()
    {
        var id = await Arrange();
        var (registry, wire) = Registry(_ => throw new HttpRequestException("blocked", new BlockedAddressException("internal")));

        await Dispatch(registry);
        _h.Clock.Advance(TimeSpan.FromDays(1));
        await Dispatch(registry);

        Assert.Equal(NotificationStatus.Failed, Load(id).Status);
        Assert.Single(wire.Calls); // no retry storm against an internal address
    }

    [Fact]
    public async Task Settings_that_became_invalid_are_failed_without_any_request()
    {
        // A settings record that bypassed validation (for example an old row) must still never be sent.
        using (var db = _h.NewDb())
        {
            var u = _h.AddUser(db);
            var ch = _h.AddChannel(db, u, "Webhook", settings: JsonSerializer.Serialize(new { url = "https://127.0.0.1/x", secret = Secret }));
            _h.AddRule(db, u, EventCategory.News, null, 1, 0, ch);
            await _h.Ingestion(db).IngestAsync(_h.SimSource(db), new[] { Harness.NewsItem("w2") }, null, default);
        }
        var (registry, wire) = Registry();

        await Dispatch(registry);

        Assert.Empty(wire.Calls);
        using var check = _h.NewDb();
        Assert.Equal(NotificationStatus.Failed, check.Notifications.Single().Status);
    }
}
