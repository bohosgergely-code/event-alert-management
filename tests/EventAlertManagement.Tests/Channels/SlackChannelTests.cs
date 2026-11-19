using System.Net;
using System.Text.Json;
using EventAlertManagement.Core.Contracts;
using EventAlertManagement.Core.Domain;
using EventAlertManagement.Infrastructure.Channels;

namespace EventAlertManagement.Tests.Channels;

public class SlackChannelTests
{
    private const string GoodUrl = "https://hooks.slack.com/services/T0123/B0123/abcdEFGH1234";
    private static string Settings(string url) => JsonSerializer.Serialize(new { webhookUrl = url });

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            return respond(request);
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private static NotificationMessage Msg(string title = "Quake", string summary = "Summary", string? url = "https://example.com/e/1", string rule = "My rule") =>
        new(new Event { Title = title, Summary = summary, Url = url, Location = "Tokyo", IsSimulated = false }, rule);

    private static (SlackChannel Channel, StubHandler Handler) Build(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        return (new SlackChannel(new Factory(handler)), handler);
    }

    // ---- webhook URL validation (SSRF protection) ----

    [Theory]
    [InlineData("https://hooks.slack.com/services/T1/B2/xyz", true)]
    [InlineData("HTTPS://HOOKS.SLACK.COM/services/T1/B2/xyz", true)]
    [InlineData("http://hooks.slack.com/services/T1/B2/xyz", false)]              // not https
    [InlineData("https://evil.example.com/services/T1/B2/xyz", false)]           // other host
    [InlineData("https://hooks.slack.com.evil.example/services/T1/B2/xyz", false)] // look-alike host
    [InlineData("https://evil.example.com/hooks.slack.com/services/x", false)]
    [InlineData("https://user:pw@hooks.slack.com/services/T1/B2/xyz", false)]    // credentials in URL
    [InlineData("https://hooks.slack.com:8443/services/T1/B2/xyz", false)]       // non-default port
    [InlineData("https://hooks.slack.com/other/path", false)]
    [InlineData("https://127.0.0.1/services/T1/B2/xyz", false)]
    [InlineData("https://169.254.169.254/services/x", false)]                     // cloud metadata address
    [InlineData("hooks.slack.com/services/T1/B2/xyz", false)]                    // not absolute
    [InlineData("", false)]
    public void Webhook_url_is_restricted_to_slack(string url, bool allowed)
    {
        var (channel, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.OK));
        Assert.Equal(allowed, channel.ValidateSettings(Settings(url)).Count == 0);
    }

    [Fact]
    public void Malformed_settings_are_reported_not_thrown()
    {
        var (channel, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.OK));
        Assert.NotEmpty(channel.ValidateSettings("{oops"));
    }

    [Fact]
    public void Masked_settings_do_not_reveal_the_secret_part_of_the_url()
    {
        var (channel, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var masked = channel.MaskSettings(Settings(GoodUrl));
        Assert.DoesNotContain("T0123", masked);
        Assert.DoesNotContain("B0123", masked);
        Assert.DoesNotContain("abcdEFGH", masked);
    }

    // ---- rendering ----

    [Fact]
    public void Slack_control_characters_in_event_text_are_escaped()
    {
        // "<!channel>" would ping everyone in the channel if left as is.
        var text = SlackChannel.RenderText(Msg(title: "<!channel> & <@U123> win", summary: "<https://evil.example|click>"));
        Assert.DoesNotContain("<!channel>", text);
        Assert.DoesNotContain("<@U123>", text);
        Assert.Contains("&lt;!channel&gt; &amp; &lt;@U123&gt;", text);
        Assert.DoesNotContain("<https://evil.example|click>", text);
    }

    [Fact]
    public void Link_is_rendered_only_for_http_and_https()
    {
        Assert.Contains("<https://example.com/e/1|Open source>", SlackChannel.RenderText(Msg()));
        Assert.DoesNotContain("javascript", SlackChannel.RenderText(Msg(url: "javascript:alert(1)")));
        Assert.DoesNotContain("Open source", SlackChannel.RenderText(Msg(url: "ftp://example.com/x")));
    }

    [Fact]
    public void Link_characters_that_break_slack_syntax_are_encoded()
    {
        var text = SlackChannel.RenderText(Msg(url: "https://example.com/a|b>c"));
        var linkPart = text[text.IndexOf("<https", StringComparison.Ordinal)..];
        Assert.Equal(1, linkPart.Count(c => c == '|'));   // only the separator
        Assert.Equal(1, linkPart.Split('\n')[0].Count(c => c == '>')); // only the closing bracket
    }

    [Fact]
    public void Newlines_in_title_cannot_forge_extra_lines()
    {
        var text = SlackChannel.RenderText(Msg(title: "Line1\nFAKE: send money"));
        Assert.DoesNotContain("\nFAKE", text);
    }

    // ---- sending and failure classification ----

    [Fact]
    public async Task Successful_post_goes_to_the_webhook_with_a_json_text_body()
    {
        var (channel, handler) = Build(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });

        var result = await channel.SendAsync(Msg(), Settings(GoodUrl), default);

        Assert.Equal(ChannelSendOutcome.Success, result.Outcome);
        var call = Assert.Single(handler.Calls);
        Assert.Equal(GoodUrl, call.Request.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, call.Request.Method);
        using var json = JsonDocument.Parse(call.Body);
        Assert.Contains("Quake", json.RootElement.GetProperty("text").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, ChannelSendOutcome.TransientFailure)]
    [InlineData(HttpStatusCode.InternalServerError, ChannelSendOutcome.TransientFailure)]
    [InlineData(HttpStatusCode.BadGateway, ChannelSendOutcome.TransientFailure)]
    [InlineData(HttpStatusCode.RequestTimeout, ChannelSendOutcome.TransientFailure)]
    [InlineData(HttpStatusCode.NotFound, ChannelSendOutcome.PermanentFailure)]   // webhook removed
    [InlineData(HttpStatusCode.Gone, ChannelSendOutcome.PermanentFailure)]       // channel archived
    [InlineData(HttpStatusCode.BadRequest, ChannelSendOutcome.PermanentFailure)] // invalid payload
    [InlineData(HttpStatusCode.Forbidden, ChannelSendOutcome.PermanentFailure)]
    [InlineData(HttpStatusCode.Found, ChannelSendOutcome.PermanentFailure)]      // redirects are never followed
    public async Task Http_status_is_classified_as_transient_or_permanent(HttpStatusCode status, ChannelSendOutcome expected)
    {
        var (channel, _) = Build(_ => new HttpResponseMessage(status));
        var result = await channel.SendAsync(Msg(), Settings(GoodUrl), default);
        Assert.Equal(expected, result.Outcome);
    }

    [Fact]
    public async Task Network_error_is_transient_and_the_error_text_contains_no_url()
    {
        var (channel, _) = Build(_ => throw new HttpRequestException($"Name resolution failed for {GoodUrl}"));
        var result = await channel.SendAsync(Msg(), Settings(GoodUrl), default);

        Assert.Equal(ChannelSendOutcome.TransientFailure, result.Outcome);
        Assert.DoesNotContain("abcdEFGH1234", result.Error);
        Assert.DoesNotContain("hooks.slack.com", result.Error);
    }

    [Fact]
    public async Task Timeout_is_transient()
    {
        var (channel, _) = Build(_ => throw new TaskCanceledException("timeout"));
        var result = await channel.SendAsync(Msg(), Settings(GoodUrl), default);
        Assert.Equal(ChannelSendOutcome.TransientFailure, result.Outcome);
    }

    [Fact]
    public async Task Disallowed_url_is_never_contacted()
    {
        var (channel, handler) = Build(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var result = await channel.SendAsync(Msg(), Settings("https://evil.example.com/services/a/b/c"), default);

        Assert.Equal(ChannelSendOutcome.PermanentFailure, result.Outcome);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_a_delivery_failure()
    {
        using var cts = new CancellationTokenSource();
        var (channel, _) = Build(_ => { cts.Cancel(); throw new TaskCanceledException(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => channel.SendAsync(Msg(), Settings(GoodUrl), cts.Token));
    }
}
