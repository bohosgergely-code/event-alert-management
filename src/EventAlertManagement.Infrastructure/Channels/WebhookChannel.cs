using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EventAlertManagement.Core.Contracts;
using EventAlertManagement.Core.Domain;
using EventAlertManagement.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EventAlertManagement.Infrastructure.Channels;

public sealed class WebhookOptions
{
    /// <summary>
    /// Off by default. Sending through a proxy means the proxy, not this application, makes the connection, so the
    /// address check at connect time can no longer see the real destination and protection depends on the proxy.
    /// </summary>
    public bool UseSystemProxy { get; set; }
    public int TimeoutSeconds { get; set; } = 10;
}

public sealed record WebhookSettings(string? Url, string? Secret);

/// <summary>
/// Posts the event as signed JSON to a URL the user supplies (ADR-019). The destination is fully user controlled,
/// so it is restricted to public https addresses, checked again when connecting, and redirects are never followed.
/// </summary>
public sealed class WebhookChannel(IHttpClientFactory httpFactory, IClock clock) : INotificationChannel
{
    public const string TypeKey = "Webhook";
    public const string HttpClientName = "webhook";
    public const int MinSecretLength = 16;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Type => TypeKey;

    public IReadOnlyList<string> ValidateSettings(string settingsJson)
    {
        var s = Parse(settingsJson);
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(s?.Url)) problems.Add("A webhook URL is required.");
        else if (!TryGetAllowedUri(s.Url, out var reason)) problems.Add(reason!);

        if (string.IsNullOrEmpty(s?.Secret)) problems.Add($"A signing secret of at least {MinSecretLength} characters is required.");
        else if (s.Secret.Length < MinSecretLength) problems.Add($"The signing secret must be at least {MinSecretLength} characters long.");
        else if (s.Secret.Length > 200 || s.Secret.Any(char.IsControl)) problems.Add("The signing secret is too long or contains control characters.");

        return problems;
    }

    public string MaskSettings(string settingsJson)
    {
        var url = Parse(settingsJson)?.Url;
        return url is not null && TryGetAllowedUri(url, out _) ? $"https://{new Uri(url).Host}/… (signed)" : "(not set)";
    }

    public async Task<ChannelSendResult> SendAsync(NotificationMessage message, string settingsJson, CancellationToken ct)
    {
        var settings = Parse(settingsJson);
        if (settings?.Url is null || settings.Secret is null || !TryGetAllowedUri(settings.Url, out _))
            return ChannelSendResult.Permanent("The webhook settings are not valid.");

        var body = BuildBody(message);
        var timestamp = new DateTimeOffset(clock.UtcNow).ToUnixTimeSeconds().ToString();

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Url)
        {
            Content = new StringContent(body, new UTF8Encoding(false), new MediaTypeHeaderValue("application/json")),
        };
        request.Headers.TryAddWithoutValidation("X-EventAlert-Timestamp", timestamp);
        request.Headers.TryAddWithoutValidation("X-EventAlert-Signature", "sha256=" + Sign(settings.Secret, timestamp, body));
        request.Headers.TryAddWithoutValidation("X-EventAlert-Event", message.Event.Id.ToString());
        request.Headers.UserAgent.ParseAdd("EventAlertManagement/1.0");

        try
        {
            var http = httpFactory.CreateClient(HttpClientName);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var code = (int)response.StatusCode;

            if (response.IsSuccessStatusCode) return ChannelSendResult.Ok();
            if (code is >= 300 and < 400) return ChannelSendResult.Permanent($"The server answered with a redirect (HTTP {code}). Redirects are not followed.");
            if (code is 408 or 429 || code >= 500) return ChannelSendResult.Transient($"The server answered HTTP {code}.");
            return ChannelSendResult.Permanent($"The server rejected the message (HTTP {code}).");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (HttpRequestException ex) when (Find<BlockedAddressException>(ex) is not null)
        {
            return ChannelSendResult.Permanent("The address points to a private or internal network and is not allowed.");
        }
        catch (HttpRequestException ex) when (Find<AuthenticationException>(ex) is not null)
        {
            return ChannelSendResult.Permanent("The server's certificate was not accepted.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Only fixed wording is stored: the original message could contain the address.
            return ChannelSendResult.Transient(ex is TaskCanceledException ? "The server did not answer in time." : "Could not reach the server.");
        }
    }

    // ---- exposed for tests ----

    /// <summary>HMAC-SHA256 over "timestamp.body", as lower-case hex. The timestamp lets receivers reject old (replayed) messages.</summary>
    public static string Sign(string secret, string timestamp, string body) => HmacHex(secret, $"{timestamp}.{body}");

    public static string HmacHex(string key, string data) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(data))).ToLowerInvariant();

    internal static string BuildBody(NotificationMessage m)
    {
        var e = m.Event;
        return JsonSerializer.Serialize(new
        {
            id = e.Id, title = e.Title, summary = e.Summary, url = e.Url, location = e.Location,
            category = e.Category.ToString(), severity = e.Severity, occurredAt = e.OccurredAt.ToString("O"),
            isSimulated = e.IsSimulated, rule = m.RuleName, source = e.SourceKey,
        }, Json);
    }

    /// <summary>First-line checks on the address as typed. The decisive check happens when the connection is made.</summary>
    internal static bool TryGetAllowedUri(string url, out string? reason)
    {
        reason = null;
        if (url.Length > 2000) { reason = "The webhook URL is too long."; return false; }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        { reason = "The webhook URL must be an https:// address."; return false; }
        if (!string.IsNullOrEmpty(uri.UserInfo)) { reason = "The webhook URL must not contain a user name or password."; return false; }

        // Uri turns forms such as http://2130706433, 0x7f.1 or 127.1 into the real address, so the check sees what would be contacted.
        // Other spellings (a trailing dot, full-width or circled digits, ideographic full stops) are folded to plain text first.
        // This is only the first line of defence; the connection guard decides on the address that is really used.
        var spellings = new[] { uri.IdnHost, uri.Host, uri.DnsSafeHost }
            .Select(h => h.Normalize(System.Text.NormalizationForm.FormKC).Replace('\u3002', '.').Replace('\uFF0E', '.').Replace('\uFF61', '.').Trim('[', ']').TrimEnd('.'))
            .Distinct().ToList();
        var literal = spellings.Select(h => IPAddress.TryParse(h, out var parsed) ? parsed : null).Where(a => a is not null).ToList();
        if (literal.Count > 0)
        {
            if (literal.Any(a => !PublicAddressPolicy.IsPublic(a!))) { reason = "The webhook URL points to a private or internal address."; return false; }
            return true;
        }
        // A name whose last part is a number is never a real public domain. It is an attempt to spell an address.
        if (spellings.Any(h => h.Split('.')[^1].All(char.IsDigit))) { reason = "The webhook URL does not look like a valid address."; return false; }

        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        var internalName = !host.Contains('.') || host == "localhost" || host.EndsWith(".localhost") || host.EndsWith(".local")
                           || host.EndsWith(".internal") || host.EndsWith(".localdomain") || host.EndsWith(".lan") || host.EndsWith(".home.arpa");
        if (internalName) { reason = "The webhook URL points to a private or internal address."; return false; }
        return true;
    }

    private static T? Find<T>(Exception ex) where T : Exception
    {
        for (var e = ex; e is not null; e = e.InnerException) if (e is T match) return match;
        return null;
    }

    private static WebhookSettings? Parse(string json)
    {
        try { return JsonSerializer.Deserialize<WebhookSettings>(json, Json); }
        catch (JsonException) { return null; }
    }
}

public static class WebhookChannelRegistration
{
    /// <summary>The connection settings that make the protection work. Kept in one place so a test can pin them down.</summary>
    internal static SocketsHttpHandler CreateHandler(WebhookOptions o) => new()
    {
        AllowAutoRedirect = false,                                   // a redirect could point anywhere, including inside the network
        UseProxy = o.UseSystemProxy,
        ConnectCallback = o.UseSystemProxy ? null : SsrfGuard.CreateConnectCallback(),
        ConnectTimeout = TimeSpan.FromSeconds(5),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),          // forces a fresh DNS answer and a fresh address check
        MaxResponseHeadersLength = 16,                               // kilobytes
    };

    /// <summary>The single line a new channel needs in the composition root.</summary>
    public static IServiceCollection AddWebhookChannel(this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration config)
    {
        services.Configure<WebhookOptions>(config.GetSection("Webhook"));
        services.AddHttpClient(WebhookChannel.HttpClientName)
            .RemoveAllLoggers() // the default request logging writes the full address, which contains secrets, at Information level
            .ConfigurePrimaryHttpMessageHandler(sp => CreateHandler(sp.GetRequiredService<IOptions<WebhookOptions>>().Value))
            .ConfigureHttpClient((sp, c) => c.Timeout = TimeSpan.FromSeconds(Math.Clamp(sp.GetRequiredService<IOptions<WebhookOptions>>().Value.TimeoutSeconds, 2, 60)));
        services.AddSingleton<INotificationChannel, WebhookChannel>();
        return services;
    }
}
