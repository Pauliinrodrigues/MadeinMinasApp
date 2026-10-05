using MadeInMinas.Api.DTOs.Chat;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MadeInMinas.Api.Controllers;

[ApiController, Route("api/public-chat"), AllowAnonymous]
[RequestSizeLimit(16384), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PublicChatController(HumanChatService chat) : ControllerBase
{
    private string? Access => Request.Headers["X-Chat-Access"].Count == 1 ? Request.Headers["X-Chat-Access"][0] : null;

    [HttpPost, EnableRateLimiting("chat-start")]
    public async Task<ActionResult<StartChatResponse>> Start(StartChatRequest request, CancellationToken cancellationToken)
    {
        var result = await chat.StartAsync(request, cancellationToken);
        return StatusCode(result.Created ? 201 : 200, result.Response);
    }

    [HttpGet, EnableRateLimiting("chat-read")]
    public Task<ChatTranscriptResponse> Read([FromQuery] ChatMessagesQuery query, CancellationToken cancellationToken) =>
        chat.ReadPublicAsync(Access, query.After, cancellationToken);

    [HttpPost("messages"), EnableRateLimiting("chat-send")]
    public async Task<ActionResult<ChatMessageResponse>> Send(SendChatMessageRequest request, CancellationToken cancellationToken)
    {
        var result = await chat.SendPublicAsync(Access, request, cancellationToken);
        return StatusCode(result.Created ? 201 : 200, result.Message);
    }
}
