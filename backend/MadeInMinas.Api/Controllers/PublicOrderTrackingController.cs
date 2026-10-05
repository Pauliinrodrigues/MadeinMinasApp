using MadeInMinas.Api.DTOs.PublicOrders;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MadeInMinas.Api.Controllers;

[ApiController]
[Route("api/public-orders/tracking")]
[AllowAnonymous]
[EnableRateLimiting("public-tracking")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PublicOrderTrackingController(PublicOrderTrackingService tracking) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PublicOrderTrackingResponse>> Get(CancellationToken cancellationToken)
    {
        var headers = Request.Headers["X-Order-Access"];
        var result = await tracking.GetAsync(headers.Count == 1 ? headers[0] : null, cancellationToken);
        return result is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: "Acompanhamento indisponível.",
                detail: "O acesso não está disponível ou expirou. Procure o atendimento.")
            : Ok(result);
    }
}
