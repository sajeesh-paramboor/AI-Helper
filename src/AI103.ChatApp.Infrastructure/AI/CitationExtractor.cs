using System.Text;
using System.Text.RegularExpressions;
using AI103.ChatApp.Application.DTOs;
using OpenAI.Responses;

namespace AI103.ChatApp.Infrastructure.AI;

/// <summary>
/// Reads URL citations from Responses API output items and cleans them for display.
/// Pure and side-effect free, so it can be unit tested without Azure.
/// </summary>
public static class CitationExtractor
{
    public const int MaxSources = 20;
    public const int MaxTitleLength = 200;

    // The inline form the model wrote for a citation, e.g. "([learn.microsoft.com](https://learn.microsoft.com/x))".
    // A range is only removed from the answer when its text matches this exactly.
    private static readonly Regex InlineLink = new(
        @"^\(\[[^\]\r\n]*\]\(https?://[^\s)]+\)\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public sealed record Extracted(string Answer, IReadOnlyList<SourceReference> Sources);

    /// <summary>
    /// Builds the answer text and the de-duplicated source list from the assistant messages in <paramref name="outputItems"/>.
    /// </summary>
    /// <param name="removeInlineLinks">
    /// When true, citation ranges whose text is exactly an inline Markdown link are removed from the answer,
    /// because the same links are returned as <see cref="Extracted.Sources"/>. Anything that does not match is left as written.
    /// </param>
    public static Extracted Extract(IEnumerable<ResponseItem> outputItems, bool removeInlineLinks = true)
    {
        ArgumentNullException.ThrowIfNull(outputItems);

        var answer = new StringBuilder();
        var candidates = new List<(string? Title, Uri? Uri)>();

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

                var text = part.Text ?? string.Empty;
                var citations = (part.OutputTextAnnotations ?? [])
                    .OfType<UriCitationMessageAnnotation>()
                    .ToList();

                foreach (var citation in citations)
                {
                    candidates.Add((citation.Title, citation.Uri));
                }

                answer.Append(removeInlineLinks ? RemoveInlineLinks(text, citations) : text);
            }
        }

        return new Extracted(answer.ToString(), CleanSources(candidates));
    }

    /// <summary>
    /// Keeps only http/https URLs, removes duplicates (first occurrence wins), bounds the title length,
    /// falls back to the host name for an empty title, and caps the number of sources.
    /// </summary>
    public static IReadOnlyList<SourceReference> CleanSources(IEnumerable<(string? Title, Uri? Uri)> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sources = new List<SourceReference>();

        foreach (var (title, uri) in candidates)
        {
            if (uri is null
                || !uri.IsAbsoluteUri
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                continue;
            }

            var url = uri.AbsoluteUri;
            if (!seen.Add(url))
            {
                continue;
            }

            sources.Add(new SourceReference(CleanTitle(title, uri), url));
            if (sources.Count == MaxSources)
            {
                break;
            }
        }

        return sources;
    }

    private static string CleanTitle(string? title, Uri uri)
    {
        // Control characters are dropped and whitespace is collapsed. The title is still untrusted text.
        var cleaned = Regex.Replace(title ?? string.Empty, @"[\p{C}\s]+", " ").Trim();
        if (cleaned.Length == 0)
        {
            return uri.Host;
        }

        return cleaned.Length <= MaxTitleLength ? cleaned : cleaned[..MaxTitleLength].TrimEnd() + "…";
    }

    private static string RemoveInlineLinks(string text, IReadOnlyList<UriCitationMessageAnnotation> citations)
    {
        // Valid, non-overlapping ranges only, processed from the end so earlier indexes stay correct.
        var ranges = new List<(int Start, int End)>();
        foreach (var citation in citations.OrderBy(c => c.StartIndex))
        {
            var start = citation.StartIndex;
            var end = citation.EndIndex;
            if (start < 0 || end > text.Length || end <= start)
            {
                continue;
            }

            if (ranges.Count > 0 && start < ranges[^1].End)
            {
                continue;
            }

            if (!InlineLink.IsMatch(text.AsSpan(start, end - start)))
            {
                continue;
            }

            ranges.Add((start, end));
        }

        if (ranges.Count == 0)
        {
            return text;
        }

        var result = new StringBuilder(text);
        for (var i = ranges.Count - 1; i >= 0; i--)
        {
            var (start, end) = ranges[i];

            // Also drop the single space that separated the link from the sentence.
            if (start > 0 && result[start - 1] == ' ')
            {
                start--;
            }

            result.Remove(start, end - start);
        }

        return result.ToString();
    }
}
