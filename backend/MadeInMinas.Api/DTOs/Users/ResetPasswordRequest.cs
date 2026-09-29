using System.ComponentModel.DataAnnotations;

namespace MadeInMinas.Api.DTOs.Users;

public sealed record ResetPasswordRequest(
    [Required, StringLength(128, MinimumLength = 15)] string NewPassword);
