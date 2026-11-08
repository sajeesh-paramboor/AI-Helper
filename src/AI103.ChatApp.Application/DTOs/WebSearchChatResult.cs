namespace AI103.ChatApp.Application.DTOs;

/// <summary>Result of a model call that had the web search tool enabled.</summary>
public sealed record WebSearchChatResult(string Answer, string ResponseId, IReadOnlyList<SourceReference> Sources);
