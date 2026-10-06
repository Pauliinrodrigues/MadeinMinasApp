using MadeInMinas.Api.DTOs.Stock;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Controllers;

[ApiController]
[Route("api/stock/replenishment")]
[Authorize(Policy = AccessPolicies.ManageCatalog)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StockReplenishmentController(StockService stock) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<StockReplenishmentResponse>> Get([FromQuery] StockReplenishmentQuery query,
        CancellationToken cancellationToken) => await stock.ReplenishmentAsync(query, cancellationToken);
}
