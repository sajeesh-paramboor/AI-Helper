using AI103.ChatApp.Application.DTOs;

namespace AI103.ChatApp.Application.Interfaces;

/// <summary>
/// Conversation layer for web-search turns. Uses the same <see cref="IConversationStore"/> as
/// <see cref="IConversationService"/>, so a conversationId works across both kinds of turn.
/// </summary>
public interface IWebSearchConversationService
{
    Task<WebSearchChatResponse> SendAsync(ChatRequest request, CancellationToken cancellationToken = default);

    IAsyncEnumerable<WebSearchStreamUpdate> StreamAsync(ChatRequest request, CancellationToken cancellationToken = default);
}
