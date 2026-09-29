using System.ComponentModel.DataAnnotations;

namespace MadeInMinas.Api.DTOs.Auth;

public sealed record ChangePasswordRequest(
    [Required, StringLength(128)] string CurrentPassword,
    [Required, StringLength(128, MinimumLength = 15)] string NewPassword);
