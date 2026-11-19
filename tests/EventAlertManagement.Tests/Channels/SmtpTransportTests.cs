using System.Text.Json;
using EventAlertManagement.Core.Contracts;
using EventAlertManagement.Core.Domain;
using EventAlertManagement.Infrastructure.Channels;
using Microsoft.Extensions.Options;

namespace EventAlertManagement.Tests.Channels;

/// <summary>Runs only when EAM_SMTP_TEST_PORT points at a real SMTP server (see tools/smtp-sink.py or Mailpit).</summary>
public sealed class SmtpFactAttribute : FactAttribute
{
    public SmtpFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("EAM_SMTP_TEST_PORT") is null)
            Skip = "Set EAM_SMTP_TEST_PORT to run tests against a real SMTP server.";
    }
}

public class SmtpTransportTests
{
    private static int Port => int.Parse(Environment.GetEnvironmentVariable("EAM_SMTP_TEST_PORT") ?? "1025");

    private static EmailChannel Channel(int port)
    {
        var options = Options.Create(new SmtpOptions { Host = "127.0.0.1", Port = port, FromAddress = "alerts@eventalert.local" });
        return new EmailChannel(new MailKitEmailTransport(options), options);
    }

    private static string To(string address) => JsonSerializer.Serialize(new { address });

    private static NotificationMessage Msg() => new(new Event
    {
        Title = "M 6.1 - 20 km N of Testville <b>bold</b>",
        Summary = "A test alert & more.",
        Url = "https://example.com/event/1",
        Location = "Testville",
        OccurredAt = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc),
        IsSimulated = true,
    }, "Quakes over 6");

    [SmtpFact]
    public async Task Real_message_is_accepted_by_a_real_smtp_server()
    {
        var result = await Channel(Port).SendAsync(Msg(), To("person@example.com"), default);
        Assert.Equal(ChannelSendOutcome.Success, result.Outcome);
    }

    [SmtpFact]
    public async Task Smtp_4xx_reply_is_transient()
    {
        var result = await Channel(Port).SendAsync(Msg(), To("later@example.com"), default);
        Assert.Equal(ChannelSendOutcome.TransientFailure, result.Outcome);
    }

    [SmtpFact]
    public async Task Smtp_5xx_reply_is_permanent()
    {
        var result = await Channel(Port).SendAsync(Msg(), To("reject@example.com"), default);
        Assert.Equal(ChannelSendOutcome.PermanentFailure, result.Outcome);
    }

    [SmtpFact]
    public async Task Unreachable_server_is_transient_and_the_error_does_not_leak_details()
    {
        var result = await Channel(1).SendAsync(Msg(), To("person@example.com"), default); // nothing listens on port 1
        Assert.Equal(ChannelSendOutcome.TransientFailure, result.Outcome);
        Assert.DoesNotContain("127.0.0.1", result.Error);
    }
}
