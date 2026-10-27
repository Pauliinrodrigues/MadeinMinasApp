namespace MadeInMinas.Api.Models;

public sealed class ChatConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public string VisitorName { get; set; } = string.Empty;
    public string Status { get; set; } = "Waiting";
    public string AutomationMode { get; set; } = "Manual";
    public int AiProcessedSequence { get; set; }
    public Guid? AiLeaseId { get; set; }
    public DateTimeOffset? AiLeaseExpiresAt { get; set; }
    public int? AiLeaseSourceSequence { get; set; }
    public DateOnly? AiCallsDate { get; set; }
    public int AiDailyCalls { get; set; }
    public int AiTotalCalls { get; set; }
    public Guid? AssignedToId { get; set; }
    public string? AssignedToName { get; set; }
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}
