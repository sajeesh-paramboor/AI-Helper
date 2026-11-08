using AI103.ChatApp.Application.DTOs;

namespace AI103.ChatApp.Application.Interfaces;

/// <summary>
/// Model call with the built-in file search tool enabled over the configured vector store(s).
/// Kept separate from <see cref="IChatService"/> so the normal chat path can never invoke file search.
/// </summary>
public interface IFileSearchChatService
{
    /// <summary>Sends a prompt with file search available. The model decides whether to search.</summary>
    Task<FileSearchChatResult> SendWithFileSearchAsync(
        string message,
        string? previousResponseId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Streaming variant. Yields text deltas, then one completed update with the response ID and documents.</summary>
    IAsyncEnumerable<FileSearchStreamUpdate> StreamWithFileSearchAsync(
        string message,
        string? previousResponseId = null,
        CancellationToken cancellationToken = default);
}
