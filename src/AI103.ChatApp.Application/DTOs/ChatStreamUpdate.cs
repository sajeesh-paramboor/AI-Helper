namespace AI103.ChatApp.Application.DTOs;

public enum ChatStreamUpdateKind
{
    /// <summary>An incremental piece of generated text.</summary>
    TextDelta,

    /// <summary>The stream finished; <see cref="ChatStreamUpdate.ResponseId"/> is populated.</summary>
    Completed
}

/// <summary>
/// One event from a streaming model call.
/// </summary>
public sealed record ChatStreamUpdate(
    ChatStreamUpdateKind Kind,
    string? Text = null,
    string? ResponseId = null,
    string? ConversationId = null)
{
    public static ChatStreamUpdate Delta(string text) => new(ChatStreamUpdateKind.TextDelta, Text: text);

    public static ChatStreamUpdate Complete(string responseId, string? conversationId = null) =>
        new(ChatStreamUpdateKind.Completed, ResponseId: responseId, ConversationId: conversationId);
}
