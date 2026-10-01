using System.Security.Claims;
using MadeInMinas.Api.DTOs.Kitchen;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Controllers;

[ApiController]
[Route("api/kitchen/orders")]
[Authorize(Policy = AccessPolicies.WorkKitchen)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class KitchenController(KitchenService kitchen) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<KitchenBoardResponse>> Board([FromQuery] KitchenBoardQuery query, CancellationToken cancellationToken) =>
        await kitchen.BoardAsync(query, cancellationToken);

    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<KitchenOrderResponse>> Status(Guid id, KitchenStatusRequest request, CancellationToken cancellationToken) =>
        await kitchen.SetStatusAsync(Guid.Parse(User.FindFirstValue("sub")!), Guid.Parse(User.FindFirstValue("auth_stamp")!), id, request, cancellationToken);
}
