namespace AI103.ChatApp.Application.DTOs;

/// <summary>
/// Response body of POST /api/chat/message.
/// </summary>
/// <param name="Answer">The generated text.</param>
/// <param name="ResponseId">The provider's ID of this response.</param>
/// <param name="ConversationId">This app's conversation ID. Send it back to continue the conversation.</param>
public sealed record ChatResponse(string Answer, string ResponseId, string ConversationId);
