using AI103.ChatApp.Application.DTOs;

namespace AI103.ChatApp.Application.Interfaces;

/// <summary>
/// Model call with the built-in web search tool enabled. Kept separate from <see cref="IChatService"/>
/// so the normal chat path can never invoke web search.
/// </summary>
public interface IWebSearchChatService
{
    /// <summary>
    /// Sends a prompt with web search available. The model decides whether to search.
    /// </summary>
    Task<WebSearchChatResult> SendWithWebSearchAsync(
        string message,
        string? previousResponseId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streaming variant. Yields text deltas, then one completed update that carries the response ID and sources.
    /// </summary>
    IAsyncEnumerable<WebSearchStreamUpdate> StreamWithWebSearchAsync(
        string message,
        string? previousResponseId = null,
        CancellationToken cancellationToken = default);
}
