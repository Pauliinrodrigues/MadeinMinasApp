using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MadeInMinas.Api.DTOs.Payments;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreatePaymentRequest(
    [Required] Guid? RequestId,
    [Required, RegularExpression("^(Cash|Pix|CreditCard|DebitCard)$")] string Method,
    [Required, Range(1, int.MaxValue)] int? ExpectedOrderVersion) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RequestId == Guid.Empty)
            yield return new ValidationResult("Informe o identificador da tentativa.", [nameof(RequestId)]);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReceivePaymentRequest(
    [Required, Range(1, int.MaxValue)] int? ExpectedVersion,
    [Required] bool? ReceivedConfirmed,
    decimal? CashTendered = null) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ReceivedConfirmed != true)
            yield return new ValidationResult("Confirme que o valor foi recebido e conferido.", [nameof(ReceivedConfirmed)]);
        if (CashTendered is { } cash && (cash < 0 || cash > 9999999999.99m || decimal.Round(cash, 2) != cash))
            yield return new ValidationResult("Informe dinheiro válido, com até duas casas decimais.", [nameof(CashTendered)]);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CancelPaymentRequest(
    [Required, Range(1, int.MaxValue)] int? ExpectedVersion,
    [Required, StringLength(500)] string Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RefundPaymentRequest(
    [Required, Range(1, int.MaxValue)] int? ExpectedVersion,
    [Required, StringLength(500)] string Reason,
    [Required] bool? RefundedConfirmed) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RefundedConfirmed != true)
            yield return new ValidationResult("Confirme que a devolução integral já foi realizada.", [nameof(RefundedConfirmed)]);
    }
}

public sealed class PaymentListQuery
{
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}

public sealed record PaymentHistoryResponse(int Version, string? FromStatus, string ToStatus,
    Guid ActorId, string ActorName, string? Reason, DateTimeOffset OccurredAt);
public sealed record PaymentResponse(Guid Id, Guid OrderId, string Method, string Status, int Version,
    decimal Amount, decimal? CashTendered, decimal? ChangeAmount, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, PaymentHistoryResponse[] History);
public sealed record PaymentPageResponse(Guid OrderId, int OrderNumber, string OrderStatus, int OrderVersion,
    decimal OrderTotal, decimal ReceivedAmount, decimal Balance, PaymentResponse? ActivePayment,
    PaymentResponse[] Items, int Page, int PageSize, int TotalCount);
public sealed record PaymentCreationResult(PaymentResponse Payment, bool Created);
