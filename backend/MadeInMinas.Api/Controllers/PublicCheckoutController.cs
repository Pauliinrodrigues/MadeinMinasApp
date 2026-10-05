using MadeInMinas.Api.DTOs.PublicCheckout;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MadeInMinas.Api.Controllers;

[ApiController]
[Route("api/public-checkout")]
[AllowAnonymous]
[EnableRateLimiting("public-checkout")]
[RequestSizeLimit(65536)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PublicCheckoutController(PublicCheckoutService checkout) : ControllerBase
{
    [HttpPost("review")]
    public Task<PublicCheckoutReviewResponse> Review(PublicCheckoutRequest request, CancellationToken cancellationToken) =>
        checkout.ReviewAsync(request, cancellationToken);

    [HttpPost("orders")]
    public async Task<ActionResult<PublicOrderReceipt>> Create(PublicOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await checkout.CreateAsync(request, cancellationToken);
        return StatusCode(result.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK, result.Receipt);
    }
}
