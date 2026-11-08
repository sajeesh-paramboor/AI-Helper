namespace AI103.ChatApp.Application.DTOs;

/// <summary>Result of a model call that had the file search tool enabled.</summary>
public sealed record FileSearchChatResult(string Answer, string ResponseId, IReadOnlyList<DocumentReference> Documents);
