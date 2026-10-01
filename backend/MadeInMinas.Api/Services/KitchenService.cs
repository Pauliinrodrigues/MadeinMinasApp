using System.Data;
using System.Linq.Expressions;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Kitchen;
using MadeInMinas.Api.Models;
using MadeInMinas.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Services;

public sealed class KitchenService(AppDbContext database, TimeProvider clock, ILogger<KitchenService> logger)
{
    public async Task<KitchenOrderResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await database.Orders.AsNoTracking().Where(order => order.Id == id).Select(Projection).SingleOrDefaultAsync(cancellationToken)
        ?? throw new OrderException(OrderError.OrderNotFound, "Pedido não encontrado.");

    // A projeção limita o contrato aos dados de produção; não envia dados financeiros ou do cliente.
    private static readonly Expression<Func<Order, KitchenOrderResponse>> Projection = order => new(
        order.Id, order.Number, order.Fulfillment, order.Status, order.Version, order.Notes, order.CreatedAt,
        order.History.Where(history => history.ToStatus == "Confirmed").Select(history => (DateTimeOffset?)history.OccurredAt).Min(),
        order.History.Where(history => history.ToStatus == "InPreparation").Select(history => (DateTimeOffset?)history.OccurredAt).Min(),
        order.History.Where(history => history.ToStatus == "Ready").Select(history => (DateTimeOffset?)history.OccurredAt).Min(),
        order.Items.OrderBy(item => item.Position).Select(item => new KitchenItemResponse(item.Position, item.ProductName, item.Quantity, item.Notes)).ToArray());

    public async Task<KitchenBoardResponse> BoardAsync(KitchenBoardQuery request, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var columns = new List<KitchenColumnResponse>();
        foreach (var (status, requestedPage) in new[] { ("Confirmed", request.ConfirmedPage), ("InPreparation", request.PreparingPage), ("Ready", request.ReadyPage) })
        {
            var query = database.Orders.AsNoTracking().Where(order => order.Status == status);
            var count = await query.CountAsync(cancellationToken);
            var page = Math.Min(requestedPage, Math.Max(1, (count + request.PageSize - 1) / request.PageSize));
            var items = await query.OrderBy(order => order.History.Where(history => history.ToStatus == "Confirmed")
                    .Select(history => (DateTimeOffset?)history.OccurredAt).Min()).ThenBy(order => order.Number)
                .Skip((page - 1) * request.PageSize).Take(request.PageSize).Select(Projection).ToArrayAsync(cancellationToken);
            columns.Add(new KitchenColumnResponse(status, items, page, request.PageSize, count));
        }
        await transaction.CommitAsync(cancellationToken);
        return new KitchenBoardResponse(UtcNow(), columns.ToArray());
    }

    public async Task<KitchenOrderResponse> SetStatusAsync(Guid actorId, Guid stamp, Guid id, KitchenStatusRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var actor = await database.Users.FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {actorId} FOR SHARE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (actor is null || !actor.IsActive || actor.SecurityStamp != stamp)
            throw new OrderException(OrderError.InvalidSession, "Sessão inválida. Faça login novamente.");
        var roles = AccessPolicies.RolesByPermission[AccessPolicies.WorkKitchen];
        if (!await database.Roles.AnyAsync(role => role.Id == actor.RoleId && roles.Contains(role.Code), cancellationToken))
            throw new OrderException(OrderError.PermissionDenied, "Acesso restrito ao administrador e cozinha.");
        // Mesmo bloqueio usado por cancelamento e pagamentos, antes de decidir a transição.
        var order = await database.Orders.FromSqlInterpolated($"SELECT * FROM \"Orders\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw new OrderException(OrderError.OrderNotFound, "Pedido não encontrado.");
        var last = await database.OrderStatusHistory.Where(history => history.OrderId == id)
            .OrderByDescending(history => history.Version).FirstAsync(cancellationToken);
        if (order.Status == request.Status && request.ExpectedVersion == order.Version - 1 && last.ActorId == actorId)
        {
            var existing = await ReadAsync(id, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return existing;
        }
        if (order.Version != request.ExpectedVersion)
            throw new OrderException(OrderError.OrderVersionConflict, "O pedido mudou. Atualize o painel antes de continuar.");
        if (!(order.Status == "Confirmed" && request.Status == "InPreparation" || order.Status == "InPreparation" && request.Status == "Ready"))
            throw new OrderException(OrderError.OrderTransitionDenied, "A cozinha avança somente de Confirmado para Em preparação e depois Pronto.");
        var previous = order.Status;
        order.Status = request.Status;
        order.Version++;
        order.UpdatedAt = UtcNow();
        database.OrderStatusHistory.Add(new OrderStatusHistory
        {
            OrderId = id,
            Version = order.Version,
            FromStatus = previous,
            ToStatus = order.Status,
            ActorId = actor.Id,
            ActorName = actor.Name,
            OccurredAt = order.UpdatedAt
        });
        await database.SaveChangesAsync(cancellationToken);
        var result = await ReadAsync(id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Kitchen order {OrderId} changed from {FromStatus} to {ToStatus} by {ActorId}.", id, previous, order.Status, actorId);
        return result;
    }

    private Task<KitchenOrderResponse> ReadAsync(Guid id, CancellationToken cancellationToken) =>
        database.Orders.AsNoTracking().Where(order => order.Id == id).Select(Projection).SingleAsync(cancellationToken);
    private DateTimeOffset UtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
}
