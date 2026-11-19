using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EventAlertManagement.Core.Contracts;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EventAlertManagement.Api.Auth;

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) CreateUserToken(Guid userId, string email);
    (string Token, DateTime ExpiresAt) CreateAdminToken();
    bool IsAdminKeyValid(string? candidate);
}

public sealed class TokenService(IOptions<AuthOptions> options, IClock clock) : ITokenService
{
    private readonly AuthOptions _o = options.Value;

    public (string, DateTime) CreateUserToken(Guid userId, string email) =>
        Create(new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(ClaimTypes.Role, Roles.User),
        });

    public (string, DateTime) CreateAdminToken() =>
        Create(new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "admin"),
            new Claim(ClaimTypes.Role, Roles.Admin),
        });

    /// <summary>Constant-time comparison. An empty configured key never matches anything.</summary>
    public bool IsAdminKeyValid(string? candidate)
    {
        if (string.IsNullOrEmpty(_o.AdminKey) || string.IsNullOrEmpty(candidate)) return false;
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(_o.AdminKey));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(candidate));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    private (string, DateTime) Create(IEnumerable<Claim> claims)
    {
        var now = clock.UtcNow;
        var expires = now.AddHours(_o.TokenHours);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_o.SigningKey));
        var token = new JwtSecurityToken(_o.Issuer, _o.Audience, claims, now, expires,
            new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
