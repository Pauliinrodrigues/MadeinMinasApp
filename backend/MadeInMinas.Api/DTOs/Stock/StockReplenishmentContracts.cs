using System.ComponentModel.DataAnnotations;

namespace MadeInMinas.Api.DTOs.Stock;

public sealed class StockReplenishmentQuery
{
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    [StringLength(150)] public string? Search { get; init; }
    [Required, RegularExpression("^(Attention|OutOfStock|LowStock|Unrecorded|All)$")]
    public string Status { get; init; } = "Attention";
    public bool IncludeInactive { get; init; }
}

public sealed record StockReplenishmentSummary(int TotalCount, int AttentionCount, int OutOfStockCount,
    int LowStockCount, int UnrecordedCount);

public sealed record StockReplenishmentItem(Guid IngredientId, string Name, string Unit, string? Supplier,
    bool IsActive, decimal CurrentStock, decimal MinimumStock, decimal QuantityToMinimum, bool IsLowStock,
    bool HasMovements);

public sealed record StockReplenishmentResponse(StockReplenishmentSummary Summary, StockReplenishmentItem[] Items,
    int Page, int PageSize, int TotalCount);
