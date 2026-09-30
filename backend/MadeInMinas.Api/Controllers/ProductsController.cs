using System.Security.Claims;
using MadeInMinas.Api.DTOs.Products;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Controllers;

[ApiController]
[Route("api/products")]
[Authorize(Policy = AccessPolicies.ManageCatalog)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProductsController(ProductService products) : ControllerBase
{
    private Guid ActorId => Guid.Parse(User.FindFirstValue("sub")!);
    private Guid ActorStamp => Guid.Parse(User.FindFirstValue("auth_stamp")!);

    [HttpGet]
    public async Task<ActionResult<ProductPageResponse>> List([FromQuery] ProductListQuery query, CancellationToken cancellationToken) =>
        await products.ListAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProductResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await products.GetAsync(id, cancellationToken);

    [HttpPost]
    public async Task<ActionResult<ProductResponse>> Create(ProductRequest request, CancellationToken cancellationToken)
    {
        var product = await products.CreateAsync(ActorId, ActorStamp, request, cancellationToken);
        return CreatedAtAction(nameof(Get), new
        {
            id = product.Id
        }, product);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProductResponse>> Update(Guid id, ProductRequest request, CancellationToken cancellationToken) =>
        await products.UpdateAsync(ActorId, ActorStamp, id, request, cancellationToken);

    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<ProductResponse>> SetStatus(Guid id, ProductStatusRequest request, CancellationToken cancellationToken) =>
        await products.SetStatusAsync(ActorId, ActorStamp, id, request.IsActive!.Value, cancellationToken);

    [HttpPut("{id:guid}/availability")]
    public async Task<ActionResult<ProductResponse>> SetAvailability(Guid id, ProductAvailabilityRequest request, CancellationToken cancellationToken) =>
        await products.SetAvailabilityAsync(ActorId, ActorStamp, id, request.IsAvailable!.Value, cancellationToken);
}

