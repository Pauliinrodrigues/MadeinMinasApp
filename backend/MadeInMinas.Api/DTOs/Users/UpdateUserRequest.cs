using System.ComponentModel.DataAnnotations;

namespace MadeInMinas.Api.DTOs.Users;

public sealed record UpdateUserRequest(
    [Required, StringLength(120)] string Name,
    [Required, StringLength(64, MinimumLength = 3), RegularExpression(@"[a-zA-Z0-9._-]+")] string Username,
    [Range(1, int.MaxValue)] int RoleId,
    [Required] bool? IsActive);
