using System.Data;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Cart;
using MadeInMinas.Api.DTOs.Dispatch;
using MadeInMinas.Api.Models;
using MadeInMinas.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Services;

public sealed class DispatchService(AppDbContext database, TimeProvider clock, ILogger<DispatchService> logger)
{
    public async Task<DispatchPageResponse> ListAsync(DispatchQuery request, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var query = database.Orders.AsNoTracking().Where(order => order.Status == request.Status);
        var count = await query.CountAsync(cancellationToken);
        var page = Math.Min(request.Page, Math.Max(1, (count + request.PageSize - 1) / request.PageSize));
        var orders = await query.OrderBy(order => order.UpdatedAt).ThenBy(order => order.Number)
            .Skip((page - 1) * request.PageSize).Take(request.PageSize)
            .Include(order => order.Items).Include(order => order.History).AsSingleQuery().ToArrayAsync(cancellationToken);
        var ids = orders.Select(order => order.Id).ToArray();
        var payments = await database.Payments.AsNoTracking().Where(payment => ids.Contains(payment.OrderId)
            && (payment.Status == "Pending" || payment.Status == "Received")).ToDictionaryAsync(payment => payment.OrderId, cancellationToken);
        var result = orders.Select(order => ToResponse(order, payments.GetValueOrDefault(order.Id))).ToArray();
        await transaction.CommitAsync(cancellationToken);
        return new DispatchPageResponse(UtcNow(), result, page, request.PageSize, count);
    }

    public async Task<DispatchOrderResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var result = await ReadAsync(id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<DispatchOrderResponse> SetStatusAsync(Guid actorId, Guid stamp, Guid id, DispatchStatusRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var actor = await database.Users.FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {actorId} FOR SHARE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (actor is null || !actor.IsActive || actor.SecurityStamp != stamp)
            throw new OrderException(OrderError.InvalidSession, "Sessão inválida. Faça login novamente.");
        var roles = AccessPolicies.RolesByPermission[AccessPolicies.WorkDispatch];
        if (!await database.Roles.AnyAsync(role => role.Id == actor.RoleId && roles.Contains(role.Code), cancellationToken))
            throw new OrderException(OrderError.PermissionDenied, "Acesso restrito ao administrador e expedição.");
        // A ordem de bloqueios coincide com cozinha, cancelamento e pagamentos.
        var order = await database.Orders.FromSqlInterpolated($"SELECT * FROM \"Orders\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw NotFound();
        var last = await database.OrderStatusHistory.Where(history => history.OrderId == id)
            .OrderByDescending(history => history.Version).FirstAsync(cancellationToken);
        if (order.Status == request.Status && request.ExpectedVersion == order.Version - 1 && last.ActorId == actorId)
        {
            var replay = await ReadAsync(id, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }
        if (order.Version != request.ExpectedVersion)
            throw new OrderException(OrderError.OrderVersionConflict, "O pedido mudou. Atualize a expedição.");
        var allowed = order.Fulfillment == "Delivery"
            ? (order.Status, request.Status) is ("Ready", "AwaitingDelivery") or ("AwaitingDelivery", "OutForDelivery") or ("OutForDelivery", "Delivered") or ("Delivered", "Finalized")
            : (order.Status, request.Status) is ("Ready", "Delivered") or ("Delivered", "Finalized");
        if (!allowed)
            throw new OrderException(OrderError.OrderTransitionDenied, "Esta etapa não é permitida para a modalidade e o status do pedido.");
        if (request.Status == "Finalized" && !await database.Payments.AnyAsync(payment => payment.OrderId == id
            && payment.Status == "Received" && payment.Amount == order.Total, cancellationToken))
            throw new OrderException(OrderError.OrderPaymentRequired, "Registre e confira o recebimento integral em Pagamentos antes de finalizar.");
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
        logger.LogInformation("Dispatch order {OrderId} changed from {FromStatus} to {ToStatus} by {ActorId}.", id, previous, order.Status, actorId);
        return result;
    }

    private async Task<DispatchOrderResponse> ReadAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await database.Orders.AsNoTracking().Include(order => order.Items).Include(order => order.History)
            .AsSingleQuery().SingleOrDefaultAsync(order => order.Id == id, cancellationToken) ?? throw NotFound();
        var payment = await database.Payments.AsNoTracking().SingleOrDefaultAsync(payment => payment.OrderId == id
            && (payment.Status == "Pending" || payment.Status == "Received"), cancellationToken);
        return ToResponse(order, payment);
    }
    private static DispatchOrderResponse ToResponse(Order order, Payment? payment) => new(order.Id, order.Number, order.Status,
        order.Version, order.Fulfillment, order.CustomerName, order.CustomerPhone,
        order.AddressId is null ? null : new CartAddressResponse(order.AddressId.Value, order.AddressStreet!, order.AddressNumber!,
            order.AddressNeighborhood!, order.AddressCity!, order.AddressState!, order.AddressComplement, order.AddressPostalCode, order.AddressReference),
        order.Items.OrderBy(item => item.Position).Select(item => new CartItemResponse(item.ProductId, item.ProductName, item.Quantity,
            item.UnitPrice, item.LineTotal, item.Notes)).ToArray(), order.Notes, order.Subtotal, order.DeliveryFee, order.Total,
        order.CreatedAt, order.UpdatedAt, order.History.Where(history => history.ToStatus == "Ready").Select(history => (DateTimeOffset?)history.OccurredAt).Min(),
        payment is null ? null : new DispatchPaymentResponse(payment.Method, payment.Status, payment.Amount, payment.CashTendered, payment.CashTendered - payment.Amount));
    private static OrderException NotFound() => new(OrderError.OrderNotFound, "Pedido não encontrado.");
    private DateTimeOffset UtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
}
