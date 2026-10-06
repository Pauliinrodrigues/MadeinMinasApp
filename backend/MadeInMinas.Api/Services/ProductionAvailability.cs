using MadeInMinas.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Services;

// Consulta sem reserva. A confirmação continua fazendo a baixa sob bloqueio transacional.
internal sealed class ProductionAvailability
{
    private sealed record Component(Guid IngredientId, decimal Quantity, int Yield, bool Active, decimal Stock);
    private readonly Dictionary<Guid, Component[]> recipes;

    private ProductionAvailability(Dictionary<Guid, Component[]> recipes) => this.recipes = recipes;

    public static async Task<ProductionAvailability> ReadAsync(AppDbContext database, Guid[] productIds, CancellationToken cancellationToken)
    {
        var data = await database.Recipes.AsNoTracking().Where(recipe => productIds.Contains(recipe.ProductId))
            .Select(recipe => new
            {
                recipe.ProductId,
                Items = recipe.Items.Select(item => new Component(item.IngredientId, item.Quantity,
                    recipe.YieldQuantity, item.Ingredient.IsActive, item.Ingredient.CurrentStock)).ToArray()
            }).ToArrayAsync(cancellationToken);
        return new ProductionAvailability(data.ToDictionary(recipe => recipe.ProductId, recipe => recipe.Items));
    }

    public bool CanProduce(IEnumerable<(Guid ProductId, int Quantity)> lines)
    {
        var totals = new Dictionary<Guid, (decimal Required, decimal Stock)>();
        foreach (var product in lines.GroupBy(line => line.ProductId))
        {
            if (!recipes.TryGetValue(product.Key, out var components) || components.Length == 0)
                return false;
            var quantity = product.Sum(line => line.Quantity);
            foreach (var component in components)
            {
                if (!component.Active)
                    return false;
                // Mesma precisão da baixa: arredondar para cima por produto/ingrediente.
                var consumed = decimal.Ceiling(component.Quantity * 1000m * quantity / component.Yield) / 1000m;
                var total = totals.GetValueOrDefault(component.IngredientId).Required + consumed;
                if (total > 999999.999m || total > component.Stock)
                    return false;
                totals[component.IngredientId] = (total, component.Stock);
            }
        }
        return true;
    }
}
