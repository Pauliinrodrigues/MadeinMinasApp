using MadeInMinas.Api.DTOs.Costing;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Controllers;

[ApiController]
[Route("api/products/{productId:guid}/costing")]
[Authorize(Policy = AccessPolicies.ManageCatalog)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProductCostsController(ProductCostService costs) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ProductCostResponse>> Get(Guid productId, CancellationToken cancellationToken) =>
        await costs.GetAsync(productId, cancellationToken);
}
