using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using EventAlertManagement.Core.Contracts;
using EventAlertManagement.Core.Domain;
using Microsoft.Extensions.Options;
using MimeKit;

namespace EventAlertManagement.Infrastructure.Channels;

public sealed class SmtpOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public bool UseTls { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "alerts@eventalert.local";
    public string FromName { get; set; } = "EventAlertManagement";
}

/// <summary>Sends a prepared message over SMTP. Behind an interface so rendering can be tested without a server.</summary>
public interface IEmailTransport
{
    Task<ChannelSendResult> SendAsync(MimeMessage message, CancellationToken ct);
}

public sealed record EmailSettings(string? Address);

public sealed class EmailChannel(IEmailTransport transport, IOptions<SmtpOptions> smtp) : INotificationChannel
{
    public const string TypeKey = "Email";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Type => TypeKey;

    public IReadOnlyList<string> ValidateSettings(string settingsJson)
    {
        var address = Parse(settingsJson)?.Address;
        if (string.IsNullOrWhiteSpace(address)) return new[] { "An email address is required." };
        return IsValidAddress(address) ? Array.Empty<string>() : new[] { "The email address is not valid." };
    }

    public string MaskSettings(string settingsJson)
    {
        var address = Parse(settingsJson)?.Address;
        if (string.IsNullOrWhiteSpace(address) || !address.Contains('@')) return "(not set)";
        var at = address.IndexOf('@');
        var local = address[..at];
        return $"{local[..Math.Min(1, local.Length)]}***{address[at..]}";
    }

    public async Task<ChannelSendResult> SendAsync(NotificationMessage message, string settingsJson, CancellationToken ct)
    {
        var address = Parse(settingsJson)?.Address;
        if (address is null || !IsValidAddress(address)) return ChannelSendResult.Permanent("The email address is not valid.");

        var mime = Render(message, address, smtp.Value);
        return await transport.SendAsync(mime, ct);
    }

    internal static bool IsValidAddress(string address) =>
        !address.Any(c => c is '\r' or '\n' or '<' or '>' or ',' or ';') &&
        MailAddress.TryCreate(address, out var parsed) && parsed.Address == address.Trim();

    /// <summary>Builds the message. All event text is untrusted and is escaped for the HTML part.</summary>
    internal static MimeMessage Render(NotificationMessage message, string toAddress, SmtpOptions smtp)
    {
        var ev = message.Event;
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(smtp.FromName, smtp.FromAddress));
        mime.To.Add(MailboxAddress.Parse(toAddress.Trim()));
        mime.Subject = "[Alert] " + OneLine(ev.Title, 120);

        var link = SafeLink(ev.Url);
        var text = new StringBuilder()
            .AppendLine(OneLine(ev.Title, 300))
            .AppendLine()
            .AppendLine(ev.Summary);
        if (!string.IsNullOrWhiteSpace(ev.Location)) text.AppendLine().AppendLine("Where: " + OneLine(ev.Location, 200));
        text.AppendLine("When: " + ev.OccurredAt.ToString("u"));
        if (link is not null) text.AppendLine("Link: " + link);
        text.AppendLine().AppendLine("Alert rule: " + OneLine(message.RuleName, 100));
        if (ev.IsSimulated) text.AppendLine("(Simulated event)");

        Func<string?, string> enc = s => WebUtility.HtmlEncode(s) ?? "";
        var html = new StringBuilder()
            .Append("<h2>").Append(enc(OneLine(ev.Title, 300))).Append("</h2>")
            .Append("<p>").Append(enc(ev.Summary)).Append("</p>");
        if (!string.IsNullOrWhiteSpace(ev.Location)) html.Append("<p><b>Where:</b> ").Append(enc(ev.Location)).Append("</p>");
        html.Append("<p><b>When:</b> ").Append(enc(ev.OccurredAt.ToString("u"))).Append("</p>");
        if (link is not null) html.Append("<p><a href=\"").Append(enc(link)).Append("\">Open source</a></p>");
        html.Append("<hr><small>Alert rule: ").Append(enc(OneLine(message.RuleName, 100))).Append("</small>");
        if (ev.IsSimulated) html.Append("<br><small>(Simulated event)</small>");

        mime.Body = new BodyBuilder { TextBody = text.ToString(), HtmlBody = html.ToString() }.ToMessageBody();
        return mime;
    }

    /// <summary>Only http and https links are ever rendered (blocks javascript: and similar).</summary>
    internal static string? SafeLink(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.AbsoluteUri : null;

    internal static string OneLine(string? value, int max)
    {
        var s = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return s.Length <= max ? s : s[..max] + "…";
    }

    private static EmailSettings? Parse(string json)
    {
        try { return JsonSerializer.Deserialize<EmailSettings>(json, Json); }
        catch (JsonException) { return null; }
    }
}
