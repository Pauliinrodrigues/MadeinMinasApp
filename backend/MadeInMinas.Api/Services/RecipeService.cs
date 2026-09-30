using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Recipes;
using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MadeInMinas.Api.Services;

public sealed class RecipeService(AppDbContext database, TimeProvider clock, ILogger<RecipeService> logger)
{
    public async Task<RecipeResponse> GetAsync(Guid productId, CancellationToken cancellationToken)
    {
        if (!await database.Products.AnyAsync(product => product.Id == productId, cancellationToken))
            throw ProductNotFound();
        var recipe = await database.Recipes.AsNoTracking().Include(recipe => recipe.Items).ThenInclude(item => item.Ingredient)
            .SingleOrDefaultAsync(recipe => recipe.ProductId == productId, cancellationToken)
            ?? throw new RecipeException(RecipeError.RecipeNotFound, "Este produto ainda não possui ficha técnica.");
        return ToResponse(recipe);
    }

    public async Task<(RecipeResponse Recipe, bool Created)> SaveAsync(Guid actorId, Guid actorStamp, Guid productId,
        RecipeRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginWriteAsync(actorId, actorStamp, cancellationToken);
        // Serializa inclusive a primeira criação da ficha, quando ainda não há linha em Recipes.
        var product = await database.Products.FromSqlInterpolated(
            $"SELECT * FROM \"Products\" WHERE \"Id\" = {productId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken)
            ?? throw ProductNotFound();
        var recipe = await database.Recipes.Include(recipe => recipe.Items)
            .SingleOrDefaultAsync(recipe => recipe.ProductId == productId, cancellationToken);
        var created = recipe is null;
        var existingIds = recipe?.Items.Select(item => item.IngredientId).ToHashSet() ?? [];
        var inputs = request.Items!.Select(item => item!).ToArray();
        var ingredients = new Dictionary<Guid, Ingredient>();
        // Ordem estável dos bloqueios evita inversões ao salvar fichas de produtos diferentes.
        foreach (var id in inputs.Select(item => item.IngredientId!.Value).Order())
        {
            var ingredient = await database.Ingredients.FromSqlInterpolated(
                $"SELECT * FROM \"Ingredients\" WHERE \"Id\" = {id} FOR SHARE").SingleOrDefaultAsync(cancellationToken)
                ?? throw new RecipeException(RecipeError.InvalidRecipeIngredient, "Um ingrediente informado não existe.");
            if (!ingredient.IsActive && !existingIds.Contains(id))
                throw new RecipeException(RecipeError.InactiveRecipeIngredient, "Novos vínculos exigem ingredientes ativos.");
            ingredients.Add(id, ingredient);
        }
        var now = DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
        if (recipe is null)
        {
            recipe = new Recipe { ProductId = product.Id, CreatedAt = now };
            database.Recipes.Add(recipe);
        }
        recipe.YieldQuantity = request.YieldQuantity!.Value;
        recipe.Instructions = string.IsNullOrWhiteSpace(request.Instructions) ? null : request.Instructions.Trim();
        recipe.UpdatedAt = now;
        foreach (var removed in recipe.Items.Where(item => !ingredients.ContainsKey(item.IngredientId)).ToArray())
        {
            recipe.Items.Remove(removed);
            database.RecipeItems.Remove(removed);
        }
        for (var position = 0; position < inputs.Length; position++)
        {
            var input = inputs[position];
            var id = input.IngredientId!.Value;
            var item = recipe.Items.SingleOrDefault(item => item.IngredientId == id);
            if (item is null)
            {
                item = new RecipeItem { RecipeId = recipe.Id, IngredientId = id, Ingredient = ingredients[id] };
                recipe.Items.Add(item);
            }
            item.Quantity = input.Quantity!.Value;
            item.Position = position;
        }
        await database.SaveChangesAsync(cancellationToken);
        var response = ToResponse(recipe);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Recipe {RecipeId} for product {ProductId} saved by {ActorId}.", recipe.Id, productId, actorId);
        return (response, created);
    }

    private async Task<IDbContextTransaction> BeginWriteAsync(Guid actorId, Guid actorStamp, CancellationToken cancellationToken) =>
        await CatalogWriteTransaction.BeginAsync(
            database, actorId, actorStamp,
            () => new RecipeException(RecipeError.InvalidSession, "Sessão inválida. Faça login novamente."),
            () => new RecipeException(RecipeError.PermissionDenied, "Acesso restrito ao administrador."),
            cancellationToken);

    private static RecipeException ProductNotFound() => new(RecipeError.ProductNotFound, "Produto não encontrado.");
    private static RecipeResponse ToResponse(Recipe recipe) => new(recipe.Id, recipe.ProductId, recipe.YieldQuantity,
        recipe.Instructions, recipe.CreatedAt, recipe.UpdatedAt, recipe.Items.Any(item => !item.Ingredient.IsActive),
        recipe.Items.OrderBy(item => item.Position).Select(item => new RecipeItemResponse(item.IngredientId,
            item.Ingredient.Name, item.Ingredient.Unit, item.Ingredient.IsActive, item.Quantity)).ToArray());
}
