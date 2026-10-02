using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MadeInMinas.Api.DTOs.Stock;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StockMovementRequest(
    Guid RequestId,
    [Required, Range(0, long.MaxValue)] long? ExpectedVersion,
    [Required, RegularExpression("^(Entry|Exit|Count)$")] string Type,
    [Required] decimal? Quantity,
    [Required, StringLength(500)] string Reason) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RequestId == Guid.Empty)
            yield return new ValidationResult("Informe o identificador do lançamento.", [nameof(RequestId)]);
        if (Quantity is { } quantity && (quantity < 0 || quantity > 999999.999m || decimal.Round(quantity, 3) != quantity || (Type != "Count" && quantity == 0)))
            yield return new ValidationResult("Use uma quantidade válida, com até três casas decimais. Zero é permitido somente na contagem.", [nameof(Quantity)]);
    }
}

public sealed class StockQuery
{
    [Range(1, 1000000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}

public sealed record StockMovementResponse(Guid Id, Guid RequestId, long Version, string Type, decimal Quantity,
    decimal Delta, decimal PreviousBalance, decimal Balance, string Reason, Guid ActorId, string ActorName,
    string IngredientName, string Unit, DateTimeOffset CreatedAt);

public sealed record StockResponse(Guid IngredientId, string Name, string Unit, bool IsActive, decimal CurrentStock,
    decimal MinimumStock, bool IsLowStock, long Version, IReadOnlyList<StockMovementResponse> Movements,
    int Page, int PageSize, int TotalCount);
