namespace AI103.ChatApp.Application.DTOs;

/// <summary>
/// Response of POST /api/chat/web-search/message. Same fields as <see cref="ChatResponse"/> plus the cited sources.
/// </summary>
public sealed record WebSearchChatResponse(
    string Answer,
    string ResponseId,
    string ConversationId,
    IReadOnlyList<SourceReference> Sources);
