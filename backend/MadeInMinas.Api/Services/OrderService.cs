using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Cart;
using MadeInMinas.Api.DTOs.Orders;
using MadeInMinas.Api.Models;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Validation;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Services;

public sealed class OrderService(AppDbContext database, CartService cart, OrderStockService stock, TimeProvider clock, ILogger<OrderService> logger)
{
    public async Task<OrderPageResponse> ListAsync(OrderListQuery request, CancellationToken cancellationToken)
    {
        var query = database.Orders.AsNoTracking();
        if (request.Status is not null)
            query = query.Where(order => order.Status == request.Status);
        if (request.CustomerId is not null)
            query = query.Where(order => order.CustomerId == request.CustomerId);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().Normalize().ToUpperInvariant();
            var phone = BrazilianPhone.SearchDigits(request.Search) ?? "";
            int.TryParse(search.TrimStart('#'), out var number);
            query = query.Where(order => order.Number == number || order.CustomerName.ToUpper().Contains(search)
                || phone != "" && order.CustomerPhone.Contains(phone));
        }
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(order => order.CreatedAt).ThenByDescending(order => order.Number)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(order => new OrderSummaryResponse(order.Id, order.Number, order.CustomerName, order.Fulfillment,
                order.Status, order.Total, order.CreatedAt)).ToArrayAsync(cancellationToken);
        return new OrderPageResponse(items, request.Page, request.PageSize, total);
    }

    public async Task<OrderResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken);
        var order = await ReadOrders().SingleOrDefaultAsync(order => order.Id == id, cancellationToken) ?? throw NotFound();
        await transaction.CommitAsync(cancellationToken);
        return ToResponse(order);
    }

    public async Task<OrderCreationResult> CreateAsync(Guid actorId, Guid actorStamp, CreateOrderRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var actor = await RequireActorAsync(actorId, actorStamp, cancellationToken);
        // Serializa reenvios da mesma tentativa, inclusive quando chegam antes do primeiro commit.
        var lockKey = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"{actorId}:{request.RequestId}")));
        await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
        var hash = OrderFingerprint.ForRequest(request);
        var existingId = await database.Orders.Where(order => order.CreatedById == actorId && order.RequestId == request.RequestId)
            .Select(order => (Guid?)order.Id).SingleOrDefaultAsync(cancellationToken);
        if (existingId is not null)
            await database.Orders.FromSqlInterpolated($"SELECT * FROM \"Orders\" WHERE \"Id\" = {existingId.Value} FOR SHARE")
                .AsNoTracking().ToArrayAsync(cancellationToken);
        var existing = existingId is null ? null : await ReadOrders().SingleAsync(order => order.Id == existingId, cancellationToken);
        if (existing is not null)
        {
            if (existing.RequestHash != hash)
                throw new OrderException(OrderError.OrderRequestConflict, "Esta tentativa já foi usada com outro conteúdo. Consulte os pedidos antes de tentar novamente.");
            await transaction.CommitAsync(cancellationToken);
            return new OrderCreationResult(ToResponse(existing), false);
        }

        await LockCartAsync(request.Cart, cancellationToken);
        var quote = await cart.QuoteAsync(request.Cart, cancellationToken);
        if (quote.ReviewToken != request.ReviewToken)
            throw new OrderException(OrderError.OrderReviewChanged, "Os dados da compra mudaram. Revise os valores e o endereço novamente.");
        var now = UtcNow();
        var order = new Order
        {
            RequestId = request.RequestId!.Value,
            RequestHash = hash,
            CreatedById = actor.Id,
            CustomerId = quote.Customer.Id,
            CustomerName = quote.Customer.Name,
            CustomerPhone = quote.Customer.Phone,
            Fulfillment = quote.Fulfillment,
            AddressId = quote.Address?.Id,
            AddressStreet = quote.Address?.Street,
            AddressNumber = quote.Address?.Number,
            AddressNeighborhood = quote.Address?.Neighborhood,
            AddressCity = quote.Address?.City,
            AddressState = quote.Address?.State,
            AddressComplement = quote.Address?.Complement,
            AddressPostalCode = quote.Address?.PostalCode,
            AddressReference = quote.Address?.Reference,
            Notes = quote.Notes,
            Subtotal = quote.Subtotal,
            DeliveryFee = quote.DeliveryFee,
            Total = quote.Total,
            CreatedAt = now,
            UpdatedAt = now,
            Items = quote.Items.Select((item, index) => new OrderItem
            {
                Position = index + 1,
                ProductId = item.ProductId,
                ProductName = item.Name,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                LineTotal = item.LineTotal,
                Notes = item.Notes
            }).ToList(),
            History = [new OrderStatusHistory { Version = 1, ToStatus = "New", ActorId = actor.Id, ActorName = actor.Name, OccurredAt = now }]
        };
        database.Orders.Add(order);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Order {OrderId} number {OrderNumber} created by {ActorId}.", order.Id, order.Number, actorId);
        return new OrderCreationResult(ToResponse(order), true);
    }

    public async Task<OrderResponse> SetStatusAsync(Guid actorId, Guid actorStamp, Guid id, OrderStatusRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var actor = await RequireActorAsync(actorId, actorStamp, cancellationToken);
        var order = await database.Orders.FromSqlInterpolated($"SELECT * FROM \"Orders\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw NotFound();
        await database.Entry(order).Collection(value => value.Items).LoadAsync(cancellationToken);
        await database.Entry(order).Collection(value => value.History).LoadAsync(cancellationToken);
        await database.Entry(order).Collection(value => value.StockComponents).LoadAsync(cancellationToken);
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim().Normalize();
        var last = order.History.MaxBy(history => history.Version)!;
        if (order.Status == request.Status && request.ExpectedVersion == order.Version - 1 && last.ActorId == actorId && last.Reason == reason)
        {
            await transaction.CommitAsync(cancellationToken);
            return ToResponse(order);
        }
        if (request.ExpectedVersion != order.Version)
            throw new OrderException(OrderError.OrderVersionConflict, "O pedido foi alterado por outro atendimento. Atualize os dados antes de continuar.");
        if (!(order.Status == "New" && request.Status is "Confirmed" or "Cancelled"
            || order.Status is "Confirmed" or "InPreparation" or "Ready" or "AwaitingDelivery" or "OutForDelivery" && request.Status == "Cancelled"))
            throw new OrderException(OrderError.OrderTransitionDenied, "Esta alteração de status não é permitida.");
        if (request.Status == "Cancelled" && order.Status != "New" && !await database.Roles.AnyAsync(role => role.Id == actor.RoleId && role.Code == "Administrator", cancellationToken))
            throw new OrderException(OrderError.OrderCancellationDenied, "Somente o administrador pode cancelar um pedido após a confirmação.");
        if (request.Status == "Confirmed")
        {
            var input = new CartQuoteRequest(order.CustomerId, order.Fulfillment,
                order.Items.OrderBy(item => item.Position).Select(item => new CartItemRequest(item.ProductId, item.Quantity, item.Notes)).ToArray(),
                order.AddressId, order.Notes)
            { DeliveryFee = order.DeliveryFee };
            await LockCartAsync(input, cancellationToken);
            // Confirma disponibilidade atual sem reescrever preços e dados já registrados na compra.
            await cart.QuoteAsync(input, cancellationToken);
        }
        if (request.Status == "Cancelled" && await database.Payments.AnyAsync(payment => payment.OrderId == id
            && (payment.Status == "Pending" || payment.Status == "Received"), cancellationToken))
            throw new OrderException(OrderError.OrderPaymentUnresolved, "Cancele o pagamento pendente ou registre a devolução do valor recebido antes de cancelar o pedido.");
        var previous = order.Status;
        var now = UtcNow();
        if (request.Status == "Confirmed")
            await stock.ConsumeAsync(order, actor, now, cancellationToken);
        else if (request.Status == "Cancelled")
            await stock.CancelAsync(order, actor, now, cancellationToken);
        order.Status = request.Status;
        order.Version++;
        order.UpdatedAt = now;
        var history = new OrderStatusHistory
        {
            OrderId = order.Id,
            Version = order.Version,
            FromStatus = previous,
            ToStatus = order.Status,
            ActorId = actor.Id,
            ActorName = actor.Name,
            Reason = reason,
            OccurredAt = order.UpdatedAt
        };
        order.History.Add(history);
        database.OrderStatusHistory.Add(history);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Order {OrderId} status changed from {FromStatus} to {ToStatus} by {ActorId}.", id, previous, order.Status, actorId);
        return ToResponse(order);
    }

    private async Task<User> RequireActorAsync(Guid id, Guid stamp, CancellationToken cancellationToken)
    {
        var actor = await database.Users.FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {id} FOR SHARE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (actor is null || !actor.IsActive || actor.SecurityStamp != stamp)
            throw new OrderException(OrderError.InvalidSession, "Sessão inválida. Faça login novamente.");
        var roles = AccessPolicies.RolesByPermission[AccessPolicies.ManageOrders];
        if (!await database.Roles.AnyAsync(role => role.Id == actor.RoleId && roles.Contains(role.Code), cancellationToken))
            throw new OrderException(OrderError.PermissionDenied, "Acesso restrito ao administrador e atendente.");
        return actor;
    }

    private async Task LockCartAsync(CartQuoteRequest request, CancellationToken cancellationToken)
    {
        // A ordem dos bloqueios acompanha as gravações de clientes e catálogo.
        await database.Customers.FromSqlInterpolated($"SELECT * FROM \"Customers\" WHERE \"Id\" = {request.CustomerId} FOR SHARE")
            .AsNoTracking().ToArrayAsync(cancellationToken);
        if (request.AddressId is not null)
            await database.Addresses.FromSqlInterpolated($"SELECT * FROM \"Addresses\" WHERE \"Id\" = {request.AddressId} FOR SHARE")
                .AsNoTracking().ToArrayAsync(cancellationToken);
        var ids = request.Items.Select(item => item.ProductId!.Value).Distinct().ToArray();
        var products = await database.Products.FromSqlInterpolated($"SELECT * FROM \"Products\" WHERE \"Id\" = ANY ({ids}) ORDER BY \"Id\" FOR SHARE")
            .AsNoTracking().ToArrayAsync(cancellationToken);
        var categoryIds = products.Select(product => product.CategoryId).Distinct().ToArray();
        await database.Categories.FromSqlInterpolated($"SELECT * FROM \"Categories\" WHERE \"Id\" = ANY ({categoryIds}) ORDER BY \"Id\" FOR SHARE")
            .AsNoTracking().ToArrayAsync(cancellationToken);
    }

    private IQueryable<Order> ReadOrders() => database.Orders.AsNoTracking().Include(order => order.Items)
        .Include(order => order.History).Include(order => order.StockComponents).AsSplitQuery();
    private DateTimeOffset UtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
    private static OrderException NotFound() => new(OrderError.OrderNotFound, "Pedido não encontrado.");
    private static OrderResponse ToResponse(Order order) => new(order.Id, order.Number, order.Origin, order.Status, order.Version,
        new CartCustomerResponse(order.CustomerId, order.CustomerName, order.CustomerPhone), order.Fulfillment,
        order.AddressId is null ? null : new CartAddressResponse(order.AddressId.Value, order.AddressStreet!, order.AddressNumber!,
            order.AddressNeighborhood!, order.AddressCity!, order.AddressState!, order.AddressComplement, order.AddressPostalCode, order.AddressReference),
        order.Items.OrderBy(item => item.Position).Select(item => new CartItemResponse(item.ProductId, item.ProductName, item.Quantity,
            item.UnitPrice, item.LineTotal, item.Notes)).ToArray(), order.Notes, order.Subtotal, order.DeliveryFee, order.Total,
        order.CreatedAt, order.UpdatedAt, order.History.OrderBy(history => history.Version).Select(history => new OrderHistoryResponse(
            history.Version, history.FromStatus, history.ToStatus, history.ActorId, history.ActorName, history.Reason, history.OccurredAt)).ToArray())
    {
        StockStatus = order.StockStatus,
        StockComponents = order.StockComponents.OrderBy(item => item.ProductName).ThenBy(item => item.IngredientName)
            .ThenBy(item => item.ProductId).ThenBy(item => item.IngredientId)
            .Select(item => new OrderStockComponentResponse(item.ProductId, item.ProductName, item.IngredientId,
                item.IngredientName, item.Unit, item.ProductQuantity, item.RecipeYield, item.RecipeQuantity, item.ConsumedQuantity)).ToArray()
    };
}
