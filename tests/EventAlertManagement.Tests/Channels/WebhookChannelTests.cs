using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EventAlertManagement.Core.Contracts;
using EventAlertManagement.Core.Domain;
using EventAlertManagement.Infrastructure.Channels;
using EventAlertManagement.Infrastructure.Security;
using EventAlertManagement.Tests.Integration;

namespace EventAlertManagement.Tests.Channels;

public class PublicAddressPolicyTests
{
    // Public cases and the "is it global" answers come from an independent Python check (ipaddress module),
    // except where noted: Python calls multicast and NAT64 addresses global, which would be unsafe here.
    [Theory]
    [InlineData("8.8.8.8")] [InlineData("1.1.1.1")] [InlineData("93.184.216.34")] [InlineData("172.32.0.1")] [InlineData("172.15.255.255")]
    [InlineData("2606:4700:4700::1111")] [InlineData("2001:4860:4860::8888")] [InlineData("::ffff:8.8.8.8")]
    public void Public_addresses_are_allowed(string ip) => Assert.True(PublicAddressPolicy.IsPublic(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("127.0.0.1")] [InlineData("127.255.255.254")] [InlineData("0.0.0.0")] [InlineData("0.1.2.3")]
    [InlineData("10.0.0.1")] [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")] [InlineData("172.31.255.255")]
    [InlineData("192.168.0.1")] [InlineData("192.168.255.255")]
    [InlineData("169.254.169.254")]                                   // cloud metadata
    [InlineData("100.64.0.1")] [InlineData("100.127.255.255")]        // carrier-grade NAT
    [InlineData("192.0.0.1")] [InlineData("192.0.2.1")] [InlineData("198.51.100.1")] [InlineData("203.0.113.1")] [InlineData("198.18.0.1")] [InlineData("198.19.255.255")]
    [InlineData("224.0.0.1")] [InlineData("239.255.255.250")]         // multicast (Python says global; we refuse)
    [InlineData("240.0.0.1")] [InlineData("255.255.255.255")]
    public void Internal_and_reserved_ipv4_addresses_are_refused(string ip) => Assert.False(PublicAddressPolicy.IsPublic(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("::1")] [InlineData("::")]
    [InlineData("fe80::1")]                                           // link-local
    [InlineData("fc00::1")] [InlineData("fd12:3456:789a::1")]         // unique local
    [InlineData("ff02::1")]                                           // multicast
    [InlineData("2001:db8::1")]                                       // documentation
    [InlineData("2001::1")]                                           // Teredo
    [InlineData("2002:7f00:1::1")]                                    // 6to4 embedding 127.0.0.1
    [InlineData("64:ff9b::7f00:1")]                                   // NAT64 embedding 127.0.0.1 (Python says global; we refuse)
    [InlineData("::ffff:127.0.0.1")] [InlineData("::ffff:10.0.0.1")] [InlineData("::ffff:169.254.169.254")] // IPv4 hidden inside IPv6
    [InlineData("::127.0.0.1")]                                       // deprecated IPv4-compatible form
    [InlineData("3fff::1")]                                           // documentation
    public void Internal_and_reserved_ipv6_addresses_are_refused(string ip) => Assert.False(PublicAddressPolicy.IsPublic(IPAddress.Parse(ip)));

    [Fact]
    public void The_boundaries_of_the_172_block_are_exact()
    {
        Assert.True(PublicAddressPolicy.IsPublic(IPAddress.Parse("172.15.255.255")));
        Assert.False(PublicAddressPolicy.IsPublic(IPAddress.Parse("172.16.0.0")));
        Assert.False(PublicAddressPolicy.IsPublic(IPAddress.Parse("172.31.255.255")));
        Assert.True(PublicAddressPolicy.IsPublic(IPAddress.Parse("172.32.0.0")));
        Assert.True(PublicAddressPolicy.IsPublic(IPAddress.Parse("100.63.255.255")));
        Assert.False(PublicAddressPolicy.IsPublic(IPAddress.Parse("100.64.0.0")));
        Assert.True(PublicAddressPolicy.IsPublic(IPAddress.Parse("100.128.0.0")));
    }
}

public class WebhookValidationTests
{
    private static readonly string Secret = "0123456789abcdef";
    private readonly WebhookChannel _channel = new(new NoHttp(), new FakeClock());
    private sealed class NoHttp : IHttpClientFactory { public HttpClient CreateClient(string name) => throw new InvalidOperationException("no network in this test"); }

    private IReadOnlyList<string> Validate(string url, string? secret = null) =>
        _channel.ValidateSettings(JsonSerializer.Serialize(new { url, secret = secret ?? Secret }));

    [Theory]
    [InlineData("https://example.com/hook")]
    [InlineData("https://hooks.example.co.uk:8443/a/b?token=1")]
    [InlineData("https://8.8.8.8/x")]
    [InlineData("https://[2606:4700:4700::1111]/x")]
    [InlineData("https://xn--bcher-kva.example/hook")]
    public void Ordinary_public_https_addresses_are_accepted(string url) => Assert.Empty(Validate(url));

    [Theory]
    [InlineData("http://example.com/hook")]                       // not https
    [InlineData("ftp://example.com/hook")]
    [InlineData("example.com/hook")]
    [InlineData("")]
    [InlineData("https://localhost/x")]
    [InlineData("https://LOCALHOST./x")]
    [InlineData("https://app.localhost/x")]
    [InlineData("https://printer.local/")]
    [InlineData("https://intranet/")]                             // single-label internal name
    [InlineData("https://db.internal/")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://[::1]/")]
    [InlineData("https://2130706433/")]                           // 127.0.0.1 written as one decimal number
    [InlineData("https://0x7f.0.0.1/")]                           // hexadecimal
    [InlineData("https://127.1/")]                                // short form
    [InlineData("https://0177.0.0.1/")]                           // octal
    [InlineData("https://[::ffff:127.0.0.1]/")]                   // IPv4 inside IPv6
    [InlineData("https://169.254.169.254/latest/meta-data/")]     // cloud metadata
    [InlineData("https://10.0.0.5/")]
    [InlineData("https://192.168.1.1/")]
    [InlineData("https://[fd00::1]/")]
    [InlineData("https://user:pw@example.com/")]                  // credentials
    [InlineData("https://example.com@127.0.0.1/")]                // "looks like example.com" trick
    [InlineData("https://exa mple.com/")]
    public void Dangerous_or_malformed_addresses_are_refused(string url) => Assert.NotEmpty(Validate(url));

    [Fact]
    public void A_very_long_address_is_refused() => Assert.NotEmpty(Validate("https://example.com/" + new string('a', 2100)));

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("short")] [InlineData("123456789012345")]
    public void The_signing_secret_must_be_long_enough(string? secret)
        => Assert.NotEmpty(_channel.ValidateSettings(JsonSerializer.Serialize(new { url = "https://example.com/h", secret })));

    [Fact] public void Secret_with_control_characters_is_refused() => Assert.NotEmpty(Validate("https://example.com/h", "0123456789abcdef\r\nX-Evil: 1"));
    [Fact] public void Malformed_settings_are_reported_not_thrown() => Assert.NotEmpty(_channel.ValidateSettings("{oops"));

    [Fact]
    public void Masked_settings_show_only_the_host()
    {
        var masked = _channel.MaskSettings(JsonSerializer.Serialize(new { url = "https://hooks.example.com/secret/path/TOKEN123?key=abc", secret = Secret }));
        Assert.Equal("https://hooks.example.com/… (signed)", masked);
        Assert.DoesNotContain("TOKEN123", masked);
        Assert.DoesNotContain(Secret, masked);
    }
}

public class WebhookSendTests
{
    private const string Secret = "0123456789abcdef";
    private const string Url = "https://hooks.example.com/in/abc";
    private static string Settings(string url = Url, string secret = Secret) => JsonSerializer.Serialize(new { url, secret });

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls.Add((r, r.Content is null ? "" : await r.Content.ReadAsStringAsync(ct)));
            return respond(r);
        }
    }
    private sealed class Factory(HttpMessageHandler h) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(h); }

    private static (WebhookChannel Channel, StubHandler Handler, FakeClock Clock) Build(Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        var handler = new StubHandler(respond ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var clock = new FakeClock();
        return (new WebhookChannel(new Factory(handler), clock), handler, clock);
    }

    private static NotificationMessage Msg(string title = "M 6.8 - Sendai") => new(new Event
    {
        Id = Guid.Parse("11111111-2222-3333-4444-555555555555"), Title = title, Summary = "Strong quake", Url = "https://example.com/q",
        Location = "Sendai, Japan", Category = EventCategory.Disasters, Severity = 4, SourceKey = "usgs-earthquakes",
        OccurredAt = new DateTime(2026, 6, 1, 11, 0, 0, DateTimeKind.Utc), IsSimulated = true,
    }, "Big quakes");

    // ---- signing ----

    [Fact]
    public void Hmac_matches_the_published_RFC_4231_test_vector()
        => Assert.Equal("5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843", WebhookChannel.HmacHex("Jefe", "what do ya want for nothing?"));

    [Fact]
    public void Signature_format_matches_an_independent_python_calculation()
        => Assert.Equal("d94e10f517f91a70b70a5e4c55b5e80b50b1348c38bd3eb5599e651bb5228ca8", WebhookChannel.Sign("0123456789abcdef", "1795087917", "{\"a\":1}"));

    [Fact]
    public async Task A_receiver_following_the_documented_scheme_can_verify_the_message()
    {
        var (channel, handler, clock) = Build();
        await channel.SendAsync(Msg(), Settings(), default);

        var (request, body) = Assert.Single(handler.Calls);
        var timestamp = request.Headers.GetValues("X-EventAlert-Timestamp").Single();
        var signature = request.Headers.GetValues("X-EventAlert-Signature").Single();

        // What a customer's server would do, written here without any of our code:
        var expected = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"))).ToLowerInvariant();
        Assert.Equal(expected, signature);
        Assert.Equal(new DateTimeOffset(clock.UtcNow).ToUnixTimeSeconds().ToString(), timestamp);
    }

    [Fact]
    public async Task A_different_secret_gives_a_different_signature_and_a_tampered_body_does_not_verify()
    {
        var (c1, h1, _) = Build(); var (c2, h2, _) = Build();
        await c1.SendAsync(Msg(), Settings(), default);
        await c2.SendAsync(Msg(), Settings(secret: "another-secret-value"), default);
        Assert.NotEqual(h1.Calls[0].Request.Headers.GetValues("X-EventAlert-Signature").Single(), h2.Calls[0].Request.Headers.GetValues("X-EventAlert-Signature").Single());

        var ts = h1.Calls[0].Request.Headers.GetValues("X-EventAlert-Timestamp").Single();
        Assert.NotEqual(h1.Calls[0].Request.Headers.GetValues("X-EventAlert-Signature").Single(), "sha256=" + WebhookChannel.Sign(Secret, ts, h1.Calls[0].Body + " "));
    }

    // ---- what is sent ----

    [Fact]
    public async Task The_request_is_a_json_post_with_the_event_facts()
    {
        var (channel, handler, _) = Build();
        var result = await channel.SendAsync(Msg(), Settings(), default);

        Assert.Equal(ChannelSendOutcome.Success, result.Outcome);
        var (request, body) = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(Url, request.RequestUri!.ToString());
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("11111111-2222-3333-4444-555555555555", request.Headers.GetValues("X-EventAlert-Event").Single());
        Assert.StartsWith("EventAlertManagement/", request.Headers.UserAgent.ToString());

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal("M 6.8 - Sendai", root.GetProperty("title").GetString());
        Assert.Equal("Disasters", root.GetProperty("category").GetString());
        Assert.Equal(4, root.GetProperty("severity").GetInt32());
        Assert.Equal("Big quakes", root.GetProperty("rule").GetString());
        Assert.Equal("usgs-earthquakes", root.GetProperty("source").GetString());
        Assert.True(root.GetProperty("isSimulated").GetBoolean());
        Assert.Equal("2026-06-01T11:00:00.0000000Z", root.GetProperty("occurredAt").GetString());
    }

    [Fact]
    public async Task Hostile_text_travels_as_data_and_cannot_break_the_json()
    {
        var (channel, handler, _) = Build();
        await channel.SendAsync(Msg("\"}, \"severity\": 1, \"x\": {\"y\":\"<script>alert(1)</script>\r\n"), Settings(), default);

        using var json = JsonDocument.Parse(handler.Calls[0].Body);                 // still valid
        Assert.Equal(4, json.RootElement.GetProperty("severity").GetInt32());      // the injected "severity" had no effect
        Assert.Contains("<script>", json.RootElement.GetProperty("title").GetString());
        Assert.DoesNotContain("<script>", handler.Calls[0].Body);                  // escaped on the wire
    }

    // ---- failure handling ----

    [Theory]
    [InlineData(HttpStatusCode.Created, ChannelSendOutcome.Success)]
    [InlineData(HttpStatusCode.NoContent, ChannelSendOutcome.Success)]
    [InlineData(HttpStatusCode.TooManyRequests, ChannelSendOutcome.TransientFailure)]
    [InlineData(HttpStatusCode.RequestTimeout, ChannelSendOutcome.TransientFailure)]
    [InlineData(HttpStatusCode.InternalServerError, ChannelSendOutcome.TransientFailure)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ChannelSendOutcome.TransientFailure)]
    [InlineData(HttpStatusCode.BadRequest, ChannelSendOutcome.PermanentFailure)]
    [InlineData(HttpStatusCode.Unauthorized, ChannelSendOutcome.PermanentFailure)]
    [InlineData(HttpStatusCode.NotFound, ChannelSendOutcome.PermanentFailure)]
    [InlineData(HttpStatusCode.Gone, ChannelSendOutcome.PermanentFailure)]
    [InlineData(HttpStatusCode.MovedPermanently, ChannelSendOutcome.PermanentFailure)]  // redirects are never followed
    [InlineData(HttpStatusCode.Found, ChannelSendOutcome.PermanentFailure)]
    public async Task Http_status_is_classified(HttpStatusCode status, ChannelSendOutcome expected)
    {
        var (channel, _, _) = Build(_ => new HttpResponseMessage(status));
        Assert.Equal(expected, (await channel.SendAsync(Msg(), Settings(), default)).Outcome);
    }

    [Fact]
    public async Task A_blocked_address_found_at_connect_time_is_permanent_and_explained_without_the_address()
    {
        var (channel, _, _) = Build(_ => throw new HttpRequestException("connect failed to https://hooks.example.com/in/abc", new BlockedAddressException("internal")));
        var r = await channel.SendAsync(Msg(), Settings(), default);

        Assert.Equal(ChannelSendOutcome.PermanentFailure, r.Outcome);
        Assert.Contains("private or internal", r.Error);
        Assert.DoesNotContain("hooks.example.com", r.Error);
    }

    [Fact]
    public async Task A_bad_certificate_is_permanent()
    {
        var (channel, _, _) = Build(_ => throw new HttpRequestException("ssl", new System.Security.Authentication.AuthenticationException("untrusted")));
        Assert.Equal(ChannelSendOutcome.PermanentFailure, (await channel.SendAsync(Msg(), Settings(), default)).Outcome);
    }

    [Fact]
    public async Task Network_errors_and_timeouts_are_transient_and_leak_no_address()
    {
        var (c1, _, _) = Build(_ => throw new HttpRequestException("Name resolution failed for hooks.example.com"));
        var r1 = await c1.SendAsync(Msg(), Settings(), default);
        var (c2, _, _) = Build(_ => throw new TaskCanceledException("timeout"));
        var r2 = await c2.SendAsync(Msg(), Settings(), default);

        Assert.Equal(ChannelSendOutcome.TransientFailure, r1.Outcome);
        Assert.Equal(ChannelSendOutcome.TransientFailure, r2.Outcome);
        Assert.DoesNotContain("hooks.example.com", r1.Error);
    }

    [Fact]
    public async Task Invalid_settings_are_never_sent_anywhere()
    {
        var (channel, handler, _) = Build();
        var r = await channel.SendAsync(Msg(), Settings("https://127.0.0.1/x"), default);
        Assert.Equal(ChannelSendOutcome.PermanentFailure, r.Outcome);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_a_delivery_failure()
    {
        using var cts = new CancellationTokenSource();
        var (channel, _, _) = Build(_ => { cts.Cancel(); throw new TaskCanceledException(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => channel.SendAsync(Msg(), Settings(), cts.Token));
    }
}

/// <summary>The decisive check: real sockets, real connection attempts, no mocks.</summary>
public class SsrfGuardSocketTests
{
    private static HttpClient ClientWith(Func<string, CancellationToken, Task<IPAddress[]>>? resolver = null, Func<IPAddress, bool>? allow = null)
        => new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, ConnectCallback = SsrfGuard.CreateConnectCallback(resolver, allow) })
        { Timeout = TimeSpan.FromSeconds(5) };

    private static (TcpListener Listener, int Port) Listen()
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        return (l, ((IPEndPoint)l.LocalEndpoint).Port);
    }

    [Fact]
    public async Task A_loopback_address_is_refused_and_nothing_connects()
    {
        var (listener, port) = Listen();
        try
        {
            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => ClientWith().GetAsync($"http://127.0.0.1:{port}/"));
            Assert.IsType<BlockedAddressException>(ex.InnerException);
            Assert.False(listener.Pending(), "a connection reached the internal server");
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task A_host_name_that_resolves_to_an_internal_address_is_refused_at_connect_time()
    {
        // This is the DNS rebinding case: the name looked fine when the settings were saved, but answers with an internal address now.
        var (listener, port) = Listen();
        try
        {
            var client = ClientWith(resolver: (_, _) => Task.FromResult(new[] { IPAddress.Loopback }));
            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync($"http://rebind.example.com:{port}/"));
            Assert.IsType<BlockedAddressException>(ex.InnerException);
            Assert.False(listener.Pending());
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task One_internal_address_among_public_ones_is_enough_to_refuse()
    {
        var (listener, port) = Listen();
        try
        {
            var client = ClientWith(resolver: (_, _) => Task.FromResult(new[] { IPAddress.Parse("8.8.8.8"), IPAddress.Loopback }));
            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync($"http://mixed.example.com:{port}/"));
            Assert.IsType<BlockedAddressException>(ex.InnerException);
            Assert.False(listener.Pending());
        }
        finally { listener.Stop(); }
    }

    [Theory]
    [InlineData("::1")] [InlineData("::ffff:127.0.0.1")] [InlineData("fe80::1")]
    public async Task Internal_ipv6_addresses_are_refused_too(string ip)
    {
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => ClientWith().GetAsync($"http://[{ip}]:9/"));
        Assert.IsType<BlockedAddressException>(ex.InnerException);
    }

    [Fact]
    public async Task A_name_with_no_address_is_refused()
    {
        var client = ClientWith(resolver: (_, _) => Task.FromResult(Array.Empty<IPAddress>()));
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("http://nowhere.example.com/"));
        Assert.IsType<BlockedAddressException>(ex.InnerException);
    }

    [Fact]
    public async Task An_allowed_address_is_really_connected_to_and_the_original_host_name_is_kept()
    {
        // With the policy relaxed for this test only, prove the callback connects to the address it checked
        // and that the request still names the original host.
        var (listener, port) = Listen();
        var captured = new TaskCompletionSource<string>();
        _ = Task.Run(async () =>
        {
            using var tcp = await listener.AcceptTcpClientAsync();
            var stream = tcp.GetStream();
            var buffer = new byte[4096]; var text = new StringBuilder();
            while (!text.ToString().Contains("\r\n\r\n")) { var n = await stream.ReadAsync(buffer); if (n == 0) break; text.Append(Encoding.ASCII.GetString(buffer, 0, n)); }
            captured.SetResult(text.ToString());
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"));
        });
        try
        {
            var client = ClientWith(resolver: (_, _) => Task.FromResult(new[] { IPAddress.Loopback }), allow: _ => true);
            var response = await client.GetAsync($"http://ok.example.com:{port}/path");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains($"Host: ok.example.com:{port}", await captured.Task);
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public void RequireAllowed_refuses_mixed_and_empty_lists()
    {
        Assert.Throws<BlockedAddressException>(() => SsrfGuard.RequireAllowed(Array.Empty<IPAddress>()));
        Assert.Throws<BlockedAddressException>(() => SsrfGuard.RequireAllowed(new[] { IPAddress.Parse("8.8.8.8"), IPAddress.Parse("10.0.0.1") }));
        Assert.Single(SsrfGuard.RequireAllowed(new[] { IPAddress.Parse("8.8.8.8") }));
    }
}

public class WebhookHandlerConfigurationTests
{
    [Fact]
    public void By_default_redirects_and_proxies_are_off_and_the_address_guard_is_attached()
    {
        using var handler = WebhookChannelRegistration.CreateHandler(new WebhookOptions());
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseProxy);
        Assert.NotNull(handler.ConnectCallback);
    }

    [Fact]
    public void Using_a_proxy_is_an_explicit_choice_and_still_never_follows_redirects()
    {
        using var handler = WebhookChannelRegistration.CreateHandler(new WebhookOptions { UseSystemProxy = true });
        Assert.False(handler.AllowAutoRedirect);
        Assert.True(handler.UseProxy);
        Assert.Null(handler.ConnectCallback); // the proxy makes the connection, so the guard cannot see the real destination
    }

    [Fact]
    public void Connections_are_not_kept_forever_so_a_changed_address_is_checked_again()
    {
        using var handler = WebhookChannelRegistration.CreateHandler(new WebhookOptions());
        Assert.True(handler.PooledConnectionLifetime <= TimeSpan.FromMinutes(5));
        Assert.True(handler.ConnectTimeout <= TimeSpan.FromSeconds(10));
    }
}
