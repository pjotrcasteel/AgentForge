using System.Globalization;
using System.Text.Json;
using TokenBudget.Agent;
using TokenBudget.Application;
using TokenBudget.Domain;

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine("""
        TokenBudget Agent v0.2 - deterministic preflight with optional Microsoft Agent Framework review

        Usage:
          dotnet run --project src/TokenBudget.Agent.Cli -- preflight <plan.md> --model <id> [options]

        Options:
          --target codex|work|openai|anthropic  Execution target; default codex
          --budget <number>                      Max cumulative tokens; required
          --peak-budget <number>                 Optional maximum peak context tokens
          --repo <path>                          Optional local repository; metadata only
          --ai-model <openai-api-model-id>       Enable optional AI review using this model
          --allow-ai-upload true                 Required with --ai-model; sends plan to OpenAI
          --samples <1000..100000>              Monte Carlo iterations, default 10000
          --output <file>                        Save Markdown preflight report

        Note: this agent never modifies a repository or starts a coding session.
        """);
    return 0;
}

if (args[0] != "preflight" || args.Length < 3)
{
    Console.Error.WriteLine("Expected: preflight <plan.md> --model <id> --budget <tokens> [options]");
    return 1;
}

try
{
    string planPath = Path.GetFullPath(args[1]);
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (int index = 2; index < args.Length; index += 2)
    {
        if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal) ||
            !options.TryAdd(args[index][2..], args[index + 1]))
        {
            throw new ArgumentException("Expected unique --name value pairs.");
        }
    }

    string[] allowed = ["model", "target", "budget", "peak-budget", "repo", "ai-model", "allow-ai-upload", "samples", "output"];
    if (options.Keys.Any(key => !allowed.Contains(key, StringComparer.OrdinalIgnoreCase)))
    {
        throw new ArgumentException("Unknown option. Use --help for supported options.");
    }

    if (!File.Exists(planPath) || !options.TryGetValue("model", out string? executionModel) ||
        string.IsNullOrWhiteSpace(executionModel) || !options.TryGetValue("budget", out string? cumulativeBudget))
    {
        throw new ArgumentException("An existing plan file, --model and --budget are required.");
    }

    ExecutionTarget target = (options.GetValueOrDefault("target") ?? "codex").ToLowerInvariant() switch
    {
        "codex" => ExecutionTarget.CodexCli,
        "work" => ExecutionTarget.ChatGptWork,
        "openai" => ExecutionTarget.OpenAiResponses,
        "anthropic" => ExecutionTarget.AnthropicMessages,
        _ => throw new ArgumentException("Unsupported execution target.")
    };

    long? peak = options.TryGetValue("peak-budget", out string? peakValue)
        ? long.Parse(peakValue, CultureInfo.InvariantCulture)
        : null;
    int samples = options.TryGetValue("samples", out string? configuredSamples)
        ? int.Parse(configuredSamples, CultureInfo.InvariantCulture)
        : 10_000;
    var request = new AnalyzePlanRequest(
        await File.ReadAllTextAsync(planPath),
        executionModel,
        target,
        new BudgetConstraints(MaxCumulativeTokens: long.Parse(cumulativeBudget, CultureInfo.InvariantCulture),
            MaxPeakContextTokens: peak),
        RepositoryPath: options.GetValueOrDefault("repo"),
        SimulationCount: samples);

    string? reviewModel = options.GetValueOrDefault("ai-model");
    bool upload = options.TryGetValue("allow-ai-upload", out string? flag) &&
        bool.Parse(flag);
    if (upload && string.IsNullOrWhiteSpace(reviewModel))
    {
        throw new ArgumentException("--allow-ai-upload requires --ai-model.");
    }

    var service = new AgentPreflightService(AnalyzePlanService.CreateDefault(), new MicrosoftAgentFrameworkPlanReviewer());
    // The deterministic report is always computed before optional network/model calls.
    AgentPreflightResult result = await service.RunAsync(request,
        string.IsNullOrWhiteSpace(reviewModel) ? null : new AgentReviewOptions(reviewModel, upload));
    string contents = AgentPreflightFormatter.ToMarkdown(result);
    if (options.TryGetValue("output", out string? output))
    {
        await File.WriteAllTextAsync(Path.GetFullPath(output), contents);
        Console.WriteLine($"Report written to {Path.GetFullPath(output)}");
    }
    else
    {
        Console.WriteLine(contents);
    }

    return result.Estimate.Decision switch
    {
        BudgetDecision.Pass => 0,
        BudgetDecision.Warning => 2,
        BudgetDecision.Reject => 3,
        BudgetDecision.InsufficientEvidence => 4,
        _ => 1
    };
}
catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or
    OverflowException or FormatException or InvalidOperationException or JsonException)
{
    Console.Error.WriteLine($"TokenBudget Agent: {exception.Message}");
    return 1;
}
