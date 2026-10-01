using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MadeInMinas.Api.DTOs.Cart;

namespace MadeInMinas.Api.DTOs.Orders;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateOrderRequest(
    [Required] Guid? RequestId,
    [Required, RegularExpression("^[A-F0-9]{64}$")] string ReviewToken,
    [Required] CartQuoteRequest Cart) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RequestId == Guid.Empty)
            yield return new ValidationResult("Informe o identificador da tentativa de registro.", [nameof(RequestId)]);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OrderStatusRequest(
    [Required, RegularExpression("^(Confirmed|Cancelled)$")] string Status,
    [Required, Range(1, int.MaxValue)] int? ExpectedVersion,
    [StringLength(500)] string? Reason = null) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Status == "Cancelled" && string.IsNullOrWhiteSpace(Reason))
            yield return new ValidationResult("Informe o motivo do cancelamento.", [nameof(Reason)]);
        if (Status == "Confirmed" && !string.IsNullOrWhiteSpace(Reason))
            yield return new ValidationResult("Motivo é informado somente no cancelamento.", [nameof(Reason)]);
    }
}

public sealed class OrderListQuery
{
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    [StringLength(120)] public string? Search { get; init; }
    [RegularExpression("^(New|Confirmed|Cancelled)$")] public string? Status { get; init; }
    public Guid? CustomerId { get; init; }
}

public sealed record OrderSummaryResponse(Guid Id, int Number, string CustomerName, string Fulfillment,
    string Status, decimal Total, DateTimeOffset CreatedAt);
public sealed record OrderPageResponse(OrderSummaryResponse[] Items, int Page, int PageSize, int TotalCount);
public sealed record OrderHistoryResponse(int Version, string? FromStatus, string ToStatus,
    Guid ActorId, string ActorName, string? Reason, DateTimeOffset OccurredAt);
public sealed record OrderResponse(Guid Id, int Number, string Origin, string Status, int Version,
    CartCustomerResponse Customer, string Fulfillment, CartAddressResponse? Address, CartItemResponse[] Items,
    string? Notes, decimal Subtotal, decimal DeliveryFee, decimal Total, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, OrderHistoryResponse[] History);
public sealed record OrderCreationResult(OrderResponse Order, bool Created);
