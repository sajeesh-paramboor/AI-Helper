namespace AI103.ChatApp.Evaluation.Models;

public sealed record TestCase(
    string Id,
    string Category,
    string Question,
    IReadOnlyList<string> RequiredKeywords,
    IReadOnlyList<string> ProhibitedKeywords,
    IReadOnlyList<string> ExpectedCitations,
    string ExpectedSummary);
