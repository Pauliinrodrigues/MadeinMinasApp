namespace MadeInMinas.Api.Services.Ai;

public interface IAiService
{
    Task<AiTurnResult> ProcessMessageAsync(AiTurnRequest request,
        Func<AiToolCall, CancellationToken, Task<AiToolResult>> executeTool, CancellationToken cancellationToken);
}

public interface IAiProvider
{
    Task<AiProviderResponse> RespondAsync(AiProviderRequest request, CancellationToken cancellationToken);
}

public sealed record AiHistoryMessage(string Role, string Text);
public sealed record AiTurnRequest(Guid ConversationId, Guid LeaseId, int SourceSequence,
    IReadOnlyList<AiHistoryMessage> History, string StateJson);
public sealed record AiToolCall(string Id, string Name, string ArgumentsJson);
public sealed record AiToolResult(string Json, string? CustomerText = null, bool HandOff = false);
public sealed record AiTurnResult(string Text, int InputTokens = 0, int OutputTokens = 0, bool HandOff = false);
public sealed record AiToolDefinition(string Name, string Description, object Parameters);
public sealed record AiProviderRequest(string Instructions, IReadOnlyList<object> Input,
    IReadOnlyList<AiToolDefinition> Tools);
public sealed record AiProviderResponse(IReadOnlyList<AiToolCall> Calls, IReadOnlyList<object> Output,
    int InputTokens = 0, int OutputTokens = 0);

public sealed class AiProviderException(string reason) : Exception("O serviço de atendimento automático está indisponível.")
{
    public string Reason { get; } = reason;
}
