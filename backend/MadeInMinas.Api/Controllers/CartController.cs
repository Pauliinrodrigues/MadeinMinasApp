using MadeInMinas.Api.DTOs.Cart;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Controllers;

[ApiController]
[Route("api/cart")]
[Authorize(Policy = AccessPolicies.ManageOrders)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CartController(CartService cart) : ControllerBase
{
    [HttpGet("products")]
    public async Task<ActionResult<CartProductPageResponse>> ListProducts([FromQuery] CartProductQuery query, CancellationToken cancellationToken) =>
        await cart.ListProductsAsync(query, cancellationToken);

    [HttpPost("quote")]
    public async Task<ActionResult<CartQuoteResponse>> Quote(CartQuoteRequest request, CancellationToken cancellationToken) =>
        await cart.QuoteAsync(request, cancellationToken);
}
