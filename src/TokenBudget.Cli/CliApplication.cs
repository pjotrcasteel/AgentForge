using TokenBudget.Cli.Commands;
using TokenBudget.Domain;

namespace TokenBudget.Cli;

/// <summary>Routes command-line requests to independent handlers.</summary>
internal static class CliApplication
{
    public static Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return QuickPreflightCommand.RunAsync(["preflight"]);
        }

        if (args[0] is "--help" or "-h" or "help")
        {
            PrintHelp();
            return Task.FromResult(0);
        }

        return args[0].ToLowerInvariant() switch
        {
            "setup" => SetupCommand.RunAsync(),
            "preflight" or "check" => QuickPreflightCommand.RunAsync(args),
            "analyze" => AnalyzeCommand.RunAsync(args),
            "budget" => ProviderBudgetCommand.RunAsync(args),
            "copilot-probe" => CopilotProbeCommand.RunAsync(args),
            "ingest-provider-session" => ProviderSessionCommand.RunAsync(args, ingestion: true),
            "provider-history" => ProviderSessionCommand.RunAsync(args, ingestion: false),
            "provider-calibration" => ProviderCalibrationCommand.RunAsync(args),
            "inspect-codex" => CodexInspectionCommand.RunAsync(args),
            "ingest-codex" => TelemetryCommand.RunAsync(args, ingestion: true),
            "history" => TelemetryCommand.RunAsync(args, ingestion: false),
            "capabilities" => Task.FromResult(PrintCapabilities()),
            _ => args.Length == 1 && !args[0].StartsWith("-", StringComparison.Ordinal)
                ? QuickPreflightCommand.RunAsync(["preflight", args[0]])
                : Task.FromResult(UnknownCommand())
        };
    }

    private static int PrintCapabilities()
    {
        foreach (ProviderCapability capability in ProviderCapabilities.All)
        {
            Console.WriteLine(capability.Target + ": offline=" + capability.CanAnalyzeOffline +
                ", exact-count=" + capability.CanCountExactInput + ", telemetry=" + capability.CanIngestUsage);
            Console.WriteLine("  " + capability.Limitations);
        }

        return 0;
    }

    private static int UnknownCommand()
    {
        Console.Error.WriteLine("Try: agentforge \"Describe the task\" or agentforge --help");
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            AgentForge TokenBudget v1.0 - one-command AI task preflight

            Commands:
              agentforge                               Enter a prompt interactively
              agentforge "Refactor the lighting engine"  Assess a task immediately
              agentforge setup                         Configure defaults once; no credentials stored
              agentforge preflight "Refactor the lighting engine"
              agentforge preflight <plan.md>           Read an existing prompt file
              agentforge preflight                     Enter a prompt interactively

            Advanced commands:
              tokenbudget analyze <plan.md> --model <model-id> [options]
              tokenbudget budget <plan.md> --provider copilot|openai|anthropic|codex|chatgpt
                  --model <model-id> --account <account-label> [billing options]
                  [--live-copilot true] [--improve-prompt true]
              tokenbudget copilot-probe --account <account-label> --model <model-id>
                  --allow-live-account-lookup true [--allowance <reported-credits> --used <reported-credits>]
              tokenbudget ingest-provider-session <snapshot.json> --db <history.db>
              tokenbudget provider-history --provider copilot|openai|anthropic|codex|chatgpt
                  --account <account-label> --model <id> --db <history.db>
              tokenbudget provider-calibration --provider copilot|openai|anthropic|codex|chatgpt
                  --account <account-label> --model <id> --db <history.db>
              tokenbudget capabilities
              tokenbudget inspect-codex <codex-exec.jsonl>
              tokenbudget ingest-codex <codex-exec.jsonl> --db <history.db> --profile <name> --model <id>
                  [--estimate-json <forecast.json>] [--run-id <unique-id>]
              tokenbudget history --db <history.db> --profile <name> --model <id>

            Options:
              --provider codex|codex-app-server|openai|openai-agents|work|anthropic
              --repo <local-path>                    Optional local checkout; metadata read-only
              --max-total-tokens <number>            Cumulative budget
              --max-peak-context-tokens <number>     Per-request context budget
              --model-context-limit <number>         Known model context-window limit
              --risk-tolerance <0.01..0.49>          Default 0.10 (P90)
              --samples <1000..100000>              Default 10000
              --allow-provider-upload true|false     Default false; sends plan text to direct model API
              --seed <integer>                       Default 1729
              --format markdown|json                 Default markdown
              --output <file>                        Write report instead of stdout

            Exit codes: 0 pass, 2 warning, 3 reject, 4 insufficient evidence, 1 error.
            Future agent usage is modeled from uncalibrated priors, never an exact measurement.
            Billing options: --allowance, --used, --price-input, --price-output,
              --price-cache, --price-batch, --max-task-units, --reserve-units,
              --risk-limit, --cache-fraction, --reset-utc, --format json|markdown.
            Account balances and manually entered model prices are user-reported snapshots.
            """);
    }
}
