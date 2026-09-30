using System.ComponentModel.DataAnnotations;

namespace MadeInMinas.Api.DTOs.Recipes;

public sealed record RecipeItemRequest([Required] Guid? IngredientId, [Required] decimal? Quantity) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (IngredientId == Guid.Empty)
            yield return new ValidationResult("Selecione um ingrediente.", [nameof(IngredientId)]);
        if (Quantity is not null && (Quantity < 0.001m || Quantity > 999999.999m || decimal.Round(Quantity.Value, 3) != Quantity.Value))
            yield return new ValidationResult("Informe uma quantidade de 0,001 a 999999,999, com até três casas decimais.", [nameof(Quantity)]);
    }
}

public sealed record RecipeRequest(
    [Required, Range(1, 10000)] int? YieldQuantity,
    [Required, MinLength(1), MaxLength(100)] RecipeItemRequest?[]? Items,
    [StringLength(2000)] string? Instructions = null) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Items is null)
            yield break;
        if (Items.Any(item => item is null))
            yield return new ValidationResult("Todos os itens devem informar ingrediente e quantidade.", [nameof(Items)]);
        if (Items.Where(item => item?.IngredientId is not null).GroupBy(item => item!.IngredientId).Any(group => group.Count() > 1))
            yield return new ValidationResult("Não repita ingredientes na mesma ficha técnica.", [nameof(Items)]);
    }
}

public sealed record RecipeItemResponse(Guid IngredientId, string IngredientName, string Unit, bool IngredientIsActive, decimal Quantity);
public sealed record RecipeResponse(Guid Id, Guid ProductId, int YieldQuantity, string? Instructions,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, bool HasInactiveIngredients, RecipeItemResponse[] Items);
