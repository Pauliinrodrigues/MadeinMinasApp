using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MadeInMinas.Api.DTOs.StoreAttendance;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StoreOpeningHours(
    [Range(1, 7)] int Day,
    [Required, RegularExpression("^([01][0-9]|2[0-3]):[0-5][0-9]$")] string OpensAt,
    [Required, RegularExpression("^([01][0-9]|2[0-3]):[0-5][0-9]$")] string ClosesAt,
    bool IsClosed = false);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StoreAttendanceData(
    [Required, MinLength(7), MaxLength(7)] StoreOpeningHours[] Hours,
    [Required, MinLength(1), MaxLength(3)] string[] PaymentMethods,
    [Required, StringLength(500)] string DeliveryEstimateMessage,
    [Required] bool? IsPaused,
    [Required] bool? ReceiveOrdersOutsideHours) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Hours is null || Hours.Any(hour => hour is null) || Hours.Select(hour => hour.Day).Distinct().Count() != 7)
            yield return new ValidationResult("Informe os sete dias da semana, sem repetir dias.", [nameof(Hours)]);
        else if (Hours.Any(hour => !hour.IsClosed && hour.ClosesAt != "00:00" && string.CompareOrdinal(hour.ClosesAt, hour.OpensAt) <= 0))
            yield return new ValidationResult("O fechamento deve ser após a abertura. Use 00:00 para encerrar à meia-noite.", [nameof(Hours)]);
        if (PaymentMethods is null || PaymentMethods.Any(method => method is not ("Card" or "Cash" or "Pix"))
            || PaymentMethods.Distinct(StringComparer.Ordinal).Count() != PaymentMethods.Length)
            yield return new ValidationResult("Selecione cartão, dinheiro ou Pix, sem repetir formas de pagamento.", [nameof(PaymentMethods)]);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaveStoreAttendanceRequest(
    [Required, RegularExpression("^[A-F0-9]{64}$")] string ExpectedRevision,
    [Required] StoreAttendanceData Settings);

public sealed record StoreAttendanceResponse(StoreAttendanceData Settings, string Revision,
    DateTimeOffset? UpdatedAt, string? UpdatedBy);

public sealed record PublicStoreAttendanceResponse(StoreOpeningHours[] Hours, string[] PaymentMethods,
    string DeliveryEstimateMessage, string TimeZone, bool IsOpen, bool IsPaused, bool AcceptingOrders,
    DateTimeOffset CheckedAt, DateTimeOffset? NextChangeAt);
