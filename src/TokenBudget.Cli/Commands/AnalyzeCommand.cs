using TokenBudget.Application;
using TokenBudget.Domain;

namespace TokenBudget.Cli.Commands;

/// <summary>Validates a plan and emits a deterministic preflight forecast.</summary>
internal static class AnalyzeCommand
{
    private static readonly string[] SupportedOptions =
    [
        "provider", "model", "repo", "max-total-tokens", "max-peak-context-tokens",
        "model-context-limit", "risk-tolerance", "format", "output", "seed", "samples",
        "allow-provider-upload"
    ];

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length < 2)
            {
                throw new ArgumentException("Expected: tokenbudget analyze <plan.md> [options]");
            }

            string filePath = Path.GetFullPath(args[1]);
            if (!File.Exists(filePath))
            {
                throw new ArgumentException("Plan file not found: " + filePath);
            }

            CliOptions options = CliOptions.Parse(args, 2, SupportedOptions);
            AnalyzePlanRequest request = new(
                await File.ReadAllTextAsync(filePath),
                options.Require("model"),
                ResolveTarget(options.Get("provider") ?? "codex"),
                new BudgetConstraints(
                    MaxCumulativeTokens: options.OptionalLong("max-total-tokens"),
                    MaxPeakContextTokens: options.OptionalLong("max-peak-context-tokens"),
                    RiskTolerance: options.Double("risk-tolerance", 0.10),
                    ModelContextWindowTokens: options.OptionalLong("model-context-limit")),
                RepositoryPath: options.Get("repo"),
                RandomSeed: options.Integer("seed", 1729),
                SimulationCount: options.Integer("samples", 10_000),
                AllowProviderUpload: options.Boolean("allow-provider-upload"));

            EstimateReport report = await AnalyzePlanService.CreateDefault().AnalyzeAsync(request);
            string contents = (options.Get("format") ?? "markdown").ToLowerInvariant() switch
            {
                "markdown" or "md" => EstimateReportFormatter.ToMarkdown(report),
                "json" => EstimateReportFormatter.ToJson(report),
                _ => throw new ArgumentException("--format must be markdown or json.")
            };

            if (options.Get("output") is { } output)
            {
                string outputPath = Path.GetFullPath(output);
                await File.WriteAllTextAsync(outputPath, contents);
                Console.WriteLine("Report written to " + outputPath);
                Console.WriteLine("Decision: " + report.Decision);
            }
            else
            {
                Console.WriteLine(contents);
            }

            return report.Decision switch
            {
                BudgetDecision.Pass => 0,
                BudgetDecision.Warning => 2,
                BudgetDecision.Reject => 3,
                BudgetDecision.InsufficientEvidence => 4,
                _ => 1
            };
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or
            OverflowException or FormatException)
        {
            Console.Error.WriteLine("TokenBudget: " + exception.Message);
            return 1;
        }
    }

    internal static ExecutionTarget ResolveTarget(string provider) => provider.ToLowerInvariant() switch
    {
        "codex" or "codex-cli" => ExecutionTarget.CodexCli,
        "codex-app-server" => ExecutionTarget.CodexAppServer,
        "openai" or "openai-responses" => ExecutionTarget.OpenAiResponses,
        "openai-agents" => ExecutionTarget.OpenAiAgents,
        "work" or "chatgpt-work" => ExecutionTarget.ChatGptWork,
        "anthropic" => ExecutionTarget.AnthropicMessages,
        _ => throw new ArgumentException("Unsupported --provider. See tokenbudget capabilities.")
    };
}
