namespace AI103.ChatApp.Application.Exceptions;

/// <summary>
/// The requested conversation does not exist, or has expired from the store.
/// </summary>
public sealed class ConversationNotFoundException(string conversationId)
    : Exception($"Conversation '{conversationId}' was not found.")
{
    public string ConversationId { get; } = conversationId;
}
