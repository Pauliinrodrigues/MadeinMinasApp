using System.ComponentModel.DataAnnotations;

namespace MadeInMinas.Api.DTOs.Reports;

public sealed class SalesReportQuery : IValidatableObject
{
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartDate.HasValue != EndDate.HasValue)
            yield return new ValidationResult("Informe as duas datas ou omita ambas.", [nameof(StartDate), nameof(EndDate)]);
        if (StartDate is { } start && EndDate is { } end)
        {
            if (end < start || end.DayNumber - start.DayNumber >= 90)
                yield return new ValidationResult("Selecione de 1 a 90 dias, com a data final igual ou posterior à inicial.", [nameof(EndDate)]);
            if (end == DateOnly.MaxValue)
                yield return new ValidationResult("A data final deve ser anterior a 9999-12-31.", [nameof(EndDate)]);
        }
    }
}

public sealed record SalesReportResponse(
    DateOnly StartDate, DateOnly EndDate, string TimeZone, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    DateTimeOffset CalculatedAt, SalesMetricsResponse Summary, SalesReportDayResponse[] Days,
    PaymentMethodReceiptsResponse[] PaymentMethods, SalesReportProductResponse[] TopProducts);

public sealed record SalesMetricsResponse(int Created, int CreatedAndCancelled, int Confirmed,
    decimal ConfirmedValue, decimal? AverageTicket, decimal Received, decimal Refunded, decimal NetReceived);
public sealed record SalesReportDayResponse(DateOnly Date, SalesMetricsResponse Metrics);
public sealed record PaymentMethodReceiptsResponse(string Method, decimal Received, decimal Refunded, decimal NetReceived);
public sealed record SalesReportProductResponse(Guid ProductId, string ProductName, long Quantity, decimal ItemValue);
