using System.ComponentModel.DataAnnotations;
using MadeInMinas.Api.Services;

namespace MadeInMinas.Api.DTOs.Products;

public sealed record ProductRequest(
    [Required, StringLength(120)] string Name,
    [Required] Guid? CategoryId,
    [Required] decimal? Price,
    [Required] bool? IsActive,
    [Required] bool? IsAvailable,
    [StringLength(1000)] string? Description = null,
    [StringLength(2048)] string? ImageUrl = null) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CategoryId == Guid.Empty)
            yield return new ValidationResult("Selecione uma categoria.", [nameof(CategoryId)]);
        if (Price is not null && (Price < 0.01m || Price > 999999.99m))
            yield return new ValidationResult("O preço deve estar entre R$ 0,01 e R$ 999.999,99.", [nameof(Price)]);
        if (Price is not null && decimal.Round(Price.Value, 2) != Price.Value)
            yield return new ValidationResult("O preço deve ter até duas casas decimais.", [nameof(Price)]);
        if (!string.IsNullOrWhiteSpace(ImageUrl) &&
            !ProductImageStorage.IsManagedUrl(ImageUrl.Trim()) &&
            (!Uri.TryCreate(ImageUrl.Trim(), UriKind.Absolute, out var uri) ||
             uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)))
            yield return new ValidationResult("Envie uma foto ou informe uma URL HTTPS de imagem, sem credenciais.", [nameof(ImageUrl)]);
    }
}

public sealed record ProductStatusRequest([Required] bool? IsActive);
public sealed record ProductAvailabilityRequest([Required] bool? IsAvailable);

public sealed class ProductListQuery
{
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    [StringLength(120)]
    public string? Search { get; init; }
    public Guid? CategoryId { get; init; }
    public bool? IsActive { get; init; }
    public bool? IsAvailableForSale { get; init; }
}

public sealed record ProductResponse(
    Guid Id, Guid CategoryId, string CategoryName, bool CategoryIsActive, string Name, string? Description,
    decimal Price, string? ImageUrl, bool IsActive, bool IsAvailable, bool IsAvailableForSale,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public bool? HasRecipe { get; init; }
    public bool? HasMissingCosts { get; init; }
}
public sealed record ProductPageResponse(ProductResponse[] Items, int Page, int PageSize, int TotalCount);
