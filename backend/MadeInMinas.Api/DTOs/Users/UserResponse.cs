namespace MadeInMinas.Api.DTOs.Users;

public sealed record UserResponse(
    Guid Id, string Name, string Username, int RoleId, string Role, bool IsActive, DateTimeOffset CreatedAt);
