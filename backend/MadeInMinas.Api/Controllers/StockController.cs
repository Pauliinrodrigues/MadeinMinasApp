using System.Security.Claims;
using MadeInMinas.Api.DTOs.Stock;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Controllers;

[ApiController]
[Route("api/ingredients/{ingredientId:guid}/stock")]
[Authorize(Policy = AccessPolicies.ManageCatalog)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StockController(StockService stock) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<StockResponse>> Get(Guid ingredientId, [FromQuery] StockQuery query, CancellationToken cancellationToken) =>
        await stock.GetAsync(ingredientId, query, cancellationToken);

    [HttpPost("movements")]
    public async Task<ActionResult<StockMovementResponse>> Create(Guid ingredientId, StockMovementRequest request, CancellationToken cancellationToken) =>
        await stock.CreateAsync(Guid.Parse(User.FindFirstValue("sub")!), Guid.Parse(User.FindFirstValue("auth_stamp")!),
            ingredientId, request, cancellationToken);
}
