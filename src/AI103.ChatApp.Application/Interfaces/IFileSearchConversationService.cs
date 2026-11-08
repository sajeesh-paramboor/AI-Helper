using AI103.ChatApp.Application.DTOs;

namespace AI103.ChatApp.Application.Interfaces;

/// <summary>
/// Conversation layer for file-search turns. Uses the same <see cref="IConversationStore"/> as
/// <see cref="IConversationService"/>, so a conversationId works across all kinds of turn.
/// </summary>
public interface IFileSearchConversationService
{
    Task<FileSearchChatResponse> SendAsync(ChatRequest request, CancellationToken cancellationToken = default);

    IAsyncEnumerable<FileSearchStreamUpdate> StreamAsync(ChatRequest request, CancellationToken cancellationToken = default);
}
