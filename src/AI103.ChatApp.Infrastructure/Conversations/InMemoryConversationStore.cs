using AI103.ChatApp.Application.Interfaces;
using AI103.ChatApp.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace AI103.ChatApp.Infrastructure.Conversations;

/// <summary>
/// Process-local conversation store. Conversations are lost when the app restarts or scales out to more
/// than one instance, and idle ones expire. Swap in a persistent IConversationStore for production.
/// </summary>
public sealed class InMemoryConversationStore : IConversationStore, IDisposable
{
    private static readonly TimeSpan IdleExpiry = TimeSpan.FromHours(2);
    private static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(24);

    // The size limit bounds memory use: each conversation counts as one entry.
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 10_000 });

    public Task<Conversation?> GetAsync(string conversationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_cache.TryGetValue(conversationId, out Conversation? conversation) ? conversation : null);

    public Task SaveAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        _cache.Set(
            conversation.Id,
            conversation,
            new MemoryCacheEntryOptions
            {
                Size = 1,
                SlidingExpiration = IdleExpiry,
                AbsoluteExpirationRelativeToNow = MaxLifetime
            });
        return Task.CompletedTask;
    }

    public void Dispose() => _cache.Dispose();
}
