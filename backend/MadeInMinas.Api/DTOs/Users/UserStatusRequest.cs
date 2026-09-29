using System.ComponentModel.DataAnnotations;

namespace MadeInMinas.Api.DTOs.Users;

public sealed record UserStatusRequest([Required] bool? IsActive);
