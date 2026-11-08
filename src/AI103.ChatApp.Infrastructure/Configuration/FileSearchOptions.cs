using System.Text.RegularExpressions;

namespace AI103.ChatApp.Infrastructure.Configuration;

/// <summary>
/// Server-side settings for the file search endpoints. Bound from "Foundry:FileSearch".
/// Disabled by default. The vector store is created and filled manually in the Foundry portal;
/// this application only references it by ID and never uploads or deletes documents.
/// </summary>
public sealed class FileSearchOptions
{
    public const string SectionName = "Foundry:FileSearch";

    private static readonly Regex VectorStoreIdFormat = new(
        @"^vs_[A-Za-z0-9_-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>When false, /api/chat/file-search/* returns 404 and the tool is never sent.</summary>
    public bool Enabled { get; set; }

    /// <summary>IDs of existing vector stores to search (format "vs_..."). Required when <see cref="Enabled"/> is true.</summary>
    public List<string> VectorStoreIds { get; set; } = [];

    /// <summary>Optional cap on the number of passages retrieved per search. Leave unset to use the service default.</summary>
    public int? MaxResultCount { get; set; }

    /// <summary>Returns an error message when the options are unusable, or null when they are valid.</summary>
    public string? Validate()
    {
        if (!Enabled)
        {
            return null;
        }

        if (VectorStoreIds is not { Count: > 0 } || VectorStoreIds.Any(string.IsNullOrWhiteSpace))
        {
            return $"{SectionName}:VectorStoreIds must contain at least one vector store ID when file search is enabled.";
        }

        if (VectorStoreIds.Any(id => !VectorStoreIdFormat.IsMatch(id)))
        {
            return $"{SectionName}:VectorStoreIds contains a value that is not a vector store ID (expected the form 'vs_...').";
        }

        if (MaxResultCount is < 1 or > 50)
        {
            return $"{SectionName}:MaxResultCount must be between 1 and 50.";
        }

        return null;
    }
}
