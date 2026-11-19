using System.Text.Json;
using EventAlertManagement.Core.Contracts;
using EventAlertManagement.Core.Domain;
using EventAlertManagement.Infrastructure.Channels;
using Microsoft.Extensions.Options;
using MimeKit;

namespace EventAlertManagement.Tests.Channels;

public class EmailChannelTests
{
    private sealed class CapturingTransport(ChannelSendResult? result = null) : IEmailTransport
    {
        public List<MimeMessage> Messages { get; } = new();
        public Task<ChannelSendResult> SendAsync(MimeMessage message, CancellationToken ct)
        {
            Messages.Add(message);
            return Task.FromResult(result ?? ChannelSendResult.Ok());
        }
    }

    private static readonly SmtpOptions Smtp = new() { FromAddress = "alerts@eventalert.local" };
    private static string Settings(string address) => JsonSerializer.Serialize(new { address });
    private static EmailChannel Build(IEmailTransport t) => new(t, Options.Create(Smtp));

    private static NotificationMessage Msg(string title = "Big quake", string summary = "Details", string? url = "https://example.com/e", string? location = "Tokyo", string rule = "Quakes", bool simulated = false) =>
        new(new Event { Title = title, Summary = summary, Url = url, Location = location, OccurredAt = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc), IsSimulated = simulated }, rule);

    [Theory]
    [InlineData("person@example.com", true)]
    [InlineData("first.last+tag@sub.example.co.uk", true)]
    [InlineData("", false)]
    [InlineData("not-an-email", false)]
    [InlineData("a@b.com\r\nBcc: victim@example.com", false)]  // header injection
    [InlineData("a@b.com,c@d.com", false)]                      // several recipients
    [InlineData("Name <a@b.com>", false)]
    public void Address_validation(string address, bool valid)
    {
        var channel = Build(new CapturingTransport());
        Assert.Equal(valid, channel.ValidateSettings(Settings(address)).Count == 0);
    }

    [Fact]
    public void Masked_address_hides_most_of_the_local_part()
    {
        var masked = Build(new CapturingTransport()).MaskSettings(Settings("person@example.com"));
        Assert.Equal("p***@example.com", masked);
    }

    [Fact]
    public void Html_body_escapes_untrusted_event_text()
    {
        var mime = EmailChannel.Render(Msg(title: "<script>alert(1)</script>", summary: "<img src=x onerror=alert(2)>", location: "<b>Tokyo</b>", rule: "<i>rule</i>"),
            "a@b.com", Smtp);

        var html = mime.HtmlBody!;
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("<b>Tokyo</b>", html);
        Assert.DoesNotContain("<i>rule</i>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Subject_cannot_contain_line_breaks()
    {
        var mime = EmailChannel.Render(Msg(title: "Hello\r\nBcc: victim@example.com"), "a@b.com", Smtp);
        Assert.DoesNotContain('\n', mime.Subject);
        Assert.DoesNotContain('\r', mime.Subject);
        Assert.Empty(mime.Bcc);
    }

    [Fact]
    public void Only_http_links_are_rendered()
    {
        Assert.Contains("href=\"https://example.com/e\"", EmailChannel.Render(Msg(), "a@b.com", Smtp).HtmlBody);
        var bad = EmailChannel.Render(Msg(url: "javascript:alert(1)"), "a@b.com", Smtp);
        Assert.DoesNotContain("javascript", bad.HtmlBody);
        Assert.DoesNotContain("javascript", bad.TextBody);
    }

    [Fact]
    public void Quote_in_url_cannot_break_out_of_the_href_attribute()
    {
        var html = EmailChannel.Render(Msg(url: "https://example.com/\" onclick=\"alert(1)"), "a@b.com", Smtp).HtmlBody!;
        Assert.DoesNotContain("onclick=\"alert", html);
    }

    [Fact]
    public void Text_body_has_the_facts_a_reader_needs_and_marks_simulated_events()
    {
        var text = EmailChannel.Render(Msg(simulated: true), "a@b.com", Smtp).TextBody!;
        Assert.Contains("Big quake", text);
        Assert.Contains("Tokyo", text);
        Assert.Contains("https://example.com/e", text);
        Assert.Contains("Quakes", text);
        Assert.Contains("Simulated", text);
    }

    [Fact]
    public async Task Send_hands_one_message_to_the_transport_addressed_to_the_user()
    {
        var transport = new CapturingTransport();
        var result = await Build(transport).SendAsync(Msg(), Settings("person@example.com"), default);

        Assert.Equal(ChannelSendOutcome.Success, result.Outcome);
        var mime = Assert.Single(transport.Messages);
        Assert.Equal("person@example.com", Assert.Single(mime.To.Mailboxes).Address);
        Assert.Equal("alerts@eventalert.local", Assert.Single(mime.From.Mailboxes).Address);
        Assert.StartsWith("[Alert]", mime.Subject);
    }

    [Fact]
    public async Task Transport_failure_result_is_passed_through()
    {
        var transport = new CapturingTransport(ChannelSendResult.Transient("down"));
        var result = await Build(transport).SendAsync(Msg(), Settings("person@example.com"), default);
        Assert.Equal(ChannelSendOutcome.TransientFailure, result.Outcome);
    }

    [Fact]
    public async Task Invalid_address_is_permanent_and_nothing_is_sent()
    {
        var transport = new CapturingTransport();
        var result = await Build(transport).SendAsync(Msg(), Settings("bad\r\naddress"), default);
        Assert.Equal(ChannelSendOutcome.PermanentFailure, result.Outcome);
        Assert.Empty(transport.Messages);
    }
}

public class ChannelRegistryTests
{
    [Fact]
    public void Lookup_is_case_insensitive_and_unknown_type_returns_null()
    {
        var registry = new ChannelRegistry(new INotificationChannel[] { new Integration.FakeChannel("Email") });
        Assert.NotNull(registry.Find("email"));
        Assert.Null(registry.Find("Pigeon"));
    }

    [Fact]
    public void Registering_the_same_type_twice_is_an_error()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ChannelRegistry(new INotificationChannel[] { new Integration.FakeChannel("Email"), new Integration.FakeChannel("email") }));
    }
}
