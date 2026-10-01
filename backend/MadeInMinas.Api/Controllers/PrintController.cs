using MadeInMinas.Api.Security;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Controllers;

[ApiController]
[Route("api/print/orders")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PrintController(KitchenService kitchen, DispatchService dispatch, TimeProvider clock) : ControllerBase
{
    [HttpGet("{id:guid}/kitchen")]
    [Authorize(Policy = AccessPolicies.PrintKitchen)]
    public async Task<IActionResult> Kitchen(Guid id, CancellationToken cancellationToken) =>
        Ok(new { generatedAt = clock.GetUtcNow(), order = await kitchen.GetAsync(id, cancellationToken) });

    [HttpGet("{id:guid}/dispatch")]
    [Authorize(Policy = AccessPolicies.PrintDispatch)]
    public async Task<IActionResult> Dispatch(Guid id, CancellationToken cancellationToken) =>
        Ok(new { generatedAt = clock.GetUtcNow(), order = await dispatch.GetAsync(id, cancellationToken) });
}
