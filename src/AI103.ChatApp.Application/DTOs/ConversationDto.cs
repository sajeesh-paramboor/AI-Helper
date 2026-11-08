namespace AI103.ChatApp.Application.DTOs;

/// <summary>One message of a conversation, as returned by GET /api/chat/conversations/{id}.</summary>
public sealed record ConversationMessageDto(string Role, string Content, DateTimeOffset CreatedAtUtc);

/// <summary>A conversation and its messages in order.</summary>
public sealed record ConversationDto(
    string ConversationId,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<ConversationMessageDto> Messages);
