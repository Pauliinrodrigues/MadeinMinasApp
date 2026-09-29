using System.Security.Claims;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MadeInMinas.Api.Controllers;

[ApiController]
[Route("api/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthController(AuthenticationService authentication) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var response = await authentication.LoginAsync(request, cancellationToken);
        return response is null
            ? Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Login ou senha invalidos.")
            : Ok(response);
    }

    [HttpGet("me")]
    [ProducesResponseType<AuthenticatedUserResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthenticatedUserResponse>> Me(CancellationToken cancellationToken)
    {
        var user = await authentication.GetCurrentUserAsync(Guid.Parse(User.FindFirstValue("sub")!), cancellationToken);
        return user is null ? Unauthorized() : Ok(user);
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await authentication.LogoutAsync(Guid.Parse(User.FindFirstValue("sub")!), cancellationToken);
        return NoContent();
    }

    [HttpPut("password")]
    [EnableRateLimiting("login")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordRequest request, [FromServices] UserService users, CancellationToken cancellationToken)
    {
        await users.ChangePasswordAsync(
            Guid.Parse(User.FindFirstValue("sub")!),
            Guid.Parse(User.FindFirstValue("auth_stamp")!),
            request, cancellationToken);
        return NoContent();
    }
}
