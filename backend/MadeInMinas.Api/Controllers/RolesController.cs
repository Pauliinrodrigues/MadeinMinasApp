using MadeInMinas.Api.Data;
using MadeInMinas.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Controllers;

[ApiController]
[Route("api/roles")]
[Authorize(Policy = AccessPolicies.ManageUsers)]
public sealed class RolesController(AppDbContext database) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<RoleResponse[]>> GetAll(CancellationToken cancellationToken) =>
        await database.Roles.AsNoTracking().OrderBy(role => role.Id)
            .Select(role => new RoleResponse(role.Id, role.Code, role.Name))
            .ToArrayAsync(cancellationToken);
}

public sealed record RoleResponse(int Id, string Code, string Name);
