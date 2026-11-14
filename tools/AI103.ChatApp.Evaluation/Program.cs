using AI103.ChatApp.Evaluation.Datasets;
using AI103.ChatApp.Evaluation.Models;
using AI103.ChatApp.Evaluation.Reporting;
using AI103.ChatApp.Evaluation.Runners;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var apiBaseUrl = config["Api:BaseUrl"] ?? "http://localhost:5265";
var targetMode = args.Length > 0 ? args[0].ToLowerInvariant() : "all";

Console.WriteLine("==========================================================");
Console.WriteLine("     AI-103 RAG & Model Evaluation Framework");
Console.WriteLine("==========================================================");
Console.WriteLine($"Target API Base URL: {apiBaseUrl}");
Console.WriteLine($"Evaluation Mode:     {targetMode.ToUpperInvariant()}");
Console.WriteLine();

var testCases = MtnSandboxDataset.GetTestCases();
Console.WriteLine($"Loaded {testCases.Count} test cases from MTN Sandbox dataset.");

using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
var runner = new EvaluationRunner(httpClient, apiBaseUrl);

var allResults = new Dictionary<string, IReadOnlyList<EvaluationResult>>();

string[] modesToTest = targetMode switch
{
    "standard" => ["standard"],
    "filesearch" or "file-search" or "rag" => ["file-search"],
    "websearch" or "web-search" => ["web-search"],
    _ => ["standard", "file-search"]
};

foreach (var mode in modesToTest)
{
    Console.WriteLine($"\nRunning tests for mode: [{mode}]...");
    var results = await runner.RunEvaluationAsync(testCases, mode);
    allResults[mode] = results;
    ReportGenerator.PrintConsoleSummary(mode, results);
}

if (allResults.Count > 1)
{
    var report = ReportGenerator.GenerateMarkdownComparison(allResults);
    var reportPath = Path.Combine(AppContext.BaseDirectory, "evaluation-report.md");
    await File.WriteAllTextAsync(reportPath, report);
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"Comparison report saved to: {reportPath}");
    Console.ResetColor();
}

return 0;
