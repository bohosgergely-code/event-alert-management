using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;
using Swashbuckle.AspNetCore.Swagger;

namespace EventAlertManagement.Tests.Api;

public class ApiSecurityTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    public ApiSecurityTests(ApiFactory f) => _f = f;

    private static string Unique(string prefix = "u") => $"{prefix}-{Guid.NewGuid():N}@example.com";

    // ---- the generic sweep: no endpoint can be forgotten ----

    private static readonly string[] PublicRoutes = { "api/auth/login", "api/auth/admin-login" };

    private IEnumerable<(string Method, string Path)> AllRoutes()
    {
        var source = _f.Services.GetRequiredService<EndpointDataSource>();
        foreach (var endpoint in source.Endpoints.OfType<RouteEndpoint>())
        {
            if (endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is null) continue;
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? new[] { "GET" };
            var path = System.Text.RegularExpressions.Regex.Replace(endpoint.RoutePattern.RawText!, @"\{(\w+)(:guid)?\}",
                m => m.Groups[2].Success ? Guid.NewGuid().ToString() : "x");
            foreach (var method in methods) yield return (method, "/" + path);
        }
    }

    private static HttpRequestMessage Req(string method, string path) =>
        new(new HttpMethod(method), path) { Content = method is "GET" or "DELETE" ? null : JsonContent.Create(new { }) };

    [Fact]
    public void The_sweep_actually_finds_the_endpoints()
        => Assert.True(AllRoutes().Count() >= 30, "Route discovery found too few endpoints; the sweep would prove nothing.");

    [Fact]
    public async Task Every_endpoint_except_sign_in_rejects_anonymous_callers()
    {
        var client = _f.Anonymous();
        var failures = new List<string>();
        foreach (var (method, path) in AllRoutes().Where(r => !PublicRoutes.Any(p => r.Path.TrimStart('/') == p)))
        {
            var response = await client.SendAsync(Req(method, path));
            if (response.StatusCode != HttpStatusCode.Unauthorized) failures.Add($"{method} {path} -> {(int)response.StatusCode}");
        }
        Assert.Empty(failures);
    }

    [Fact]
    public async Task Every_admin_endpoint_rejects_a_normal_user()
    {
        var user = await _f.UserAsync(Unique());
        var failures = new List<string>();
        foreach (var (method, path) in AllRoutes().Where(r => r.Path.StartsWith("/api/admin")))
        {
            var response = await user.SendAsync(Req(method, path));
            if (response.StatusCode != HttpStatusCode.Forbidden) failures.Add($"{method} {path} -> {(int)response.StatusCode}");
        }
        Assert.Empty(failures);
    }

    [Fact]
    public async Task Every_user_endpoint_rejects_an_admin_token()
    {
        var admin = await _f.AdminAsync();
        var failures = new List<string>();
        foreach (var (method, path) in AllRoutes().Where(r => r.Path.StartsWith("/api/") && !r.Path.StartsWith("/api/admin") && !r.Path.StartsWith("/api/auth")))
        {
            var response = await admin.SendAsync(Req(method, path));
            if (response.StatusCode != HttpStatusCode.Forbidden) failures.Add($"{method} {path} -> {(int)response.StatusCode}");
        }
        Assert.Empty(failures);
    }

    // ---- tokens ----

    [Fact]
    public async Task Forged_expired_or_misdirected_tokens_are_rejected()
    {
        var bad = new Dictionary<string, string>
        {
            ["signed with another key"] = ApiFactory.MakeToken("another-key-another-key-another-key-123456", "Admin"),
            ["expired"] = ApiFactory.MakeToken(ApiFactory.SigningKey, "Admin", expires: DateTime.UtcNow.AddHours(-3)),
            ["wrong issuer"] = ApiFactory.MakeToken(ApiFactory.SigningKey, "Admin", issuer: "someone-else"),
            ["wrong audience"] = ApiFactory.MakeToken(ApiFactory.SigningKey, "Admin", audience: "someone-else"),
            ["garbage"] = "not.a.token",
        };
        foreach (var (why, token) in bad)
        {
            var client = _f.Anonymous();
            client.DefaultRequestHeaders.Authorization = new("Bearer", token);
            var response = await client.GetAsync("/api/admin/overview");
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{why}: got {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task A_token_with_the_right_key_but_unsigned_algorithm_none_is_rejected()
    {
        var header = Convert.ToBase64String("{\"alg\":\"none\",\"typ\":\"JWT\"}"u8.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
            $"{{\"sub\":\"admin\",\"role\":\"Admin\",\"http://schemas.microsoft.com/ws/2008/06/identity/claims/role\":\"Admin\",\"iss\":\"EventAlertManagement\",\"aud\":\"EventAlertManagement.Web\",\"exp\":{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var client = _f.Anonymous();
        client.DefaultRequestHeaders.Authorization = new("Bearer", $"{header}.{payload}.");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/overview")).StatusCode);
    }

    // ---- sign in ----

    [Fact]
    public async Task Login_creates_the_user_once_and_ignores_letter_case_and_spaces()
    {
        var email = Unique("Case");
        var a = await _f.Anonymous().PostAsJsonAsync("/api/auth/login", new { email = email.ToUpperInvariant() });
        var b = await _f.Anonymous().PostAsJsonAsync("/api/auth/login", new { email = "  " + email.ToLowerInvariant() + " " });
        Assert.Equal(HttpStatusCode.OK, a.StatusCode);
        Assert.Equal(HttpStatusCode.OK, b.StatusCode);
        Assert.Equal(1, _f.WithDb(db => db.Users.Count(u => u.Email == email.ToLowerInvariant())));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("a@b.com\r\nBcc: x@y.com")]
    [InlineData("a b@example.com")]
    [InlineData("Name <a@b.com>")]
    public async Task Login_with_an_invalid_email_is_a_400(string email)
    {
        var response = await _f.Anonymous().PostAsJsonAsync("/api/auth/login", new { email });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_login_needs_the_right_key_and_gives_the_admin_role()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.Anonymous().PostAsJsonAsync("/api/auth/admin-login", new { key = "wrong" })).StatusCode);
        var ok = await _f.Anonymous().PostAsJsonAsync("/api/auth/admin-login", new { key = ApiFactory.AdminKey });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("Admin", (await ok.JsonAsync()).GetProperty("role").GetString());
    }

    [Fact]
    public async Task Admin_login_fails_closed_when_no_admin_key_is_configured()
    {
        using var factory = ApiFactory.With(new() { ["Auth:AdminKey"] = "" });
        var client = factory.Anonymous();
        foreach (var key in new[] { "", "anything", "dev-admin-key" })
        {
            var response = await client.PostAsJsonAsync("/api/auth/admin-login", new { key });
            Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest, $"key '{key}' gave {(int)response.StatusCode}");
            Assert.DoesNotContain("token", await response.BodyAsync());
        }
    }

    [Fact]
    public async Task Repeated_sign_in_attempts_are_rate_limited()
    {
        using var factory = ApiFactory.With(new() { ["Auth:LoginAttemptsPerMinute"] = "3" });
        var client = factory.Anonymous();
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++) codes.Add((await client.PostAsJsonAsync("/api/auth/admin-login", new { key = "guess" + i })).StatusCode);
        Assert.Equal(new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests }, codes);
    }

    [Fact]
    public void The_application_refuses_to_start_without_a_proper_signing_key()
    {
        using var factory = ApiFactory.With(new() { ["Auth:SigningKey"] = "short" });
        Assert.ThrowsAny<Exception>(() => factory.Anonymous());
    }

    // ---- API description ----

    [Fact]
    public void OpenAPI_document_can_be_generated_and_declares_bearer_security()
    {
        var swagger = _f.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        Assert.True(swagger.Paths.Count >= 20);
        Assert.Contains("Bearer", swagger.Components.SecuritySchemes.Keys);
        Assert.Contains("/api/admin/notifications/{id}/retry", swagger.Paths.Keys);
    }

    [Fact]
    public async Task Me_returns_the_signed_in_user()
    {
        var email = Unique("me");
        var client = await _f.UserAsync(email);
        var me = await (await client.GetAsync("/api/me")).JsonAsync();
        Assert.Equal(email, me.GetProperty("email").GetString());
    }
}
