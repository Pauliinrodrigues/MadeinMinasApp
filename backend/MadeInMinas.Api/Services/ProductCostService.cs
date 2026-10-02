using System.Data;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Costing;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Services;

public sealed class ProductCostService(AppDbContext database, TimeProvider clock)
{
    public async Task<ProductCostResponse> GetAsync(Guid productId, CancellationToken cancellationToken)
    {
        // Preço, composição e custos devem pertencer à mesma visão do banco.
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var product = await database.Products.AsNoTracking().Include(item => item.Category)
            .SingleOrDefaultAsync(item => item.Id == productId, cancellationToken)
            ?? throw new ProductException(ProductError.ProductNotFound, "Produto não encontrado.");
        var recipe = await database.Recipes.AsNoTracking().Include(item => item.Items).ThenInclude(item => item.Ingredient)
            .SingleOrDefaultAsync(item => item.ProductId == productId, cancellationToken);
        var items = recipe?.Items.OrderBy(item => item.Position).Select(item => new ProductCostItemResponse(
            item.IngredientId, item.Ingredient.Name, item.Ingredient.Unit, item.Ingredient.IsActive,
            item.Quantity, item.Ingredient.UnitCost, item.Quantity * item.Ingredient.UnitCost)).ToArray() ?? [];
        var status = items.Length == 0 ? "MissingRecipe" : items.Any(item => item.UnitCost == 0) ? "MissingCosts" : "Ready";
        var knownCost = items.Sum(item => item.RecipeCost);
        decimal? recipeCost = status == "Ready" ? knownCost : null;
        decimal? unitCost = recipeCost / recipe?.YieldQuantity;
        // Custos não são arredondados antes de calcular os indicadores.
        decimal? cmv = unitCost is { } cost ? decimal.Round(cost / product.Price * 100m, 2, MidpointRounding.AwayFromZero) : null;
        var response = new ProductCostResponse(product.Id, product.Name, product.IsActive,
            product.IsActive && product.IsAvailable && product.Category.IsActive, product.Price, status,
            recipe?.YieldQuantity, recipe?.UpdatedAt, items.Any(item => !item.IngredientIsActive), knownCost,
            recipeCost, unitCost, cmv, product.Price - unitCost, 100m - cmv, items,
            DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds()));
        await transaction.CommitAsync(cancellationToken);
        return response;
    }
}
