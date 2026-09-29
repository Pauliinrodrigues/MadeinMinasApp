using System.Security.Claims;
using MadeInMinas.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Security;

public sealed class StaffJwtEvents(AppDbContext database) : JwtBearerEvents
{
    public override async Task TokenValidated(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (!Guid.TryParse(principal?.FindFirstValue("sub"), out var userId) ||
            !Guid.TryParse(principal.FindFirstValue("auth_stamp"), out var stamp))
        {
            context.Fail("Invalid session.");
            return;
        }

        var user = await database.Users.AsNoTracking().Where(user => user.Id == userId)
            .Select(user => new { user.IsActive, user.SecurityStamp, Role = user.Role.Code })
            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);

        if (user is null || !user.IsActive || user.SecurityStamp != stamp ||
            user.Role != principal.FindFirstValue("role"))
        {
            context.Fail("Invalid session.");
        }
    }
}
