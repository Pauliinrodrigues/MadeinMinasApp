namespace MadeInMinas.Api.DTOs.Auth;

public sealed record AuthenticatedUserResponse(
    Guid Id, string Name, string Username, string Role, string[] Permissions);
