using System.ComponentModel.DataAnnotations;

namespace MadeInMinas.Api.DTOs.Users;

public sealed record CreateUserRequest(
    [Required, StringLength(120)] string Name,
    [Required, StringLength(64, MinimumLength = 3), RegularExpression(@"[a-zA-Z0-9._-]+")] string Username,
    [Required, StringLength(128, MinimumLength = 15)] string Password,
    [Range(1, int.MaxValue)] int RoleId,
    bool IsActive = true);
