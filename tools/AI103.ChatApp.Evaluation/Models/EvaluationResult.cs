namespace AI103.ChatApp.Evaluation.Models;

public sealed record EvaluationResult(
    TestCase TestCase,
    string Mode,
    long LatencyMs,
    bool Success,
    string? ErrorMessage,
    string Answer,
    IReadOnlyList<string> Citations,
    double CorrectnessScore,
    double GroundednessScore,
    double CompletenessScore,
    double CitationScore,
    double OverallScore);
