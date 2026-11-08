namespace AI103.ChatApp.Application.DTOs;

/// <summary>
/// Result of a non-streaming model call.
/// </summary>
/// <param name="Answer">The generated text.</param>
/// <param name="ResponseId">Provider response ID; pass it back as <c>previousResponseId</c> to continue the conversation.</param>
public sealed record ChatResult(string Answer, string ResponseId);
