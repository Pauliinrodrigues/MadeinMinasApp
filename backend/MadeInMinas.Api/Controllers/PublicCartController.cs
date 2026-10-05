using MadeInMinas.Api.DTOs.PublicCart;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Controllers;

[ApiController]
[Route("api/public-cart")]
[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PublicCartController(PublicCartService cart) : ControllerBase
{
    [HttpPost("quote")]
    [RequestSizeLimit(65536)]
    public async Task<ActionResult<PublicCartQuoteResponse>> Quote(PublicCartQuoteRequest request, CancellationToken cancellationToken) =>
        await cart.QuoteAsync(request, cancellationToken);
}
