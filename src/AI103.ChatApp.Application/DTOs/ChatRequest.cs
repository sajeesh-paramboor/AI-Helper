using System.ComponentModel.DataAnnotations;

namespace AI103.ChatApp.Application.DTOs;

/// <summary>
/// Incoming chat request. Used by both /api/chat/message and /api/chat/stream.
/// </summary>
public sealed class ChatRequest
{
    public const int MaxMessageLength = 4000;
    public const int MaxResponseIdLength = 256;
    public const int MaxConversationIdLength = 64;

    /// <summary>The user prompt.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "message is required.")]
    [StringLength(MaxMessageLength, ErrorMessage = "message must be at most {1} characters.")]
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Optional. The responseId returned by a previous turn. Used only when no conversationId is sent,
    /// to continue a chain that this server is not tracking. Null or omitted starts a new conversation.
    /// </summary>
    [StringLength(MaxResponseIdLength, ErrorMessage = "previousResponseId must be at most {1} characters.")]
    public string? PreviousResponseId { get; init; }

    /// <summary>
    /// Optional. The conversationId returned by a previous turn. When present the server continues that
    /// conversation, and previousResponseId is ignored. Null or omitted starts a new conversation.
    /// </summary>
    [RegularExpression(@"^[A-Za-z0-9\-]+$", ErrorMessage = "conversationId contains invalid characters.")]
    [StringLength(MaxConversationIdLength, ErrorMessage = "conversationId must be at most {1} characters.")]
    public string? ConversationId { get; init; }
}
