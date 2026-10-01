using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MadeInMinas.Api.DTOs.Cart;

namespace MadeInMinas.Api.DTOs.Dispatch;

public sealed class DispatchQuery
{
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 50)] public int PageSize { get; init; } = 20;
    [Required, RegularExpression("^(Ready|AwaitingDelivery|OutForDelivery|Delivered)$")]
    public string Status { get; init; } = "Ready";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DispatchStatusRequest(
    [Required, RegularExpression("^(AwaitingDelivery|OutForDelivery|Delivered|Finalized)$")] string Status,
    [Required, Range(1, int.MaxValue)] int? ExpectedVersion);

public sealed record DispatchPaymentResponse(string Method, string Status, decimal Amount, decimal? CashTendered, decimal? ChangeAmount);
public sealed record DispatchOrderResponse(Guid Id, int Number, string Status, int Version, string Fulfillment,
    string CustomerName, string CustomerPhone, CartAddressResponse? Address, CartItemResponse[] Items,
    string? Notes, decimal Subtotal, decimal DeliveryFee, decimal Total, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, DateTimeOffset? ReadyAt, DispatchPaymentResponse? Payment);
public sealed record DispatchPageResponse(DateTimeOffset ServerTime, DispatchOrderResponse[] Items, int Page, int PageSize, int TotalCount);
