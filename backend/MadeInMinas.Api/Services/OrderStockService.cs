using MadeInMinas.Api.Data;
using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Services;

// Participa da transação do pedido; o chamador já bloqueou o pedido e os produtos.
public sealed class OrderStockService(AppDbContext database)
{
    public async Task ConsumeAsync(Order order, User actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var quantities = order.Items.GroupBy(item => item.ProductId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));
        var productIds = quantities.Keys.ToArray();
        var recipes = await database.Recipes.AsNoTracking().Include(recipe => recipe.Items)
            .Where(recipe => productIds.Contains(recipe.ProductId)).ToArrayAsync(cancellationToken);
        if (recipes.Length != productIds.Length || recipes.Any(recipe => recipe.Items.Count == 0))
            throw new OrderException(OrderError.OrderRecipeRequired, "Cadastre a ficha técnica de todos os produtos antes de confirmar o pedido.");
        var ingredientIds = recipes.SelectMany(recipe => recipe.Items).Select(item => item.IngredientId).Distinct().ToArray();
        var ingredients = await LockIngredientsAsync(ingredientIds, cancellationToken);
        if (ingredients.Values.Any(ingredient => !ingredient.IsActive))
            throw new OrderException(OrderError.OrderIngredientInactive, "A ficha contém ingrediente inativo. Solicite a revisão do cadastro.");
        var components = new List<OrderStockComponent>();
        foreach (var recipe in recipes)
        {
            foreach (var item in recipe.Items)
            {
                // Arredonda uma vez por produto/ingrediente, após somar todas as linhas do produto.
                var consumed = decimal.Ceiling(item.Quantity * 1000m * quantities[recipe.ProductId] / recipe.YieldQuantity) / 1000m;
                if (consumed > 999999.999m)
                    throw QuantityExceeded();
                var ingredient = ingredients[item.IngredientId];
                components.Add(new OrderStockComponent
                {
                    OrderId = order.Id,
                    ProductId = recipe.ProductId,
                    ProductName = order.Items.First(line => line.ProductId == recipe.ProductId).ProductName,
                    IngredientId = ingredient.Id,
                    IngredientName = ingredient.Name,
                    Unit = ingredient.Unit,
                    ProductQuantity = quantities[recipe.ProductId],
                    RecipeYield = recipe.YieldQuantity,
                    RecipeQuantity = item.Quantity,
                    ConsumedQuantity = consumed
                });
            }
        }
        var totals = components.GroupBy(item => item.IngredientId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.ConsumedQuantity));
        foreach (var (id, quantity) in totals)
        {
            if (quantity > 999999.999m)
                throw QuantityExceeded();
            if (ingredients[id].CurrentStock < quantity)
                throw new OrderException(OrderError.OrderInsufficientStock, "Saldo insuficiente para os ingredientes deste pedido. Solicite a conferência do estoque.");
        }
        order.StockComponents.AddRange(components);
        foreach (var (id, quantity) in totals)
            AddMovement(order, ingredients[id], actor, -quantity, now, $"Baixa do pedido #{order.Number}");
        order.StockStatus = "Consumed";
    }

    public async Task CancelAsync(Order order, User actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        RequireTransaction();
        if (order.StockStatus != "Consumed")
        {
            if (order.Status == "New")
                order.StockStatus = "NotRequired";
            return;
        }
        if (order.Status != "Confirmed" || order.History.Any(item => item.ToStatus == "InPreparation"))
        {
            order.StockStatus = "Retained";
            return;
        }
        // A devolução usa a baixa efetiva, nunca a ficha atual nem um novo arredondamento.
        var consumed = await database.StockMovements.AsNoTracking()
            .Where(item => item.OrderId == order.Id && item.Type == "Exit").ToArrayAsync(cancellationToken);
        if (consumed.Length == 0)
            throw new InvalidOperationException("Pedido com consumo sem movimentos de estoque.");
        var ingredients = await LockIngredientsAsync(consumed.Select(item => item.IngredientId).ToArray(), cancellationToken);
        if (consumed.Any(item => ingredients[item.IngredientId].CurrentStock + item.Quantity > 999999.999m))
            throw new OrderException(OrderError.OrderStockReturnOverflow, "A devolução ultrapassa o limite do estoque. Solicite uma conferência antes de cancelar.");
        foreach (var item in consumed)
            AddMovement(order, ingredients[item.IngredientId], actor, item.Quantity, now, $"Devolução do pedido #{order.Number}, cancelado antes do preparo");
        order.StockStatus = "Returned";
    }

    private async Task<Dictionary<Guid, Ingredient>> LockIngredientsAsync(Guid[] ids, CancellationToken cancellationToken) =>
        await database.Ingredients.FromSqlInterpolated(
            $"SELECT * FROM \"Ingredients\" WHERE \"Id\" = ANY ({ids}) ORDER BY \"Id\" FOR UPDATE")
            .ToDictionaryAsync(item => item.Id, cancellationToken);

    private void AddMovement(Order order, Ingredient ingredient, User actor, decimal delta, DateTimeOffset now, string reason)
    {
        database.StockMovements.Add(new StockMovement
        {
            OrderId = order.Id,
            IngredientId = ingredient.Id,
            IngredientName = ingredient.Name,
            Unit = ingredient.Unit,
            ActorId = actor.Id,
            ActorName = actor.Name,
            RequestId = Guid.NewGuid(),
            Version = ingredient.StockVersion + 1,
            Type = delta < 0 ? "Exit" : "Entry",
            Quantity = Math.Abs(delta),
            Delta = delta,
            PreviousBalance = ingredient.CurrentStock,
            Balance = ingredient.CurrentStock + delta,
            Reason = reason,
            CreatedAt = now
        });
        ingredient.CurrentStock += delta;
        ingredient.StockVersion++;
    }

    private void RequireTransaction()
    {
        if (database.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Movimentação por pedido exige a transação do pedido.");
    }

    private static OrderException QuantityExceeded() => new(OrderError.OrderStockQuantityExceeded,
        "O consumo calculado ultrapassa o limite de movimentação. Revise as quantidades e as fichas técnicas.");
}
