namespace MadeInMinas.Api.Models;

public sealed class ChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public int Sequence { get; set; }
    public Guid? RequestId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public Guid? ActorId { get; set; }
    public string? ActorName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
