namespace MadeInMinas.Api.Services;

public enum ChatError { ChatUnavailable, ChatRequestConflict, ChatVersionConflict, ChatAssignmentRequired, ChatClosed, ChatExpired, ChatLimitReached }

public sealed class ChatException(ChatError error, string message) : Exception(message)
{
    public ChatError Error { get; } = error;
}
