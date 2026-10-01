using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Payments;
using MadeInMinas.Api.Models;
using MadeInMinas.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Services;

public sealed class PaymentService(AppDbContext database, TimeProvider clock, ILogger<PaymentService> logger)
{
    public async Task<PaymentPageResponse> ListAsync(Guid orderId, PaymentListQuery request, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var order = await database.Orders.AsNoTracking().SingleOrDefaultAsync(order => order.Id == orderId, cancellationToken) ?? throw OrderNotFound();
        var query = ReadPayments().Where(payment => payment.OrderId == orderId);
        var active = await query.SingleOrDefaultAsync(payment => payment.Status == "Pending" || payment.Status == "Received", cancellationToken);
        var count = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(payment => payment.CreatedAt).ThenByDescending(payment => payment.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToArrayAsync(cancellationToken);
        var received = active?.Status == "Received" ? active.Amount : 0;
        await transaction.CommitAsync(cancellationToken);
        return new PaymentPageResponse(order.Id, order.Number, order.Status, order.Version, order.Total, received,
            order.Status == "Cancelled" ? 0 : order.Total - received, active is null ? null : ToResponse(active),
            items.Select(ToResponse).ToArray(), request.Page, request.PageSize, count);
    }

    public async Task<PaymentResponse> GetAsync(Guid orderId, Guid id, CancellationToken cancellationToken) =>
        ToResponse(await ReadPayments().SingleOrDefaultAsync(payment => payment.OrderId == orderId && payment.Id == id, cancellationToken)
            ?? throw new PaymentException(PaymentError.PaymentNotFound, "Pagamento não encontrado para este pedido."));

    public async Task<PaymentCreationResult> CreateAsync(Guid actorId, Guid actorStamp, Guid orderId, CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var actor = await RequireActorAsync(actorId, actorStamp, AccessPolicies.ManagePayments, cancellationToken);
        var lockKey = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"payment:{actorId}:{request.RequestId}")));
        await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { orderId, request.Method, request.ExpectedOrderVersion })));
        var existing = await ReadPayments().SingleOrDefaultAsync(payment => payment.CreatedById == actorId && payment.RequestId == request.RequestId, cancellationToken);
        if (existing is not null)
        {
            if (existing.RequestHash != hash)
                throw new PaymentException(PaymentError.PaymentRequestConflict, "Esta tentativa já foi registrada com outro conteúdo. Confira o histórico antes de continuar.");
            await transaction.CommitAsync(cancellationToken);
            return new PaymentCreationResult(ToResponse(existing), false);
        }
        var order = await LockOrderAsync(orderId, cancellationToken);
        RequireActiveOrder(order);
        if (order.Version != request.ExpectedOrderVersion)
            throw new PaymentException(PaymentError.PaymentOrderChanged, "O pedido mudou. Atualize os dados antes de definir o pagamento.");
        if (await database.Payments.AnyAsync(payment => payment.OrderId == orderId && (payment.Status == "Pending" || payment.Status == "Received"), cancellationToken))
            throw new PaymentException(PaymentError.PaymentAlreadyActive, "Este pedido já possui pagamento pendente ou recebido. Confira o pagamento atual.");
        var now = UtcNow();
        var payment = new Payment
        {
            OrderId = order.Id,
            RequestId = request.RequestId!.Value,
            RequestHash = hash,
            CreatedById = actor.Id,
            Method = request.Method,
            Amount = order.Total,
            CreatedAt = now,
            UpdatedAt = now,
            History = [new PaymentStatusHistory { Version = 1, ToStatus = "Pending", ActorId = actor.Id, ActorName = actor.Name, OccurredAt = now }]
        };
        database.Payments.Add(payment);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Payment {PaymentId} created for order {OrderId} by {ActorId}.", payment.Id, orderId, actorId);
        return new PaymentCreationResult(ToResponse(payment), true);
    }

    public Task<PaymentResponse> ReceiveAsync(Guid actorId, Guid actorStamp, Guid orderId, Guid id, ReceivePaymentRequest request, CancellationToken cancellationToken) =>
        TransitionAsync(actorId, actorStamp, orderId, id, "Received", request.ExpectedVersion!.Value, null, request.CashTendered, cancellationToken);

    public Task<PaymentResponse> CancelAsync(Guid actorId, Guid actorStamp, Guid orderId, Guid id, CancelPaymentRequest request, CancellationToken cancellationToken) =>
        TransitionAsync(actorId, actorStamp, orderId, id, "Cancelled", request.ExpectedVersion!.Value, request.Reason, null, cancellationToken);

    public Task<PaymentResponse> RefundAsync(Guid actorId, Guid actorStamp, Guid orderId, Guid id, RefundPaymentRequest request, CancellationToken cancellationToken) =>
        TransitionAsync(actorId, actorStamp, orderId, id, "Refunded", request.ExpectedVersion!.Value, request.Reason, null, cancellationToken);

    private async Task<PaymentResponse> TransitionAsync(Guid actorId, Guid stamp, Guid orderId, Guid id, string status,
        int expectedVersion, string? reason, decimal? cashTendered, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var permission = status == "Refunded" ? AccessPolicies.RefundPayments : AccessPolicies.ManagePayments;
        var actor = await RequireActorAsync(actorId, stamp, permission, cancellationToken);
        var order = await LockOrderAsync(orderId, cancellationToken);
        // Todas as gravações de pagamento e cancelamento do pedido bloqueiam primeiro o pedido.
        var payment = await database.Payments.FromSqlInterpolated($"SELECT * FROM \"Payments\" WHERE \"Id\" = {id} AND \"OrderId\" = {orderId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw new PaymentException(PaymentError.PaymentNotFound, "Pagamento não encontrado para este pedido.");
        await database.Entry(payment).Collection(value => value.History).LoadAsync(cancellationToken);
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim().Normalize();
        var last = payment.History.MaxBy(history => history.Version)!;
        if (payment.Status == status && expectedVersion == payment.Version - 1 && last.ActorId == actorId && last.Reason == reason
            && (status != "Received" || payment.CashTendered == cashTendered))
        {
            await transaction.CommitAsync(cancellationToken);
            return ToResponse(payment);
        }
        if (expectedVersion != payment.Version)
            throw new PaymentException(PaymentError.PaymentVersionConflict, "O pagamento foi alterado por outro atendimento. Atualize o histórico.");
        if (!(payment.Status == "Pending" && status is "Received" or "Cancelled" || payment.Status == "Received" && status == "Refunded"))
            throw new PaymentException(PaymentError.PaymentTransitionDenied, "Esta alteração não é permitida para o pagamento atual.");
        if (status == "Received")
        {
            RequireActiveOrder(order);
            if (payment.Method == "Cash" ? cashTendered is null || cashTendered < payment.Amount : cashTendered is not null)
                throw new PaymentException(PaymentError.PaymentInvalidCash, "Em dinheiro, informe o valor entregue, igual ou maior que o pedido. Nas demais formas, não informe dinheiro entregue.");
            payment.CashTendered = cashTendered;
        }
        var previous = payment.Status;
        payment.Status = status;
        payment.Version++;
        payment.UpdatedAt = UtcNow();
        var history = new PaymentStatusHistory
        {
            PaymentId = payment.Id,
            Version = payment.Version,
            FromStatus = previous,
            ToStatus = status,
            ActorId = actor.Id,
            ActorName = actor.Name,
            Reason = reason,
            OccurredAt = payment.UpdatedAt
        };
        payment.History.Add(history);
        database.PaymentStatusHistory.Add(history);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Payment {PaymentId} changed from {FromStatus} to {ToStatus} by {ActorId}.", id, previous, status, actorId);
        return ToResponse(payment);
    }

    private async Task<User> RequireActorAsync(Guid id, Guid stamp, string permission, CancellationToken cancellationToken)
    {
        var actor = await database.Users.FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {id} FOR SHARE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (actor is null || !actor.IsActive || actor.SecurityStamp != stamp)
            throw new PaymentException(PaymentError.InvalidSession, "Sessão inválida. Faça login novamente.");
        var roles = AccessPolicies.RolesByPermission[permission];
        if (!await database.Roles.AnyAsync(role => role.Id == actor.RoleId && roles.Contains(role.Code), cancellationToken))
            throw new PaymentException(PaymentError.PermissionDenied, "Você não tem permissão para esta operação de pagamento.");
        return actor;
    }

    private async Task<Order> LockOrderAsync(Guid id, CancellationToken cancellationToken) =>
        await database.Orders.FromSqlInterpolated($"SELECT * FROM \"Orders\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw OrderNotFound();
    private static void RequireActiveOrder(Order order)
    {
        if (order.Status == "Cancelled")
            throw new PaymentException(PaymentError.PaymentOrderCancelled, "Pedido cancelado não pode receber novos pagamentos.");
    }
    private IQueryable<Payment> ReadPayments() => database.Payments.AsNoTracking().Include(payment => payment.History);
    private static PaymentException OrderNotFound() => new(PaymentError.PaymentOrderNotFound, "Pedido não encontrado.");
    private DateTimeOffset UtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
    private static PaymentResponse ToResponse(Payment payment) => new(payment.Id, payment.OrderId, payment.Method, payment.Status,
        payment.Version, payment.Amount, payment.CashTendered, payment.CashTendered - payment.Amount, payment.CreatedAt, payment.UpdatedAt,
        payment.History.OrderBy(history => history.Version).Select(history => new PaymentHistoryResponse(history.Version, history.FromStatus,
            history.ToStatus, history.ActorId, history.ActorName, history.Reason, history.OccurredAt)).ToArray());
}
