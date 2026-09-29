using System.ComponentModel.DataAnnotations;

namespace MadeInMinas.Api.DTOs.Ingredients;

public sealed record IngredientRequest(
    [Required, StringLength(120)] string Name,
    [Required, RegularExpression("^(kg|l|un)$")] string Unit,
    [Required] decimal? UnitCost,
    [Required] decimal? MinimumStock,
    [Required] bool? IsActive,
    [StringLength(150)] string? Supplier = null) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (UnitCost is not null && (UnitCost < 0 || UnitCost > 999999.9999m || decimal.Round(UnitCost.Value, 4) != UnitCost.Value))
            yield return new ValidationResult("Informe um custo de 0 a 999999,9999, com até quatro casas decimais.", [nameof(UnitCost)]);
        if (MinimumStock is not null && (MinimumStock < 0 || MinimumStock > 999999.999m || decimal.Round(MinimumStock.Value, 3) != MinimumStock.Value))
            yield return new ValidationResult("Informe um estoque mínimo de 0 a 999999,999, com até três casas decimais.", [nameof(MinimumStock)]);
    }
}

public sealed record IngredientStatusRequest([Required] bool? IsActive);

public sealed class IngredientListQuery
{
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    [StringLength(120)] public string? Search { get; init; }
    public bool? IsActive { get; init; }
}

public sealed record IngredientResponse(
    Guid Id, string Name, string Unit, decimal UnitCost, decimal MinimumStock, string? Supplier,
    bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record IngredientPageResponse(IngredientResponse[] Items, int Page, int PageSize, int TotalCount);
