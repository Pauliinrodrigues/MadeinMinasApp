namespace MadeInMinas.Api.DTOs.Costing;

public sealed record ProductCostItemResponse(Guid IngredientId, string IngredientName, string Unit,
    bool IngredientIsActive, decimal Quantity, decimal UnitCost, decimal RecipeCost);

public sealed record ProductCostResponse(Guid ProductId, string ProductName, bool ProductIsActive,
    bool IsAvailableForSale, decimal SalePrice, string Status, int? RecipeYield,
    DateTimeOffset? RecipeUpdatedAt, bool HasInactiveIngredients, decimal KnownRecipeCost,
    decimal? RecipeCost, decimal? UnitCost, decimal? CmvPercentage, decimal? GrossMargin,
    decimal? GrossMarginPercentage, ProductCostItemResponse[] Items, DateTimeOffset CalculatedAt);
