using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace EventAlertManagement.Api.Auth;

public static class CurrentUserExtensions
{
    /// <summary>The signed-in user's id. Throws if the token has none (cannot happen behind the User role policy).</summary>
    public static Guid UserId(this ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var id) ? id : throw new InvalidOperationException("Token has no user id.");
    }
}
