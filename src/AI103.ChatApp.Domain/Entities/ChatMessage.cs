namespace AI103.ChatApp.Domain.Entities;

/// <summary>
/// A single message in a conversation.
/// </summary>
public sealed record ChatMessage(ChatRole Role, string Content, DateTimeOffset CreatedAtUtc)
{
    public static ChatMessage Create(ChatRole role, string content) =>
        new(role, content, DateTimeOffset.UtcNow);
}
