using EventAlertManagement.Api.Auth;
using EventAlertManagement.Api.Dtos;
using EventAlertManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventAlertManagement.Api.Controllers;

[ApiController]
[Route("api/me")]
[Authorize(Roles = Roles.User)]
public class MeController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MeResponse>> Get(CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == User.UserId(), ct);
        return user is null ? Unauthorized() : new MeResponse(user.Id, user.Email, user.DisplayName, user.Role.ToString());
    }
}
