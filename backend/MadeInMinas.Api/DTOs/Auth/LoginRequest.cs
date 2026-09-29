using System.ComponentModel.DataAnnotations;

namespace MadeInMinas.Api.DTOs.Auth;

public sealed record LoginRequest(
    [Required, StringLength(64, MinimumLength = 3),
     RegularExpression(@"[a-zA-Z0-9._-]+")] string Username,
    [Required, StringLength(128)] string Password);
