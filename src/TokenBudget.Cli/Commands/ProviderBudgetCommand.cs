using System.Globalization;
using System.Text;
using System.Text.Json;
using GitHub.Copilot;
using TokenBudget.Application;
using TokenBudget.Billing;
using TokenBudget.Domain;
using TokenBudget.Provider.Copilot;

namespace TokenBudget.Cli.Commands;

/// <summary>Standalone provider-aware budget assessment; never executes the submitted task.</summary>
internal static class ProviderBudgetCommand
{
    private static readonly string[] SupportedOptions =
    [
        "provider", "account", "model", "repo", "allowance", "used", "remaining", "reset-utc",
        "price-input", "price-output", "price-cache", "price-batch", "extra-cost", "billing-mode",
        "max-task-units", "reserve-units", "risk-limit", "cache-fraction", "live-copilot",
        "improve-prompt", "max-total-tokens", "max-peak-context-tokens", "risk-tolerance",
        "seed", "samples", "format", "output", "allow-provider-upload", "max-data-age-hours"
    ];

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length < 2 || !File.Exists(args[1]))
            {
                throw new ArgumentException("Expected: tokenbudget budget <plan.md> --provider <name> --account <label> --model <id>.");
            }

            CliOptions options = CliOptions.Parse(args, 2, SupportedOptions);
            BillingProvider provider = ParseProvider(options.Require("provider"));
            string model = options.Require("model");
            var account = new ProviderAccount(provider, options.Require("account"));
            DateTimeOffset now = DateTimeOffset.UtcNow;
            BudgetUnit unit = ParseUnit(provider, options.Get("billing-mode"));
            BillingMode mode = ParseBillingMode(provider, options.Get("billing-mode"));
            ValidateBillingMode(provider, mode);

            var plan = new AnalyzePlanRequest(await File.ReadAllTextAsync(Path.GetFullPath(args[1])), model,
                ResolveTarget(provider), new BudgetConstraints(options.OptionalLong("max-total-tokens"),
                    options.OptionalLong("max-peak-context-tokens"), RiskTolerance: options.Double("risk-tolerance", 0.10)),
                RepositoryPath: options.Get("repo"), AllowProviderUpload: options.Boolean("allow-provider-upload"),
                RandomSeed: options.Integer("seed", 1729), SimulationCount: options.Integer("samples", 10_000));

            ModelPricing? configuredPricing = MakeConfiguredPricing(options, provider, mode, unit, model, now);
            BudgetSnapshot? configuredQuota = MakeConfiguredQuota(options, account, unit, now);
            ModelPricing? currentPricing = configuredPricing;
            BudgetSnapshot? currentQuota = configuredQuota;
            IReadOnlyList<ModelPricing> pricingCatalog = configuredPricing is null ? [] : [configuredPricing];

            if (options.Boolean("live-copilot"))
            {
                if (provider != BillingProvider.GitHubCopilot)
                {
                    throw new ArgumentException("--live-copilot is only supported for --provider copilot.");
                }

                await using var client = new CopilotClient();
                bool started = false;
                try
                {
                    await client.StartAsync();
                    started = true;
                    if (mode == BillingMode.MeteredAiCredits)
                    {
                        pricingCatalog = await new CopilotSdkPricingProvider(client).ListAsync();
                        currentPricing = pricingCatalog.FirstOrDefault(item =>
                            string.Equals(item.ModelId, model, StringComparison.OrdinalIgnoreCase)) ?? configuredPricing;
                    }
                    currentQuota ??= await new CopilotSdkQuotaProvider(client).GetBudgetAsync(account, unit);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    Console.Error.WriteLine("Copilot SDK billing unavailable (" + exception.GetType().Name +
                        "); retaining explicitly configured inputs or unknown values.");
                }
                finally
                {
                    if (started)
                    {
                        try
                        {
                            await client.StopAsync();
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException)
                        {
                            Console.Error.WriteLine("Copilot SDK shutdown warning (" + exception.GetType().Name + ").");
                        }
                    }
                }
            }

            var assessment = new ProviderBudgetAssessmentService(AnalyzePlanService.CreateDefault(),
                new ConfiguredPricingProvider(provider, pricingCatalog.Concat(
                    currentPricing is null ? [] : [currentPricing]).DistinctBy(item => item.ModelId).ToArray()),
                new ConfiguredBudgetAdapter(provider, currentQuota is null ? [] : [currentQuota]),
                new BudgetPolicyEvaluator(), new PromptScopeAdvisor());
            var policy = new ProviderBudgetPolicy(options.OptionalDecimal("max-task-units"),
                options.Decimal("reserve-units", 0), options.OptionalDecimal("risk-limit"),
                TimeSpan.FromHours(options.Double("max-data-age-hours", 24)));
            ProviderBudgetAssessment report = await assessment.AnalyzeAsync(new ProviderBudgetRequest(
                plan, account, policy, new WorkloadAssumptions(options.Decimal("cache-fraction", 0)),
                options.Boolean("improve-prompt")));

            string content = options.Get("format")?.ToLowerInvariant() switch
            {
                "json" => JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }),
                null or "markdown" or "md" => FormatMarkdown(report),
                _ => throw new ArgumentException("--format must be json or markdown.")
            };
            if (options.Get("output") is { } path)
            {
                await File.WriteAllTextAsync(Path.GetFullPath(path), content);
                Console.WriteLine("Report written to " + Path.GetFullPath(path));
            }
            else
            {
                Console.WriteLine(content);
            }

            return report.Billing.Decision switch
            {
                ProviderBudgetDecision.Execute => 0,
                ProviderBudgetDecision.NarrowScope or ProviderBudgetDecision.ReserveBudget => 2,
                ProviderBudgetDecision.Defer => 3,
                ProviderBudgetDecision.InsufficientEvidence => 4,
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

    private static ModelPricing? MakeConfiguredPricing(CliOptions options, BillingProvider provider, BillingMode mode,
        BudgetUnit unit, string model, DateTimeOffset now)
    {
        if (options.OptionalDecimal("price-input") is not { } input ||
            options.OptionalDecimal("price-output") is not { } output)
        {
            return null;
        }

        long batch = options.OptionalLong("price-batch") ??
            throw new ArgumentException("--price-batch is required when --price-input/--price-output are configured.");
        return new ModelPricing(provider, model, mode, unit, input, output,
            options.OptionalDecimal("price-cache"), batch, now, "user-configured model pricing snapshot",
            options.Decimal("extra-cost", 0));
    }

    private static BudgetSnapshot? MakeConfiguredQuota(CliOptions options, ProviderAccount account,
        BudgetUnit unit, DateTimeOffset now)
    {
        if (!options.Contains("allowance") && !options.Contains("used") && !options.Contains("remaining"))
        {
            return null;
        }

        DateTimeOffset? reset = options.Get("reset-utc") is { } text
            ? DateTimeOffset.Parse(text, CultureInfo.InvariantCulture) : null;
        return new BudgetSnapshot(account, unit, options.OptionalDecimal("allowance"), options.OptionalDecimal("used"),
            options.OptionalDecimal("remaining"), now, reset, "explicitly configured account-reported quota");
    }

    internal static BillingProvider ParseProvider(string text) => text.ToLowerInvariant() switch
    {
        "copilot" or "github-copilot" => BillingProvider.GitHubCopilot,
        "openai" => BillingProvider.OpenAiApi,
        "anthropic" => BillingProvider.AnthropicApi,
        "codex" => BillingProvider.CodexSubscription,
        "chatgpt" or "work" => BillingProvider.ChatGptSubscription,
        _ => throw new ArgumentException("Unknown billing provider.")
    };

    private static BillingMode ParseBillingMode(BillingProvider provider, string? text) => text?.ToLowerInvariant() switch
    {
        "metered" => BillingMode.MeteredAiCredits,
        "legacy" => BillingMode.LegacyPremiumRequests,
        "api" => BillingMode.ApiUsd,
        "subscription" => BillingMode.SubscriptionQuota,
        "advisory" => BillingMode.Advisory,
        null => provider switch
        {
            BillingProvider.GitHubCopilot => BillingMode.MeteredAiCredits,
            BillingProvider.OpenAiApi or BillingProvider.AnthropicApi => BillingMode.ApiUsd,
            _ => BillingMode.Advisory
        },
        _ => throw new ArgumentException("Unknown billing mode.")
    };

    private static BudgetUnit ParseUnit(BillingProvider provider, string? mode) => ParseBillingMode(provider, mode) switch
    {
        BillingMode.MeteredAiCredits => BudgetUnit.GitHubAiCredits,
        BillingMode.LegacyPremiumRequests => BudgetUnit.PremiumInteractions,
        BillingMode.ApiUsd => BudgetUnit.Usd,
        _ => BudgetUnit.ProviderQuotaUnits
    };

    private static void ValidateBillingMode(BillingProvider provider, BillingMode mode)
    {
        bool allowed = provider switch
        {
            BillingProvider.GitHubCopilot => mode is BillingMode.MeteredAiCredits or BillingMode.LegacyPremiumRequests or BillingMode.Advisory,
            BillingProvider.OpenAiApi or BillingProvider.AnthropicApi => mode is BillingMode.ApiUsd or BillingMode.Advisory,
            _ => mode is BillingMode.SubscriptionQuota or BillingMode.Advisory
        };
        if (!allowed)
        {
            throw new ArgumentException("Billing mode is not valid for this provider. Credits cannot be transferred across providers.");
        }
    }

    internal static ExecutionTarget ResolveTarget(BillingProvider provider) => provider switch
    {
        BillingProvider.GitHubCopilot => ExecutionTarget.GitHubCopilot,
        BillingProvider.OpenAiApi => ExecutionTarget.OpenAiResponses,
        BillingProvider.AnthropicApi => ExecutionTarget.AnthropicMessages,
        BillingProvider.CodexSubscription => ExecutionTarget.CodexCli,
        BillingProvider.ChatGptSubscription => ExecutionTarget.ChatGptWork,
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    private static string FormatMarkdown(ProviderBudgetAssessment report)
    {
        ProviderCostForecast bill = report.Billing;
        var result = new StringBuilder();
        result.AppendLine("# AgentForge provider-aware budget preflight");
        result.AppendLine($"Account: {bill.Account.Provider}/{bill.Account.AccountId} | Model: {bill.ModelId} | Mode: {bill.BillingMode}");
        result.AppendLine($"Decision: **{bill.Decision}** | Token confidence: {report.Tokens.Confidence}");
        result.AppendLine($"Cumulative tokens P10/P50/P90: {report.Tokens.CumulativeTokens.P10:N0} / " +
            $"{report.Tokens.CumulativeTokens.P50:N0} / {report.Tokens.CumulativeTokens.P90:N0}");
        if (bill.TokenForecast is { } t)
        {
            result.AppendLine($"Input tokens P10/P50/P90: {t.InputTokens.P10:N0} / {t.InputTokens.P50:N0} / {t.InputTokens.P90:N0}");
            result.AppendLine($"Output tokens P10/P50/P90: {t.OutputTokens.P10:N0} / {t.OutputTokens.P50:N0} / {t.OutputTokens.P90:N0}");
        }

        result.AppendLine($"Forecast unit: {bill.Unit}");
        result.AppendLine(bill.Costs is { } costs
            ? $"Cost P10/P50/P90: {costs.P10:N4} / {costs.P50:N4} / {costs.P90:N4}"
            : "Cost P10/P50/P90: unavailable (not zero)");
        result.AppendLine("Allowance remaining before: " + (bill.RemainingBefore?.ToString("N4", CultureInfo.InvariantCulture) ?? "unknown"));
        result.AppendLine("Allowance remaining after P50/P90: " +
            (bill.RemainingAfterP50?.ToString("N4", CultureInfo.InvariantCulture) ?? "unknown") + " / " +
            (bill.RemainingAfterP90?.ToString("N4", CultureInfo.InvariantCulture) ?? "unknown"));
        result.AppendLine("P90 share of remaining: " + (bill.P90FractionOfRemaining?.ToString("P2", CultureInfo.InvariantCulture) ?? "unknown"));
        result.AppendLine("Reset: " + (bill.ResetsAt?.ToString("O", CultureInfo.InvariantCulture) ?? "unknown"));
        result.AppendLine("P50 metered USD value (not necessarily an invoice): " +
            (bill.MeteredValueUsd?.ToString("N4", CultureInfo.InvariantCulture) ?? "unknown"));
        result.AppendLine("Pricing: " + bill.PricingSource + " | Quota: " + bill.QuotaSource);
        result.AppendLine("Prompt too broad: " + report.Prompt.TooBroad);
        foreach (string warning in bill.Warnings.Concat(report.Prompt.Notes))
        {
            result.AppendLine("- Warning: " + warning);
        }

        foreach (string suggestion in bill.Recommendations)
        {
            result.AppendLine("- Recommendation: " + suggestion);
        }

        foreach (ModelCostAlternative alternative in report.Alternatives)
        {
            result.AppendLine($"- Alternative model: {alternative.ModelId}, P90 {alternative.P90Cost:N4} {bill.Unit} " +
                $"(~{alternative.SavingsPercent:N1}% lower estimated price). {alternative.SuitabilityCaveat}");
        }

        if (report.Prompt.ImprovedPrompt is { } improved)
        {
            result.AppendLine("\n## Requirement-preserving improved prompt\n");
            result.AppendLine(improved);
        }

        result.AppendLine("Forecasts are uncalibrated scenarios, not guaranteed coverage or billed amounts.");
        return result.ToString();
    }
}
