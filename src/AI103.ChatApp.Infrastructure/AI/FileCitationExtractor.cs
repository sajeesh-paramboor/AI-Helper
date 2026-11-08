using System.Text;
using System.Text.RegularExpressions;
using AI103.ChatApp.Application.DTOs;
using OpenAI.Responses;

namespace AI103.ChatApp.Infrastructure.AI;

/// <summary>
/// Reads file citations from Responses API output items. Pure and side-effect free.
/// Unlike URL citations, file citations carry no URL, only a file ID and file name.
/// The answer text is returned unchanged.
/// </summary>
public static class FileCitationExtractor
{
    public const int MaxDocuments = 20;
    public const int MaxFileNameLength = 200;

    public sealed record Extracted(string Answer, IReadOnlyList<DocumentReference> Documents);

    public static Extracted Extract(IEnumerable<ResponseItem> outputItems)
    {
        ArgumentNullException.ThrowIfNull(outputItems);

        var answer = new StringBuilder();
        var candidates = new List<(string? FileId, string? FileName)>();

        foreach (var item in outputItems)
        {
            if (item is not MessageResponseItem { Role: MessageRole.Assistant } message)
            {
                continue;
            }

            foreach (var part in message.Content)
            {
                if (part.Kind != ResponseContentPartKind.OutputText)
                {
                    continue;
                }

                answer.Append(part.Text ?? string.Empty);

                foreach (var citation in (part.OutputTextAnnotations ?? []).OfType<FileCitationMessageAnnotation>())
                {
                    candidates.Add((citation.FileId, citation.Filename));
                }
            }
        }

        return new Extracted(answer.ToString(), CleanDocuments(candidates));
    }

    /// <summary>
    /// Drops entries without a file ID, removes duplicates by file ID (first occurrence wins),
    /// cleans the display name, and caps the number of documents.
    /// </summary>
    public static IReadOnlyList<DocumentReference> CleanDocuments(IEnumerable<(string? FileId, string? FileName)> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var documents = new List<DocumentReference>();

        foreach (var (fileId, fileName) in candidates)
        {
            if (string.IsNullOrWhiteSpace(fileId) || !seen.Add(fileId))
            {
                continue;
            }

            documents.Add(new DocumentReference(CleanFileName(fileName, fileId), fileId));
            if (documents.Count == MaxDocuments)
            {
                break;
            }
        }

        return documents;
    }

    private static string CleanFileName(string? fileName, string fileId)
    {
        // Control characters are dropped and whitespace is collapsed. The name is still untrusted text.
        var cleaned = Regex.Replace(fileName ?? string.Empty, @"[\p{C}\s]+", " ").Trim();
        if (cleaned.Length == 0)
        {
            return fileId;
        }

        return cleaned.Length <= MaxFileNameLength ? cleaned : cleaned[..MaxFileNameLength].TrimEnd() + "…";
    }
}
