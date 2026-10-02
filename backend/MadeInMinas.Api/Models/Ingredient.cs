namespace MadeInMinas.Api.Models;

public sealed class Ingredient
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal UnitCost { get; set; }
    public decimal MinimumStock { get; set; }
    public decimal CurrentStock { get; set; }
    public long StockVersion { get; set; }
    public string? Supplier { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
