namespace MadeInMinas.Api.Models;

public sealed class Recipe
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int YieldQuantity { get; set; }
    public string? Instructions { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<RecipeItem> Items { get; set; } = [];
}
