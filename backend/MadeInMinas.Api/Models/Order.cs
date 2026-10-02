namespace MadeInMinas.Api.Models;

public sealed class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Number { get; set; }
    public Guid RequestId { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public Guid CreatedById { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string Fulfillment { get; set; } = string.Empty;
    public Guid? AddressId { get; set; }
    public string? AddressStreet { get; set; }
    public string? AddressNumber { get; set; }
    public string? AddressNeighborhood { get; set; }
    public string? AddressCity { get; set; }
    public string? AddressState { get; set; }
    public string? AddressComplement { get; set; }
    public string? AddressPostalCode { get; set; }
    public string? AddressReference { get; set; }
    public string Origin { get; set; } = "Manual";
    public string Status { get; set; } = "New";
    public int Version { get; set; } = 1;
    public string StockStatus { get; set; } = "Pending";
    public string? Notes { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<OrderItem> Items { get; set; } = [];
    public List<OrderStatusHistory> History { get; set; } = [];
    public List<OrderStockComponent> StockComponents { get; set; } = [];
}
