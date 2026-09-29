namespace MadeInMinas.Api.DTOs.Auth;

public sealed record LoginResponse(
    string AccessToken, string TokenType, DateTimeOffset ExpiresAt, AuthenticatedUserResponse User);
