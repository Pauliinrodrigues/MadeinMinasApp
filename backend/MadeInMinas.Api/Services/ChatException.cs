namespace MadeInMinas.Api.Services;

public enum ChatError { ChatUnavailable, ChatRequestConflict, ChatVersionConflict, ChatAssignmentRequired, ChatClosed, ChatExpired, ChatLimitReached, ChatAutomationUnavailable, ChatDraftChanged, ChatOrderIncomplete, ChatOrderReviewRequired, ChatOrderAlreadyCreated, ChatAutomationPending }

public sealed class ChatException(ChatError error, string message) : Exception(message)
{
    public ChatError Error { get; } = error;
}
