using System.Text;
using AI103.ChatApp.Evaluation.Models;

namespace AI103.ChatApp.Evaluation.Reporting;

public static class ReportGenerator
{
    public static void PrintConsoleSummary(string mode, IReadOnlyList<EvaluationResult> results)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"=== Evaluation Report: {mode.ToUpperInvariant()} ===");
        Console.ResetColor();

        Console.WriteLine($"{"ID",-8} | {"Category",-22} | {"Correct",-7} | {"Ground",-7} | {"Citation",-8} | {"Latency",-8} | {"Overall",-7}");
        Console.WriteLine(new string('-', 85));

        foreach (var r in results)
        {
            if (!r.Success)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"{r.TestCase.Id,-8} | {r.TestCase.Category,-22} | ERROR: {r.ErrorMessage}");
                Console.ResetColor();
                continue;
            }

            var color = r.OverallScore >= 0.8 ? ConsoleColor.Green : (r.OverallScore >= 0.5 ? ConsoleColor.Yellow : ConsoleColor.Red);
            Console.ForegroundColor = color;
            Console.WriteLine(
                $"{r.TestCase.Id,-8} | {r.TestCase.Category,-22} | {r.CorrectnessScore,7:P0} | {r.GroundednessScore,7:P0} | {r.CitationScore,8:P0} | {r.LatencyMs,6}ms | {r.OverallScore,7:P0}");
            Console.ResetColor();
        }

        var successful = results.Where(r => r.Success).ToList();
        if (successful.Count > 0)
        {
            Console.WriteLine(new string('-', 85));
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(
                $"{"AVG",-8} | {"Total: " + results.Count,-22} | {successful.Average(r => r.CorrectnessScore),7:P0} | {successful.Average(r => r.GroundednessScore),7:P0} | {successful.Average(r => r.CitationScore),8:P0} | {successful.Average(r => r.LatencyMs),6:F0}ms | {successful.Average(r => r.OverallScore),7:P0}");
            Console.ResetColor();
        }
        Console.WriteLine();
    }

    public static string GenerateMarkdownComparison(IReadOnlyDictionary<string, IReadOnlyList<EvaluationResult>> modeResults)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# AI-103 RAG & Model Evaluation Report");
        sb.AppendLine();
        sb.AppendLine($"*Generated at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*");
        sb.AppendLine();
        sb.AppendLine("## 1. Executive Summary & Mode Comparison");
        sb.AppendLine();
        sb.AppendLine("| Mode | Avg Latency | Avg Correctness | Avg Groundedness | Citation Score | Overall Quality |");
        sb.AppendLine("|---|---|---|---|---|---|");

        foreach (var (mode, results) in modeResults)
        {
            var successful = results.Where(r => r.Success).ToList();
            if (successful.Count == 0)
            {
                sb.AppendLine($"| **{mode}** | FAILED | 0% | 0% | 0% | 0% |");
            }
            else
            {
                sb.AppendLine($"| **{mode}** | {successful.Average(r => r.LatencyMs):F0} ms | {successful.Average(r => r.CorrectnessScore):P0} | {successful.Average(r => r.GroundednessScore):P0} | {successful.Average(r => r.CitationScore):P0} | **{successful.Average(r => r.OverallScore):P0}** |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## 2. Detailed Test Breakdown");
        sb.AppendLine();

        foreach (var (mode, results) in modeResults)
        {
            sb.AppendLine($"### Mode: `{mode}`");
            sb.AppendLine();
            sb.AppendLine("| ID | Category | Question | Latency | Citations | Score |");
            sb.AppendLine("|---|---|---|---|---|---|");

            foreach (var r in results)
            {
                var cites = r.Citations.Count > 0 ? string.Join(", ", r.Citations) : "None";
                sb.AppendLine($"| {r.TestCase.Id} | {r.TestCase.Category} | {r.TestCase.Question} | {r.LatencyMs} ms | {cites} | {r.OverallScore:P0} |");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
