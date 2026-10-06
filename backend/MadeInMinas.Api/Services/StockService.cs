using System.Data;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Stock;
using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Services;

public sealed class StockService(AppDbContext database, ILogger<StockService> logger)
{
    public async Task<StockReplenishmentResponse> ReplenishmentAsync(StockReplenishmentQuery request, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var query = database.Ingredients.AsNoTracking();
        if (!request.IncludeInactive)
            query = query.Where(ingredient => ingredient.IsActive);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().Normalize().ToUpperInvariant();
            query = query.Where(ingredient => ingredient.NormalizedName.Contains(search)
                || ingredient.Supplier != null && ingredient.Supplier.ToUpper().Contains(search));
        }
        // Os contadores mantêm busca e inclusão de inativos, independentemente da fila selecionada.
        var summary = await query.GroupBy(_ => 1).Select(group => new StockReplenishmentSummary(
            group.Count(),
            group.Count(ingredient => ingredient.CurrentStock <= ingredient.MinimumStock || ingredient.StockVersion == 0),
            group.Count(ingredient => ingredient.CurrentStock == 0),
            group.Count(ingredient => ingredient.CurrentStock <= ingredient.MinimumStock),
            group.Count(ingredient => ingredient.StockVersion == 0))).SingleOrDefaultAsync(cancellationToken)
            ?? new StockReplenishmentSummary(0, 0, 0, 0, 0);
        query = request.Status switch
        {
            "Attention" => query.Where(ingredient => ingredient.CurrentStock <= ingredient.MinimumStock || ingredient.StockVersion == 0),
            "OutOfStock" => query.Where(ingredient => ingredient.CurrentStock == 0),
            "LowStock" => query.Where(ingredient => ingredient.CurrentStock <= ingredient.MinimumStock),
            "Unrecorded" => query.Where(ingredient => ingredient.StockVersion == 0),
            _ => query
        };
        var count = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(ingredient => ingredient.CurrentStock == 0)
            .ThenByDescending(ingredient => ingredient.CurrentStock <= ingredient.MinimumStock)
            .ThenByDescending(ingredient => ingredient.StockVersion == 0)
            .ThenBy(ingredient => ingredient.NormalizedName).ThenBy(ingredient => ingredient.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(ingredient => new StockReplenishmentItem(ingredient.Id, ingredient.Name, ingredient.Unit,
                ingredient.Supplier, ingredient.IsActive, ingredient.CurrentStock, ingredient.MinimumStock,
                ingredient.CurrentStock < ingredient.MinimumStock ? ingredient.MinimumStock - ingredient.CurrentStock : 0m,
                ingredient.CurrentStock <= ingredient.MinimumStock, ingredient.StockVersion > 0))
            .ToArrayAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new StockReplenishmentResponse(summary, items, request.Page, request.PageSize, count);
    }

    public async Task<StockResponse> GetAsync(Guid ingredientId, StockQuery query, CancellationToken cancellationToken)
    {
        // Saldo e histórico pertencem à mesma visão, mesmo com lançamentos concorrentes.
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var ingredient = await database.Ingredients.AsNoTracking().SingleOrDefaultAsync(item => item.Id == ingredientId, cancellationToken)
            ?? throw new StockException(StockError.IngredientNotFound, "Ingrediente não encontrado.");
        var movements = database.StockMovements.AsNoTracking().Where(item => item.IngredientId == ingredientId);
        var count = await movements.CountAsync(cancellationToken);
        var items = await movements.OrderByDescending(item => item.Version).Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize).ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new StockResponse(ingredient.Id, ingredient.Name, ingredient.Unit, ingredient.IsActive,
            ingredient.CurrentStock, ingredient.MinimumStock, ingredient.CurrentStock <= ingredient.MinimumStock,
            ingredient.StockVersion, items.Select(ToResponse).ToArray(), query.Page, query.PageSize, count);
    }

    public async Task<StockMovementResponse> CreateAsync(Guid actorId, Guid actorStamp, Guid ingredientId,
        StockMovementRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await CatalogWriteTransaction.BeginAsync(database, actorId, actorStamp,
            () => new StockException(StockError.InvalidSession, "Sua sessão expirou. Entre novamente."),
            () => new StockException(StockError.PermissionDenied, "Somente administradores podem movimentar estoque."), cancellationToken);
        var ingredient = await database.Ingredients.FromSqlInterpolated(
                $"SELECT * FROM \"Ingredients\" WHERE \"Id\" = {ingredientId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new StockException(StockError.IngredientNotFound, "Ingrediente não encontrado.");
        var reason = request.Reason.Trim();
        var previous = await database.StockMovements.AsNoTracking().SingleOrDefaultAsync(
            item => item.IngredientId == ingredientId && item.RequestId == request.RequestId, cancellationToken);
        if (previous is not null)
        {
            if (previous.OrderId is not null || previous.ActorId != actorId || previous.Type != request.Type || previous.Quantity != request.Quantity ||
                previous.Reason != reason || previous.Version - 1 != request.ExpectedVersion)
                throw new StockException(StockError.StockRequestConflict, "Este identificador já foi utilizado em outro lançamento.");
            await transaction.CommitAsync(cancellationToken);
            return ToResponse(previous);
        }
        if (!ingredient.IsActive)
            throw new StockException(StockError.InactiveIngredient, "Reative o ingrediente antes de movimentar o estoque.");
        if (ingredient.StockVersion != request.ExpectedVersion)
            throw new StockException(StockError.StockVersionConflict, "O estoque mudou. Atualize o saldo e revise o lançamento.");
        var quantity = request.Quantity!.Value;
        var balance = request.Type switch
        {
            "Entry" => ingredient.CurrentStock + quantity,
            "Exit" => ingredient.CurrentStock - quantity,
            "Count" => quantity,
            _ => throw new ArgumentException("Tipo de movimentação inválido.", nameof(request))
        };
        if (balance < 0)
            throw new StockException(StockError.InsufficientStock, "A saída ultrapassa o saldo disponível.");
        if (balance > 999999.999m)
            throw new StockException(StockError.StockLimitExceeded, "O saldo máximo é 999999,999 na unidade-base.");
        if (balance == ingredient.CurrentStock)
            throw new StockException(StockError.StockUnchanged, "A contagem é igual ao saldo atual. Nenhum ajuste é necessário.");
        var actorName = await database.Users.Where(user => user.Id == actorId).Select(user => user.Name).SingleAsync(cancellationToken);
        var movement = new StockMovement
        {
            IngredientId = ingredientId,
            IngredientName = ingredient.Name,
            Unit = ingredient.Unit,
            ActorId = actorId,
            ActorName = actorName,
            RequestId = request.RequestId,
            Version = ingredient.StockVersion + 1,
            Type = request.Type,
            Quantity = quantity,
            PreviousBalance = ingredient.CurrentStock,
            Balance = balance,
            Delta = balance - ingredient.CurrentStock,
            Reason = reason,
            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        };
        ingredient.CurrentStock = balance;
        ingredient.StockVersion = movement.Version;
        database.StockMovements.Add(movement);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Stock movement {MovementId} registered for ingredient {IngredientId} by {ActorId}", movement.Id, ingredientId, actorId);
        return ToResponse(movement);
    }

    private static StockMovementResponse ToResponse(StockMovement item) => new(item.Id, item.RequestId, item.Version,
        item.Type, item.Quantity, item.Delta, item.PreviousBalance, item.Balance, item.Reason, item.ActorId,
        item.ActorName, item.IngredientName, item.Unit, item.CreatedAt)
    { OrderId = item.OrderId };
}
