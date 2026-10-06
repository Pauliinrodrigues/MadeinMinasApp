using System.ComponentModel.DataAnnotations;

namespace MadeInMinas.Api.DTOs.Menu;

public sealed class PublicMenuQuery
{
    [StringLength(120)] public string? Search { get; init; }
    public Guid? CategoryId { get; init; }
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 48)] public int PageSize { get; init; } = 24;
}

public sealed record MenuCategoryResponse(Guid Id, string Name);
public sealed record MenuProductResponse(
    Guid Id, Guid CategoryId, string Name, string? Description, decimal Price, string? ImageUrl, bool IsAvailable);
public sealed record PublicMenuResponse(
    MenuCategoryResponse[] Categories, MenuProductResponse[] Items, int Page, int PageSize, int TotalCount);
