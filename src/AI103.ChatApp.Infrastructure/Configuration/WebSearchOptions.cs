namespace AI103.ChatApp.Infrastructure.Configuration;

/// <summary>
/// Server-side switch for the web search endpoints. Bound from "Foundry:WebSearch".
/// Disabled by default: web search adds many input tokens and sends the model's search query to a public search service.
/// </summary>
public sealed class WebSearchOptions
{
    public const string SectionName = "Foundry:WebSearch";

    /// <summary>When false, /api/chat/web-search/* returns 404 and the tool is never sent.</summary>
    public bool Enabled { get; set; }
}
