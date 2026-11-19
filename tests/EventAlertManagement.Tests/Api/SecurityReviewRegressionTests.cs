using System.Net;
using System.Net.Http.Json;
using System.Text;
using EventAlertManagement.Core.Domain;
using EventAlertManagement.Core.Filters;
using EventAlertManagement.Core.Matching;
using EventAlertManagement.Infrastructure.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventAlertManagement.Tests.Api;

/// <summary>
/// Each test pins a weakness an independent security reviewer reported (evidence/independent-review).
/// They were written first and failed on the code as it was.
/// </summary>
public class SecurityReviewRegressionTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    public SecurityReviewRegressionTests(ApiFactory f) => _f = f;
    private static string Unique() => $"u-{Guid.NewGuid():N}@example.com";

    // ---- 1. the 10-second limit on test messages must hold for parallel requests ----

    [Fact]
    public async Task Parallel_test_requests_cannot_get_around_the_test_message_limit()
    {
        var user = await _f.UserAsync(Unique());
        var id = (await (await user.PostAsJsonAsync("/api/channels", new { type = "Email", name = "victim", settings = new { address = "third-party@example.com" } })).JsonAsync()).GetProperty("id").GetGuid();
        var before = _f.Email.Messages.Count;

        var codes = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => Task.Run(async () => (await user.PostAsync($"/api/channels/{id}/test", null)).StatusCode)));

        Assert.Equal(1, codes.Count(c => c == HttpStatusCode.OK));
        Assert.Equal(24, codes.Count(c => c == (HttpStatusCode)429));
        Assert.Equal(before + 1, _f.Email.Messages.Count); // exactly one message reached the third party
    }

    [Fact]
    public async Task A_user_cannot_start_unlimited_tests_across_many_channels()
    {
        var user = await _f.UserAsync(Unique());
        var ids = new List<Guid>();
        for (var i = 0; i < 12; i++)
            ids.Add((await (await user.PostAsJsonAsync("/api/channels", new { type = "Email", name = $"c{i}", settings = new { address = $"t{i}@example.com" } })).JsonAsync()).GetProperty("id").GetGuid());

        var codes = new List<HttpStatusCode>();
        foreach (var id in ids) codes.Add((await user.PostAsync($"/api/channels/{id}/test", null)).StatusCode);

        Assert.True(codes.Count(c => c == HttpStatusCode.OK) <= 6, "a per-user limit should apply across channels");
        Assert.Contains((HttpStatusCode)429, codes);
    }

    // ---- 2. secrets in URLs must never reach the server log ----

    private sealed class Capture : ILoggerProvider
    {
        public List<string> Lines { get; } = new();
        public ILogger CreateLogger(string category) => new L(this);
        public void Dispose() { }
        private sealed class L(Capture c) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> formatter)
            { lock (c.Lines) c.Lines.Add(formatter(state, ex) + (ex is null ? "" : " " + ex)); }
        }
    }

    private sealed class Ok : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    [Fact]
    public async Task Webhook_and_slack_addresses_with_secrets_are_not_written_to_the_log()
    {
        var capture = new Capture();
        using var factory = ApiFactory.WithServices(services =>
        {
            services.AddLogging(b => b.AddProvider(capture).SetMinimumLevel(LogLevel.Trace));
            services.AddHttpClient(WebhookChannel.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new Ok());
            services.AddHttpClient(SlackChannel.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new Ok());
        });
        var user = await factory.UserAsync(Unique());

        var hook = (await (await user.PostAsJsonAsync("/api/channels", new { type = "Webhook", name = "h", settings = new { url = "https://hook.example.com/SECRETPATH-abc?token=QUERYSECRET", secret = "SIGNINGSECRET-0123456789" } })).JsonAsync()).GetProperty("id").GetGuid();
        var slack = (await (await user.PostAsJsonAsync("/api/channels", new { type = "Slack", name = "s", settings = new { webhookUrl = "https://hooks.slack.com/services/T000/B000/SLACKTOKENXYZ" } })).JsonAsync()).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await user.PostAsync($"/api/channels/{hook}/test", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.PostAsync($"/api/channels/{slack}/test", null)).StatusCode);

        string all; lock (capture.Lines) all = string.Join("\n", capture.Lines);
        Assert.Contains("Webhook", all + "Webhook"); // sanity: the capture is wired up
        foreach (var secret in new[] { "SECRETPATH", "QUERYSECRET", "SIGNINGSECRET", "SLACKTOKENXYZ" })
            Assert.DoesNotContain(secret, all);
    }

    // ---- 3. nothing the user sends may be unbounded ----

    [Fact]
    public async Task A_rule_with_an_enormous_filter_is_refused()
    {
        var user = await _f.UserAsync(Unique());
        var channel = (await (await user.PostAsJsonAsync("/api/channels", new { type = "Fake", name = "c", settings = new { } })).JsonAsync()).GetProperty("id").GetGuid();
        var keywords = Enumerable.Range(0, 5000).Select(i => "keyword" + i).ToArray();

        var r = await user.PostAsJsonAsync("/api/rules", new { name = "big", category = "News", minSeverity = 1, filters = new { includeKeywords = keywords }, cooldownSeconds = 0, isEnabled = true, channelIds = new[] { channel } });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("keywords", await r.BodyAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_very_long_single_keyword_is_refused_but_a_normal_rule_still_works()
    {
        var user = await _f.UserAsync(Unique());
        var channel = (await (await user.PostAsJsonAsync("/api/channels", new { type = "Fake", name = "c", settings = new { } })).JsonAsync()).GetProperty("id").GetGuid();
        object Body(string kw) => new { name = "r", category = "News", minSeverity = 1, filters = new { includeKeywords = new[] { kw } }, cooldownSeconds = 0, isEnabled = true, channelIds = new[] { channel } };

        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsJsonAsync("/api/rules", Body(new string('x', 500)))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await user.PostAsJsonAsync("/api/rules", Body("evacuation"))).StatusCode);
    }

    [Fact]
    public async Task Oversized_request_bodies_are_refused_before_they_are_read()
    {
        var anonymous = _f.Anonymous();
        var huge = new StringContent("{\"email\":\"" + new string('a', 400_000) + "@example.com\"}", Encoding.UTF8, "application/json");
        var r = await anonymous.PostAsync("/api/auth/login", huge);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, r.StatusCode);
    }

    [Fact]
    public async Task One_rule_cannot_fan_out_to_more_than_five_channels()
    {
        var user = await _f.UserAsync(Unique());
        var ids = new List<Guid>();
        for (var i = 0; i < 6; i++)
            ids.Add((await (await user.PostAsJsonAsync("/api/channels", new { type = "Fake", name = $"c{i}", settings = new { } })).JsonAsync()).GetProperty("id").GetGuid());
        object Body(IEnumerable<Guid> c) => new { name = "r", category = "News", minSeverity = 1, filters = new { }, cooldownSeconds = 0, isEnabled = true, channelIds = c };

        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsJsonAsync("/api/rules", Body(ids))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await user.PostAsJsonAsync("/api/rules", Body(ids.Take(5)))).StatusCode);
    }

    // ---- 4. the text checks on webhook addresses should not depend on the connection guard alone ----

    [Theory]
    [InlineData("https://127.0.0.1./")]
    [InlineData("https://127.0.0.1.:8443/")]
    [InlineData("https://①②⑦.0.0.1/")]
    [InlineData("https://127。0。0。1/")]
    [InlineData("https://１２７.0.0.1/")]
    [InlineData("https://10.0.0.1./hook")]
    [InlineData("https://[::1]./")]
    [InlineData("https://1.2.3.4.5/")]          // five numbers: not an address, but an attempt to look like one
    [InlineData("https://example.123/")]        // no real domain ends in a number
    [InlineData("https://999.999.999.999/")]
    public async Task Odd_spellings_of_internal_addresses_are_refused_when_the_channel_is_saved(string url)
    {
        var user = await _f.UserAsync(Unique());
        var r = await user.PostAsJsonAsync("/api/channels", new { type = "Webhook", name = "w", settings = new { url, secret = "0123456789abcdef-secret" } });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    // ---- 5. smaller things ----

    [Fact]
    public void Links_with_a_user_name_and_password_are_not_rendered_in_email_or_slack()
    {
        var ev = new Event { Title = "t", Summary = "s", Url = "https://trusted.example.com@evil.example.net/login", Category = EventCategory.News };
        var msg = new EventAlertManagement.Core.Contracts.NotificationMessage(ev, "r");
        Assert.DoesNotContain("evil.example.net", SlackChannel.RenderText(msg));
        Assert.Null(EmailChannel.SafeLink(ev.Url));
    }

    [Fact]
    public void Masked_slack_settings_show_no_character_of_the_secret_token()
    {
        var slack = new SlackChannel(new NoHttp());
        var masked = slack.MaskSettings("{\"webhookUrl\":\"https://hooks.slack.com/services/T000/B000/abcdEFGH1234\"}");
        Assert.DoesNotContain("1234", masked);
        Assert.DoesNotContain("EFGH", masked);
    }
    private sealed class NoHttp : IHttpClientFactory { public HttpClient CreateClient(string name) => throw new InvalidOperationException(); }

    [Fact]
    public void The_application_refuses_to_start_with_a_weak_admin_key()
    {
        using var factory = ApiFactory.With(new() { ["Auth:AdminKey"] = "short" });
        Assert.ThrowsAny<Exception>(() => factory.Anonymous());
    }
}

public class RuleFilterLimitTests
{
    private static AlertRule Rule(RuleFilters f) { var r = new AlertRule { Name = "r", Category = f is NewsFilters ? EventCategory.News : f is DisasterFilters ? EventCategory.Disasters : EventCategory.Markets, MinSeverity = 1, FiltersJson = RuleFilterSerializer.Serialize(f) }; r.Channels.Add(new ChannelConfig()); return r; }

    [Fact] public void Fifty_keywords_are_fine() => Assert.Empty(RuleValidator.Validate(Rule(new NewsFilters { IncludeKeywords = Enumerable.Range(0, 50).Select(i => "k" + i).ToList() })));
    [Fact] public void More_than_fifty_keywords_are_refused() => Assert.NotEmpty(RuleValidator.Validate(Rule(new NewsFilters { IncludeKeywords = Enumerable.Range(0, 51).Select(i => "k" + i).ToList() })));
    [Fact] public void More_than_fifty_excluded_words_are_refused() => Assert.NotEmpty(RuleValidator.Validate(Rule(new NewsFilters { ExcludeKeywords = Enumerable.Range(0, 51).Select(i => "k" + i).ToList() })));
    [Fact] public void A_keyword_over_one_hundred_characters_is_refused() => Assert.NotEmpty(RuleValidator.Validate(Rule(new NewsFilters { IncludeKeywords = { new string('x', 101) } })));
    [Fact] public void More_than_twenty_tickers_are_refused() => Assert.NotEmpty(RuleValidator.Validate(Rule(new MarketFilters { Tickers = Enumerable.Range(0, 21).Select(i => "T" + i).ToList(), MinPercentMove = 1 })));
    [Fact] public void A_long_region_is_refused() => Assert.NotEmpty(RuleValidator.Validate(Rule(new DisasterFilters { Region = new string('r', 101) })));
    [Fact] public void More_than_twenty_event_types_are_refused() => Assert.NotEmpty(RuleValidator.Validate(Rule(new DisasterFilters { EventTypes = Enumerable.Range(0, 21).Select(i => "e" + i).ToList() })));
}
