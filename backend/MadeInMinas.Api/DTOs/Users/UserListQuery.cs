using System.ComponentModel.DataAnnotations;

namespace MadeInMinas.Api.DTOs.Users;

public sealed class UserListQuery
{
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    [StringLength(120)] public string? Search { get; init; }
    [Range(1, int.MaxValue)] public int? RoleId { get; init; }
    public bool? IsActive { get; init; }
}
