using System.Text;
using System.Threading.RateLimiting;
using EventAlertManagement.Api.Auth;
using EventAlertManagement.Api.Services;
using EventAlertManagement.Infrastructure.Hosting;
using EventAlertManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ---- authentication settings are read lazily and validated when the host starts ----
// (Reading them eagerly here would miss configuration added later, for example by tests or the hosting environment.)
builder.Services.AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.Section))
    .Validate(o => o.SigningKey.Length >= 32,
        "Auth:SigningKey must be set to a secret of at least 32 characters (use user secrets or an environment variable).")
    .Validate(o => o.AdminKey.Length == 0 || o.AdminKey.Length >= 12,
        "Auth:AdminKey must be at least 12 characters (32 or more is recommended), or empty to switch admin sign-in off.")
    .ValidateOnStart();
builder.Services.AddSingleton<ITokenService, TokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<AuthOptions>>((jwt, auth) =>
    {
        var a = auth.Value;
        jwt.MapInboundClaims = false; // keep "sub" as "sub"
        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = a.Issuer,
            ValidateAudience = true, ValidAudience = a.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(a.SigningKey)),
            ValidateLifetime = true, ClockSkew = TimeSpan.FromMinutes(1),
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
            NameClaimType = "sub",
        };
    });

// Everything requires a signed-in user unless an endpoint says [AllowAnonymous]. Forgetting an attribute fails closed.
builder.Services.AddAuthorization(o =>
    o.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Test messages go to destinations the user typed, possibly third parties: a small budget per signed-in user,
    // across all of their channels.
    o.AddPolicy("test", http => RateLimitPartition.GetFixedWindowLimiter(
        http.User.FindFirst("sub")?.Value ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 6, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("auth", http =>
    {
        var limit = http.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value.LoginAttemptsPerMinute;
        return RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, limit), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
    });
});

var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? new[] { "http://localhost:4200" };
builder.Services.AddCors(o => o.AddPolicy("web", p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddProblemDetails();
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()))
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ctx =>
        new BadRequestObjectResult(new ValidationProblemDetails(ctx.ModelState) { Status = 400 }));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "EventAlertManagement API", Version = "v1" });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
        Description = "Sign in through /api/auth/login (or /api/auth/admin-login) and paste the token here.",
    });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>(),
    });
    o.SupportNonNullableReferenceTypes();
    o.SchemaFilter<RequiredNonNullableSchemaFilter>();
    o.CustomSchemaIds(SchemaNames.For);
});

builder.Services.AddEventAlertInfrastructure(builder.Configuration, runBackgroundWorkers: builder.Configuration.GetValue("Workers:Enabled", true));
builder.Services.AddScoped<RuleBuilder>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    await DbInitializer.InitializeAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());

// Unexpected errors become a plain problem response. Stack traces and messages are never sent to clients.
app.UseExceptionHandler(errors => errors.Run(async ctx =>
{
    var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
    logger.LogError(ctx.Features.Get<IExceptionHandlerFeature>()?.Error, "Unhandled error on {Path}", ctx.Request.Path);
    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await ctx.Response.WriteAsJsonAsync(new ProblemDetails { Status = 500, Title = "Something went wrong." });
}));

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing")) app.UseHttpsRedirection();

// Nothing here needs a big request body. Refusing early keeps one caller from making the server read and parse megabytes.
const long MaxRequestBytes = 256 * 1024;
app.Use(async (ctx, next) =>
{
    if (ctx.Request.ContentLength > MaxRequestBytes) { ctx.Response.StatusCode = StatusCodes.Status413PayloadTooLarge; return; }
    if (ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = MaxRequestBytes;
    await next();
});

app.UseCors("web");
app.UseAuthentication();
app.UseRateLimiter();   // after authentication, so the per-user policy sees who is calling
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program;
