namespace MadeInMinas.Api.Models;

public sealed class OrderStockComponent
{
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public Guid IngredientId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string IngredientName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public int ProductQuantity { get; set; }
    public int RecipeYield { get; set; }
    public decimal RecipeQuantity { get; set; }
    public decimal ConsumedQuantity { get; set; }
}
