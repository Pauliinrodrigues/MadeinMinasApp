using MadeInMinas.Api.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace MadeInMinas.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/system")]
public sealed class SystemController : ControllerBase
{
    [HttpGet("status")]
    [ProducesResponseType<SystemStatusResponse>(StatusCodes.Status200OK)]
    public ActionResult<SystemStatusResponse> GetStatus() => new SystemStatusResponse("available");
}
