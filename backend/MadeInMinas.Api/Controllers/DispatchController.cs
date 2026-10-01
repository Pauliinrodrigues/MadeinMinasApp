using System.Security.Claims;
using MadeInMinas.Api.DTOs.Dispatch;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Controllers;

[ApiController]
[Route("api/dispatch/orders")]
[Authorize(Policy = AccessPolicies.WorkDispatch)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DispatchController(DispatchService dispatch) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DispatchPageResponse>> List([FromQuery] DispatchQuery query, CancellationToken cancellationToken) =>
        await dispatch.ListAsync(query, cancellationToken);

    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<DispatchOrderResponse>> Status(Guid id, DispatchStatusRequest request, CancellationToken cancellationToken) =>
        await dispatch.SetStatusAsync(Guid.Parse(User.FindFirstValue("sub")!), Guid.Parse(User.FindFirstValue("auth_stamp")!), id, request, cancellationToken);
}
