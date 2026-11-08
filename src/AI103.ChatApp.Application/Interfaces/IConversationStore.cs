using AI103.ChatApp.Domain.Entities;

namespace AI103.ChatApp.Application.Interfaces;

/// <summary>
/// Where conversations are kept between turns. The first implementation is in-memory;
/// a persistent store (database, cache) can replace it without touching the rest of the app.
/// </summary>
public interface IConversationStore
{
    /// <summary>Returns the conversation, or null if it does not exist or has expired.</summary>
    Task<Conversation?> GetAsync(string conversationId, CancellationToken cancellationToken = default);

    Task SaveAsync(Conversation conversation, CancellationToken cancellationToken = default);
}
