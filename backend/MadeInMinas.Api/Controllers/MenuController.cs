using MadeInMinas.Api.DTOs.Menu;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Controllers;

[ApiController]
[Route("api/menu")]
[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MenuController(PublicMenuService menu) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PublicMenuResponse>> Get([FromQuery] PublicMenuQuery query, CancellationToken cancellationToken) =>
        await menu.GetAsync(query, cancellationToken);
}
