using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Cart;
using MadeInMinas.Api.DTOs.PublicCheckout;
using MadeInMinas.Api.Models;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Validation;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Services;

public sealed class PublicCheckoutService(
    AppDbContext database,
    PublicCartService cart,
    TimeProvider clock,
    ILogger<PublicCheckoutService> logger,
    PublicOrderAccess access,
    DeliverySettingsService settings)
{
    public async Task<PublicCheckoutReviewResponse> ReviewAsync(PublicCheckoutRequest request, CancellationToken cancellationToken)
    {
        var area = request.Fulfillment == "Delivery"
            ? (await DeliveryAreasAsync(cancellationToken)).SingleOrDefault(area => area.Id == request.Address!.AreaId)
                ?? throw new OrderException(OrderError.PublicDeliveryUnavailable, "A região selecionada não está disponível para entrega. Confira as regiões atendidas ou escolha retirada.")
            : null;
        var address = area is null ? null : new CartAddressResponse(null,
            request.Address!.Street.Trim().Normalize(), request.Address.Number.Trim().Normalize(),
            area.Neighborhood, area.City, area.State, Clean(request.Address.Complement),
            Clean(request.Address.PostalCode)?.Replace("-", "", StringComparison.Ordinal), Clean(request.Address.Reference));
        var quote = await cart.QuoteAsync(request.Cart, cancellationToken);
        BrazilianPhone.TryNormalize(request.Phone, out var phone);
        var review = new PublicCheckoutReviewResponse(request.Name.Trim().Normalize(), phone, request.Fulfillment, quote.Items,
            quote.Notes, quote.Subtotal, area?.Fee ?? 0, quote.Subtotal + (area?.Fee ?? 0), "", quote.CalculatedAt)
        { Address = address, DeliveryAreaId = area?.Id };
        return review with { ReviewToken = PublicCheckoutFingerprint.ForReview(review) };
    }

    public Task<PublicDeliveryAreaResponse[]> DeliveryAreasAsync(CancellationToken cancellationToken) =>
        settings.PublicAreasAsync(cancellationToken);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Normalize();

    public async Task<PublicOrderCreationResult> CreateAsync(PublicOrderRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"public-order:{request.RequestId}")));
        await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
        var hash = PublicCheckoutFingerprint.ForRequest(request);
        var existing = await database.Orders.AsNoTracking()
            .SingleOrDefaultAsync(order => order.Origin == "DirectLink" && order.RequestId == request.RequestId, cancellationToken);
        if (existing is not null)
        {
            if (existing.RequestHash != hash)
                throw new OrderException(OrderError.OrderRequestConflict, "Esta tentativa já foi usada com outro conteúdo. Procure o atendimento antes de fazer outro pedido.");
            await transaction.CommitAsync(cancellationToken);
            return new PublicOrderCreationResult(Receipt(existing), false);
        }

        if (request.Checkout.Fulfillment == "Delivery")
            await settings.LockForOrderAsync(cancellationToken);
        var now = DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
        // Cliente antes do catálogo, na mesma ordem dos bloqueios do atendimento.
        var customerId = await ResolveCustomerAsync(request.Checkout, now, cancellationToken);
        var ids = request.Checkout.Cart.Items.Select(item => item.ProductId!.Value).Distinct().ToArray();
        var products = await database.Products.FromSqlInterpolated($"SELECT * FROM \"Products\" WHERE \"Id\" = ANY ({ids}) ORDER BY \"Id\" FOR SHARE")
            .AsNoTracking().ToArrayAsync(cancellationToken);
        var categoryIds = products.Select(product => product.CategoryId).Distinct().ToArray();
        await database.Categories.FromSqlInterpolated($"SELECT * FROM \"Categories\" WHERE \"Id\" = ANY ({categoryIds}) ORDER BY \"Id\" FOR SHARE")
            .AsNoTracking().ToArrayAsync(cancellationToken);
        var review = await ReviewAsync(request.Checkout, cancellationToken);
        if (review.ReviewToken != request.ReviewToken)
            throw new OrderException(OrderError.OrderReviewChanged, "Os dados da compra mudaram. Revise novamente antes de enviar.");

        var order = new Order
        {
            RequestId = request.RequestId!.Value,
            RequestHash = hash,
            Origin = "DirectLink",
            CustomerId = customerId,
            CustomerName = review.Name,
            CustomerPhone = review.Phone,
            Fulfillment = review.Fulfillment,
            AddressStreet = review.Address?.Street,
            AddressNumber = review.Address?.Number,
            AddressNeighborhood = review.Address?.Neighborhood,
            AddressCity = review.Address?.City,
            AddressState = review.Address?.State,
            AddressComplement = review.Address?.Complement,
            AddressPostalCode = review.Address?.PostalCode,
            AddressReference = review.Address?.Reference,
            Notes = review.Notes,
            Subtotal = review.Subtotal,
            DeliveryFee = review.DeliveryFee,
            Total = review.Total,
            CreatedAt = now,
            UpdatedAt = now,
            Items = review.Items.Select((item, index) => new OrderItem
            {
                Position = index + 1,
                ProductId = item.ProductId,
                ProductName = item.Name,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                LineTotal = item.LineTotal,
                Notes = item.Notes
            }).ToList(),
            History = [new OrderStatusHistory { Version = 1, ToStatus = "New", ActorName = "Cliente pelo site", OccurredAt = now }]
        };
        database.Orders.Add(order);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Public order {OrderId} number {OrderNumber} received.", order.Id, order.Number);
        return new PublicOrderCreationResult(Receipt(order), true);
    }

    private async Task<Guid> ResolveCustomerAsync(PublicCheckoutRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        BrazilianPhone.TryNormalize(request.Phone, out var phone);
        var id = Guid.NewGuid();
        var name = request.Name.Trim().Normalize();
        var normalizedName = name.ToUpperInvariant();
        // Não atualiza cadastro preexistente. ON CONFLICT também cobre cadastro manual concorrente.
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Customers" ("Id", "Name", "NormalizedName", "Phone", "IsActive", "CreatedAt", "UpdatedAt")
            VALUES ({id}, {name}, {normalizedName}, {phone}, TRUE, {now}, {now})
            ON CONFLICT ("Phone") DO NOTHING
            """, cancellationToken);
        var customer = await database.Customers.FromSqlInterpolated($"SELECT * FROM \"Customers\" WHERE \"Phone\" = {phone} FOR SHARE")
            .AsNoTracking().SingleAsync(cancellationToken);
        if (!customer.IsActive)
            throw new OrderException(OrderError.PublicCheckoutUnavailable, "Não foi possível concluir o envio. Procure o atendimento para continuar.");
        return customer.Id;
    }

    private PublicOrderReceipt Receipt(Order order) => new(order.Number, order.Fulfillment, order.Total, order.CreatedAt)
    {
        Tracking = access.Issue(order.Id, order.CreatedAt)
    };
}
