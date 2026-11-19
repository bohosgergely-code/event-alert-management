using System.Net.Mail;
using EventAlertManagement.Api.Auth;
using EventAlertManagement.Api.Dtos;
using EventAlertManagement.Core.Contracts;
using EventAlertManagement.Core.Domain;
using EventAlertManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EventAlertManagement.Api.Controllers;

/// <summary>
/// Deliberately minimal sign-in (ADR-007): knowing an email address is enough to act as that user.
/// This is NOT production-safe and is stated as a limitation in the README.
/// </summary>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
[EnableRateLimiting("auth")]
public class AuthController(AppDbContext db, ITokenService tokens, IClock clock) : ControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        if (!IsValidEmail(email))
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["email"] = new[] { "A valid email address is required." } }));

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            user = new User { Email = email, DisplayName = email, Role = UserRole.User, CreatedAt = clock.UtcNow };
            db.Users.Add(user);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) // Two first sign-ins at the same moment: the other one won.
            {
                db.Entry(user).State = EntityState.Detached;
                user = await db.Users.FirstAsync(u => u.Email == email, ct);
            }
        }

        var (token, expires) = tokens.CreateUserToken(user.Id, user.Email);
        return new TokenResponse(token, expires, Roles.User);
    }

    [HttpPost("admin-login")]
    public ActionResult<TokenResponse> AdminLogin(AdminLoginRequest request)
    {
        // Same answer whether the key is wrong or admin sign-in is switched off.
        if (!tokens.IsAdminKeyValid(request.Key)) return Unauthorized(new ProblemDetails { Title = "Invalid admin key.", Status = 401 });
        var (token, expires) = tokens.CreateAdminToken();
        return new TokenResponse(token, expires, Roles.Admin);
    }

    internal static bool IsValidEmail(string email) =>
        email.Length is > 3 and <= 320 &&
        !email.Any(c => c is '\r' or '\n' or '<' or '>' or ',' or ';' or ' ') &&
        MailAddress.TryCreate(email, out var parsed) && parsed.Address == email && email.Contains('.', StringComparison.Ordinal);
}
