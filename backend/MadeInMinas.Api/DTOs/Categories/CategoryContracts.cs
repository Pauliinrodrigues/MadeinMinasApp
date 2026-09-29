using System.ComponentModel.DataAnnotations;

namespace MadeInMinas.Api.DTOs.Categories;

public sealed record CreateCategoryRequest(
    [Required, StringLength(80)] string Name,
    [StringLength(500)] string? Description = null,
    [Range(0, 9999)] int DisplayOrder = 0,
    bool IsActive = true);

public sealed record UpdateCategoryRequest(
    [Required, StringLength(80)] string Name,
    [StringLength(500)] string? Description,
    [Required, Range(0, 9999)] int? DisplayOrder,
    [Required] bool? IsActive);

public sealed record CategoryStatusRequest([Required] bool? IsActive);

public sealed class CategoryListQuery
{
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    [StringLength(80)] public string? Search { get; init; }
    public bool? IsActive { get; init; }
}

public sealed record CategoryResponse(
    Guid Id, string Name, string? Description, int DisplayOrder, bool IsActive,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record CategoryPageResponse(CategoryResponse[] Items, int Page, int PageSize, int TotalCount);

