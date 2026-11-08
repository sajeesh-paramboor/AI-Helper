using AI103.ChatApp.Application.DTOs;

namespace AI103.ChatApp.Application.Interfaces;

/// <summary>
/// Provider-agnostic contract for talking to a generative AI model.
/// The Application layer depends on this abstraction; Infrastructure supplies the
/// Azure AI Foundry implementation (FoundryChatService).
/// </summary>
public interface IChatService
{
    /// <summary>
    /// Sends a prompt and waits for the complete answer.
    /// </summary>
    /// <param name="message">The user prompt.</param>
    /// <param name="previousResponseId">ID of the prior response to continue from, or null to start a new conversation.</param>
    /// <param name="cancellationToken">Cancels the underlying model call.</param>
    Task<ChatResult> SendMessageAsync(
        string message,
        string? previousResponseId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a prompt and yields the answer incrementally as the model generates it.
    /// </summary>
    IAsyncEnumerable<ChatStreamUpdate> StreamMessageAsync(
        string message,
        string? previousResponseId = null,
        CancellationToken cancellationToken = default);
}
