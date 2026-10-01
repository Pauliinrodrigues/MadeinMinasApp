namespace MadeInMinas.Api.Models;

public sealed class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid RequestId { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public Guid CreatedById { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public int Version { get; set; } = 1;
    public decimal Amount { get; set; }
    public decimal? CashTendered { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<PaymentStatusHistory> History { get; set; } = [];
}
