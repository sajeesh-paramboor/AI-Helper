namespace AI103.ChatApp.Application.DTOs;

public enum FileSearchStreamUpdateKind
{
    /// <summary>An incremental piece of generated text.</summary>
    TextDelta,

    /// <summary>The stream finished; response ID and cited documents are populated.</summary>
    Completed
}

/// <summary>
/// One event from a streaming file search call. Separate from <see cref="ChatStreamUpdate"/> so the existing
/// streaming contract is unchanged. Documents are only known when the stream completes.
/// </summary>
public sealed record FileSearchStreamUpdate(
    FileSearchStreamUpdateKind Kind,
    string? Text = null,
    string? ResponseId = null,
    string? ConversationId = null,
    IReadOnlyList<DocumentReference>? Documents = null)
{
    public static FileSearchStreamUpdate Delta(string text) => new(FileSearchStreamUpdateKind.TextDelta, Text: text);

    public static FileSearchStreamUpdate Complete(
        string responseId, IReadOnlyList<DocumentReference> documents, string? conversationId = null) =>
        new(FileSearchStreamUpdateKind.Completed, ResponseId: responseId, ConversationId: conversationId, Documents: documents);
}
