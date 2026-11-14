using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AI103.ChatApp.Evaluation.Models;

namespace AI103.ChatApp.Evaluation.Runners;

public sealed class EvaluationRunner(HttpClient httpClient, string baseUrl)
{
    public async Task<IReadOnlyList<EvaluationResult>> RunEvaluationAsync(
        IReadOnlyList<TestCase> testCases,
        string mode,
        CancellationToken cancellationToken = default)
    {
        var results = new List<EvaluationResult>();

        var endpoint = mode.ToLowerInvariant() switch
        {
            "filesearch" or "file-search" or "rag" => $"{baseUrl.TrimEnd('/')}/api/chat/file-search/message",
            "websearch" or "web-search" => $"{baseUrl.TrimEnd('/')}/api/chat/web-search/message",
            _ => $"{baseUrl.TrimEnd('/')}/api/chat/message"
        };

        foreach (var testCase in testCases)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var payload = new { message = testCase.Question };
                var response = await httpClient.PostAsJsonAsync(endpoint, payload, cancellationToken);
                stopwatch.Stop();

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    results.Add(new EvaluationResult(
                        TestCase: testCase,
                        Mode: mode,
                        LatencyMs: stopwatch.ElapsedMilliseconds,
                        Success: false,
                        ErrorMessage: $"HTTP {(int)response.StatusCode}: {errorBody}",
                        Answer: string.Empty,
                        Citations: [],
                        CorrectnessScore: 0,
                        GroundednessScore: 0,
                        CompletenessScore: 0,
                        CitationScore: 0,
                        OverallScore: 0));
                    continue;
                }

                var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                var root = jsonDoc.RootElement;
                var answer = root.TryGetProperty("answer", out var a) ? a.GetString() ?? string.Empty : string.Empty;

                var citations = new List<string>();
                if (root.TryGetProperty("documents", out var docs) && docs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var d in docs.EnumerateArray())
                    {
                        if (d.ValueKind == JsonValueKind.String)
                        {
                            var s = d.GetString();
                            if (!string.IsNullOrEmpty(s)) citations.Add(s);
                        }
                        else if (d.ValueKind == JsonValueKind.Object)
                        {
                            if (d.TryGetProperty("fileName", out var fn) || d.TryGetProperty("FileName", out fn))
                            {
                                var s = fn.GetString();
                                if (!string.IsNullOrEmpty(s)) citations.Add(s);
                            }
                        }
                    }
                }
                else if (root.TryGetProperty("sources", out var sources) && sources.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in sources.EnumerateArray())
                    {
                        if (s.ValueKind == JsonValueKind.Object && (s.TryGetProperty("title", out var t) || s.TryGetProperty("Title", out t)))
                        {
                            var title = t.GetString();
                            if (!string.IsNullOrEmpty(title)) citations.Add(title);
                        }
                    }
                }

                // 1. Correctness: proportion of required keywords present
                var matchedKeywords = testCase.RequiredKeywords.Count(kw => answer.Contains(kw, StringComparison.OrdinalIgnoreCase));
                var correctness = testCase.RequiredKeywords.Count > 0 ? (double)matchedKeywords / testCase.RequiredKeywords.Count : 1.0;

                // 2. Groundedness / Anti-Hallucination: penalize prohibited hallucinated terms
                var hallucinated = testCase.ProhibitedKeywords.Any(pk => answer.Contains(pk, StringComparison.OrdinalIgnoreCase));
                var groundedness = hallucinated ? 0.0 : 1.0;

                // 3. Completeness: combination of correctness + response length sanity check
                var completeness = (correctness * 0.7) + (answer.Length > 80 ? 0.3 : 0.0);

                // 4. Citation Quality: did it cite the expected reference?
                double citationScore = 0.0;
                if (testCase.ExpectedCitations.Count == 0)
                {
                    citationScore = 1.0;
                }
                else
                {
                    var matchedCitations = testCase.ExpectedCitations.Count(exp => citations.Any(c => c.Contains(exp, StringComparison.OrdinalIgnoreCase)));
                    citationScore = (double)matchedCitations / testCase.ExpectedCitations.Count;
                }

                // Overall weighted composite score
                var overall = (correctness * 0.35) + (groundedness * 0.30) + (completeness * 0.20) + (citationScore * 0.15);

                results.Add(new EvaluationResult(
                    TestCase: testCase,
                    Mode: mode,
                    LatencyMs: stopwatch.ElapsedMilliseconds,
                    Success: true,
                    ErrorMessage: null,
                    Answer: answer,
                    Citations: citations,
                    CorrectnessScore: Math.Round(correctness, 2),
                    GroundednessScore: Math.Round(groundedness, 2),
                    CompletenessScore: Math.Round(completeness, 2),
                    CitationScore: Math.Round(citationScore, 2),
                    OverallScore: Math.Round(overall, 2)));
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                results.Add(new EvaluationResult(
                    TestCase: testCase,
                    Mode: mode,
                    LatencyMs: stopwatch.ElapsedMilliseconds,
                    Success: false,
                    ErrorMessage: ex.Message,
                    Answer: string.Empty,
                    Citations: [],
                    CorrectnessScore: 0,
                    GroundednessScore: 0,
                    CompletenessScore: 0,
                    CitationScore: 0,
                    OverallScore: 0));
            }
        }

        return results;
    }
}
