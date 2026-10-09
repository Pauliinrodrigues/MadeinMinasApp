namespace MadeInMinas.Api.Models;

public sealed class PrintStation
{
    public int Id { get; set; } = 1;
    public string? KeyHash { get; set; }
    public bool Automatic { get; set; }
    public int Version { get; set; } = 1;
    public DateTimeOffset? LastSeenAt { get; set; }
}

public sealed class PrintJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public long OrderNumber { get; set; }
    public string Mode { get; set; } = "";
    public string RequestKey { get; set; } = "";
    public string Payload { get; set; } = "";
    public string State { get; set; } = "Queued";
    public Guid? ClaimId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}
