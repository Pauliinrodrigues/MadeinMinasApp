namespace MadeInMinas.Api.Models;

// The draft belongs to a visitor capability, never to a phone-number lookup.
public sealed class ChatOrderDraft
{
    public Guid ConversationId { get; set; }
    public int Version { get; set; } = 1;
    public string ItemsJson { get; set; } = "[]";
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Fulfillment { get; set; }
    public string? AddressJson { get; set; }
    public string? Notes { get; set; }
    public string? QuoteJson { get; set; }
    public string? ReviewJson { get; set; }
    public Guid? OrderRequestId { get; set; }
    public Guid? ConfirmationRequestId { get; set; }
    public string? ConfirmationHash { get; set; }
    public Guid? OrderId { get; set; }
    public string? ReceiptJson { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
