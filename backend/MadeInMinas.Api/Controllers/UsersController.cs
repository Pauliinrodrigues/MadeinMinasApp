using System.Security.Claims;
using MadeInMinas.Api.DTOs.Users;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Policy = AccessPolicies.ManageUsers)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class UsersController(UserService users) : ControllerBase
{
    private Guid ActorId => Guid.Parse(User.FindFirstValue("sub")!);
    private Guid ActorStamp => Guid.Parse(User.FindFirstValue("auth_stamp")!);

    [HttpGet]
    public async Task<ActionResult<UserPageResponse>> List([FromQuery] UserListQuery query, CancellationToken cancellationToken) =>
        await users.ListAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await users.GetAsync(id, cancellationToken);

    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await users.CreateAsync(ActorId, ActorStamp, request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, user);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserResponse>> Update(Guid id, UpdateUserRequest request, CancellationToken cancellationToken) =>
        await users.UpdateAsync(ActorId, ActorStamp, id, request, cancellationToken);

    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<UserResponse>> SetStatus(Guid id, UserStatusRequest request, CancellationToken cancellationToken) =>
        await users.SetStatusAsync(ActorId, ActorStamp, id, request.IsActive!.Value, cancellationToken);

    [HttpPut("{id:guid}/password")]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await users.ResetPasswordAsync(ActorId, ActorStamp, id, request.NewPassword, cancellationToken);
        return NoContent();
    }
}
