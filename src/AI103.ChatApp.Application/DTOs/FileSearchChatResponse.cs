namespace AI103.ChatApp.Application.DTOs;

/// <summary>
/// Response of POST /api/chat/file-search/message. Same fields as <see cref="ChatResponse"/> plus the cited documents.
/// </summary>
public sealed record FileSearchChatResponse(
    string Answer,
    string ResponseId,
    string ConversationId,
    IReadOnlyList<DocumentReference> Documents);
