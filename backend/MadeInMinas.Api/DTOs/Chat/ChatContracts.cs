using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MadeInMinas.Api.DTOs.Chat;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StartChatRequest([Required] Guid? RequestId,
    [Required, StringLength(120)] string Name, [Required, StringLength(2000)] string Text) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RequestId == Guid.Empty)
            yield return new ValidationResult("Informe o identificador da tentativa.", [nameof(RequestId)]);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SendChatMessageRequest([Required] Guid? RequestId,
    [Required, StringLength(2000)] string Text) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RequestId == Guid.Empty)
            yield return new ValidationResult("Informe o identificador da mensagem.", [nameof(RequestId)]);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChatActionRequest([Required, Range(1, int.MaxValue)] int? ExpectedVersion);

public sealed class ChatMessagesQuery
{
    [Range(0, int.MaxValue)] public int After { get; init; }
}

public sealed class ChatListQuery
{
    [Required, RegularExpression("^(Waiting|InService|Closed)$")] public string Status { get; init; } = "Waiting";
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
}

public sealed record ChatAccessResponse(string Token, DateTimeOffset ExpiresAt);
public sealed record StartChatResponse(ChatAccessResponse? Access, DateTimeOffset CreatedAt);
public sealed record ChatStartResult(StartChatResponse Response, bool Created);
public sealed record ChatMessageResponse(int Sequence, string Kind, string Text, DateTimeOffset CreatedAt);
public sealed record ChatSendResult(ChatMessageResponse Message, bool Created);
public sealed record ChatTranscriptResponse(string Status, int Version, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, ChatMessageResponse[] Messages, bool HasMore);
public sealed record ChatSummaryResponse(Guid Id, string VisitorName, string Status, Guid? AssignedToId,
    string? AssignedToName, int Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public DateTimeOffset ExpiresAt => CreatedAt.AddDays(7);
}
public sealed record StaffChatResponse(ChatSummaryResponse Conversation, ChatTranscriptResponse Transcript);
public sealed record ChatPageResponse(ChatSummaryResponse[] Items, int Page, int PageSize, int TotalCount);
