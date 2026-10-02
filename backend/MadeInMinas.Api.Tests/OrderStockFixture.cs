using MadeInMinas.Api.Data;
using MadeInMinas.Api.Models;

namespace MadeInMinas.Api.Tests;

internal static class OrderStockFixture
{
    public static void AddRecipe(AppDbContext database, Product product)
    {
        var name = "Insumo de teste " + Guid.NewGuid().ToString("N");
        var ingredient = new Ingredient { Name = name, NormalizedName = name.ToUpperInvariant(), Unit = "un", CurrentStock = 10000m };
        database.Ingredients.Add(ingredient);
        database.Recipes.Add(new Recipe
        {
            ProductId = product.Id,
            YieldQuantity = 1,
            Items = [new RecipeItem { IngredientId = ingredient.Id, Quantity = 1m }]
        });
    }
}
