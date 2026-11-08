namespace AI103.ChatApp.Application.DTOs;

/// <summary>
/// A public web source that the model cited. Both values come from the Responses API citation
/// annotations, not from generated text. Treat them as untrusted when rendering.
/// </summary>
public sealed record SourceReference(string Title, string Url);
