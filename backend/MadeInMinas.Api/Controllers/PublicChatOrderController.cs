using MadeInMinas.Api.DTOs.ChatOrders;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MadeInMinas.Api.Controllers;

[ApiController, Route("api/public-chat/order"), AllowAnonymous]
[EnableRateLimiting("chat-send"), RequestSizeLimit(16384)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PublicChatOrderController(ChatOrderService orders) : ControllerBase
{
    private string? Access => Request.Headers["X-Chat-Access"].Count == 1 ? Request.Headers["X-Chat-Access"][0] : null;

    [HttpPost("review")]
    public Task<ChatOrderStateResponse> Review(ChatOrderReviewRequest request, CancellationToken cancellationToken) =>
        orders.ReviewPublicAsync(Access, request, cancellationToken);

    [HttpPost("confirm")]
    public Task<ChatOrderStateResponse> Confirm(ChatOrderConfirmationRequest request, CancellationToken cancellationToken) =>
        orders.ConfirmPublicAsync(Access, request, cancellationToken);
}
