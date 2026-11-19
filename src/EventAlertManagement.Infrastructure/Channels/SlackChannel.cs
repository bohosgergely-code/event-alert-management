using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EventAlertManagement.Core.Contracts;

namespace EventAlertManagement.Infrastructure.Channels;

public sealed record SlackSettings(string? WebhookUrl);

/// <summary>
/// Posts to a Slack Incoming Webhook. The webhook URL is user supplied, so it is restricted to
/// Slack's own host over HTTPS and redirects are never followed (SSRF protection, ADR-005).
/// </summary>
public sealed class SlackChannel(IHttpClientFactory httpFactory) : INotificationChannel
{
    public const string TypeKey = "Slack";
    public const string HttpClientName = "slack";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Type => TypeKey;

    public IReadOnlyList<string> ValidateSettings(string settingsJson)
    {
        var url = Parse(settingsJson)?.WebhookUrl;
        if (string.IsNullOrWhiteSpace(url)) return new[] { "A Slack webhook URL is required." };
        return IsAllowedWebhook(url) ? Array.Empty<string>()
            : new[] { "The webhook URL must be an https://hooks.slack.com/services/... address." };
    }

    public string MaskSettings(string settingsJson)
    {
        var url = Parse(settingsJson)?.WebhookUrl;
        return string.IsNullOrWhiteSpace(url) || !IsAllowedWebhook(url)
            ? "(not set)"
            : $"https://hooks.slack.com/services/…{url[^4..]}";
    }

    public async Task<ChannelSendResult> SendAsync(NotificationMessage message, string settingsJson, CancellationToken ct)
    {
        var url = Parse(settingsJson)?.WebhookUrl;
        if (url is null || !IsAllowedWebhook(url)) return ChannelSendResult.Permanent("The Slack webhook URL is not valid.");

        try
        {
            var http = httpFactory.CreateClient(HttpClientName);
            using var response = await http.PostAsJsonAsync(url, new { text = RenderText(message) }, Json, ct);
            var code = (int)response.StatusCode;

            if (response.IsSuccessStatusCode) return ChannelSendResult.Ok();
            if (code is 408 or 429 || code >= 500) return ChannelSendResult.Transient($"Slack responded with HTTP {code}.");
            // 400 invalid payload, 403 forbidden, 404/410 webhook removed or channel archived, redirects: retrying cannot help.
            return ChannelSendResult.Permanent($"Slack rejected the message (HTTP {code}).");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ChannelSendResult.Transient("Could not reach Slack.");
        }
    }

    internal static bool IsAllowedWebhook(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        string.Equals(uri.Host, "hooks.slack.com", StringComparison.OrdinalIgnoreCase) &&
        uri.IsDefaultPort &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        uri.AbsolutePath.StartsWith("/services/", StringComparison.Ordinal);

    /// <summary>Builds Slack mrkdwn. Event text is untrusted: &amp;, &lt; and &gt; are escaped as Slack requires.</summary>
    internal static string RenderText(NotificationMessage message)
    {
        var ev = message.Event;
        var sb = new StringBuilder();
        sb.Append(":rotating_light: *").Append(Escape(OneLine(ev.Title, 200))).Append('*');
        if (!string.IsNullOrWhiteSpace(ev.Summary)) sb.Append('\n').Append(Escape(Truncate(ev.Summary, 600)));
        if (!string.IsNullOrWhiteSpace(ev.Location)) sb.Append("\n_").Append(Escape(OneLine(ev.Location, 150))).Append('_');
        var link = EmailChannel.SafeLink(ev.Url);
        if (link is not null) sb.Append("\n<").Append(link.Replace("|", "%7C").Replace(">", "%3E")).Append("|Open source>");
        sb.Append("\nRule: ").Append(Escape(OneLine(message.RuleName, 100)));
        if (ev.IsSimulated) sb.Append(" (simulated)");
        return sb.ToString();
    }

    internal static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    private static string OneLine(string? v, int max) => EmailChannel.OneLine(v, max);
    private static string Truncate(string v, int max) => v.Length <= max ? v : v[..max] + "…";

    private static SlackSettings? Parse(string json)
    {
        try { return JsonSerializer.Deserialize<SlackSettings>(json, Json); }
        catch (JsonException) { return null; }
    }
}
