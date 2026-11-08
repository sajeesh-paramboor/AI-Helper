namespace AI103.ChatApp.Application.DTOs;

public enum WebSearchStreamUpdateKind
{
    /// <summary>An incremental piece of generated text.</summary>
    TextDelta,

    /// <summary>The stream finished; response ID and sources are populated.</summary>
    Completed
}

/// <summary>
/// One event from a streaming web search call. Separate from <see cref="ChatStreamUpdate"/> so the existing
/// streaming contract is unchanged. Sources are only known when the stream completes.
/// </summary>
public sealed record WebSearchStreamUpdate(
    WebSearchStreamUpdateKind Kind,
    string? Text = null,
    string? ResponseId = null,
    string? ConversationId = null,
    IReadOnlyList<SourceReference>? Sources = null)
{
    public static WebSearchStreamUpdate Delta(string text) => new(WebSearchStreamUpdateKind.TextDelta, Text: text);

    public static WebSearchStreamUpdate Complete(
        string responseId, IReadOnlyList<SourceReference> sources, string? conversationId = null) =>
        new(WebSearchStreamUpdateKind.Completed, ResponseId: responseId, ConversationId: conversationId, Sources: sources);
}
