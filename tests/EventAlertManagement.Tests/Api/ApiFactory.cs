using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using EventAlertManagement.Core.Contracts;
using EventAlertManagement.Infrastructure.Channels;
using EventAlertManagement.Infrastructure.Dispatch;
using EventAlertManagement.Infrastructure.Persistence;
using EventAlertManagement.Tests.Integration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using MimeKit;

namespace EventAlertManagement.Tests.Api;

public sealed class CapturingEmailTransport : IEmailTransport
{
    public List<MimeMessage> Messages { get; } = new();
    public Task<ChannelSendResult> SendAsync(MimeMessage message, CancellationToken ct)
    {
        lock (Messages) Messages.Add(message);
        return Task.FromResult(ChannelSendResult.Ok());
    }
}

/// <summary>The real application with a throwaway database, no background workers, and fakes for the outside world.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string SigningKey = "test-signing-key-that-is-long-enough-0123456789";
    public const string AdminKey = "test-admin-key";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eam-api-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string?> _extra;

    public FakeChannel Fake { get; } = new("Fake");
    public CapturingEmailTransport Email { get; } = new();

    /// <summary>xUnit class fixtures need a parameterless constructor.</summary>
    public ApiFactory() : this(null) { }

    private ApiFactory(Dictionary<string, string?>? extraConfig)
    {
        _extra = extraConfig ?? new();
        Directory.CreateDirectory(_dir);
    }

    /// <summary>A separate instance with different configuration (rate limit, missing keys, ...).</summary>
    public static ApiFactory With(Dictionary<string, string?> config) => new(config);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var values = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Data Source={Path.Combine(_dir, "test.db")}",
                ["DataProtection:KeysPath"] = Path.Combine(_dir, "keys"),
                ["Auth:SigningKey"] = SigningKey,
                ["Auth:AdminKey"] = AdminKey,
                ["Auth:LoginAttemptsPerMinute"] = "10000",
                ["Workers:Enabled"] = "false",
            };
            foreach (var kv in _extra) values[kv.Key] = kv.Value;
            config.AddInMemoryCollection(values);
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailTransport>();
            services.AddSingleton<IEmailTransport>(Email);
            services.AddSingleton<INotificationChannel>(Fake);
        });
    }

    // ---- helpers ----

    public HttpClient Anonymous() => CreateClient();

    public async Task<HttpClient> UserAsync(string email)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email });
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<HttpClient> AdminAsync()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/admin-login", new { key = AdminKey });
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Runs the dispatcher until nothing is due, like the background worker would.</summary>
    public async Task DispatchAsync()
    {
        int handled;
        do
        {
            using var scope = Services.CreateScope();
            handled = await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().RunOnceAsync(50, default);
        } while (handled > 0);
    }

    public T WithDb<T>(Func<AppDbContext, T> action)
    {
        using var scope = Services.CreateScope();
        return action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public static string MakeToken(string key, string role, string sub = "admin", DateTime? expires = null, string issuer = "EventAlertManagement", string audience = "EventAlertManagement.Web")
    {
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
        var exp = expires ?? DateTime.UtcNow.AddHours(1);
        var notBefore = exp < DateTime.UtcNow ? exp.AddHours(-2) : DateTime.UtcNow.AddMinutes(-1);
        var token = new JwtSecurityToken(issuer, audience,
            new[] { new Claim(JwtRegisteredClaimNames.Sub, sub), new Claim(ClaimTypes.Role, role) }, notBefore, exp, creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }
}

public static class HttpExtensions
{
    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage r) =>
        await r.Content.ReadFromJsonAsync<JsonElement>();

    public static async Task<string> BodyAsync(this HttpResponseMessage r) => await r.Content.ReadAsStringAsync();
}
