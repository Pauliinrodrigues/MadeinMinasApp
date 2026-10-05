using System.Security.Claims;
using MadeInMinas.Api.DTOs.Chat;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Controllers;

[ApiController, Route("api/chat"), Authorize(Policy = AccessPolicies.ManageChat)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ChatController(HumanChatService chat) : ControllerBase
{
    private Guid ActorId => Guid.Parse(User.FindFirstValue("sub")!);
    private Guid Stamp => Guid.Parse(User.FindFirstValue("auth_stamp")!);

    [HttpGet]
    public Task<ChatPageResponse> List([FromQuery] ChatListQuery query, CancellationToken cancellationToken) => chat.ListAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<StaffChatResponse> Read(Guid id, [FromQuery] ChatMessagesQuery query, CancellationToken cancellationToken) => chat.ReadStaffAsync(id, query.After, cancellationToken);

    [HttpPost("{id:guid}/messages")]
    public async Task<ActionResult<ChatMessageResponse>> Send(Guid id, SendChatMessageRequest request, CancellationToken cancellationToken)
    {
        var result = await chat.SendStaffAsync(ActorId, Stamp, id, request, cancellationToken);
        return StatusCode(result.Created ? 201 : 200, result.Message);
    }

    [HttpPut("{id:guid}/claim")]
    public Task<ChatSummaryResponse> Claim(Guid id, ChatActionRequest request, CancellationToken cancellationToken) => chat.ActAsync(ActorId, Stamp, id, request, false, cancellationToken);

    [HttpPut("{id:guid}/close")]
    public Task<ChatSummaryResponse> Close(Guid id, ChatActionRequest request, CancellationToken cancellationToken) => chat.ActAsync(ActorId, Stamp, id, request, true, cancellationToken);
}
