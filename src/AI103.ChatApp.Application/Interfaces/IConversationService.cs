using AI103.ChatApp.Application.DTOs;

namespace AI103.ChatApp.Application.Interfaces;

/// <summary>
/// Orchestrates a chat turn: resolves the conversation, calls <see cref="IChatService"/> with the right
/// previous response ID, and records the result. Controllers depend on this, not on IChatService.
/// </summary>
public interface IConversationService
{
    /// <exception cref="Exceptions.ConversationNotFoundException">The request names a conversation that does not exist.</exception>
    Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken cancellationToken = default);

    /// <exception cref="Exceptions.ConversationNotFoundException">The request names a conversation that does not exist.</exception>
    IAsyncEnumerable<ChatStreamUpdate> StreamAsync(ChatRequest request, CancellationToken cancellationToken = default);

    /// <summary>Returns the conversation's messages, or null if it does not exist.</summary>
    Task<ConversationDto?> GetAsync(string conversationId, CancellationToken cancellationToken = default);
}
