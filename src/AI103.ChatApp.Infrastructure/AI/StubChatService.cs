using System.Runtime.CompilerServices;
using AI103.ChatApp.Application.DTOs;
using AI103.ChatApp.Application.Interfaces;

namespace AI103.ChatApp.Infrastructure.AI;

/// <summary>
/// TEMPORARY placeholder so the API can run before the Azure AI Foundry integration exists.
/// It never calls any model. It is replaced by FoundryChatService in Phase 8.
/// </summary>
public sealed class StubChatService : IChatService
{
    public Task<ChatResult> SendMessageAsync(
        string message,
        string? previousResponseId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ChatResult($"[stub] You said: {message}", $"stub-{Guid.NewGuid():N}"));
    }

    public async IAsyncEnumerable<ChatStreamUpdate> StreamMessageAsync(
        string message,
        string? previousResponseId = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var word in $"[stub] You said: {message}".Split(' '))
        {
            await Task.Delay(50, cancellationToken);
            yield return ChatStreamUpdate.Delta(word + " ");
        }

        yield return ChatStreamUpdate.Complete($"stub-{Guid.NewGuid():N}");
    }
}
