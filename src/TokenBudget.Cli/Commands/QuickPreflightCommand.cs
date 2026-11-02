using System.Globalization;
using System.Text.RegularExpressions;
using GitHub.Copilot;
using TokenBudget.Application;
using TokenBudget.Billing;
using TokenBudget.Cli.Configuration;
using TokenBudget.Domain;
using TokenBudget.Provider.Copilot;

namespace TokenBudget.Cli.Commands;

/// <summary>One-prompt, offline-by-default preflight using previously configured non-secret preferences.</summary>
internal static partial class QuickPreflightCommand
{
    public static Task<int> RunAsync(string[] args) => RunAsync(args,
        AgentForgeSettingsStore.ForCurrentUser(), Console.In, Console.Out, Directory.GetCurrentDirectory());

    internal static async Task<int> RunAsync(string[] args, AgentForgeSettingsStore store,
        TextReader input, TextWriter output, string workingDirectory, bool allowFileInput = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string prompt = await ReadPromptAsync(args, input, output, allowFileInput);
            AgentForgeSettings settings = await store.LoadAsync(cancellationToken);
            var account = new ProviderAccount(settings.Provider, settings.AccountId);
            ModelPricing? price = null;
            string? pricingWarning = null;

            if (settings.Provider == BillingProvider.GitHubCopilot && settings.AllowLiveCopilotPricing &&
                settings.ModelId != "unconfigured-model")
            {
                (price, pricingWarning) = await TryReadCopilotPriceAsync(settings.ModelId);
            }

            BudgetSnapshot? quota = MakeAccountReportedBalance(account, settings);
            string? repository = HasGitMetadata(workingDirectory) ? workingDirectory : null;
            var request = new AnalyzePlanRequest(prompt, settings.ModelId,
                ProviderBudgetCommand.ResolveTarget(settings.Provider), new BudgetConstraints(),
                RepositoryPath: repository, AllowProviderUpload: false);
            var service = new ProviderBudgetAssessmentService(AnalyzePlanService.CreateDefault(),
                new ConfiguredPricingProvider(settings.Provider, price is null ? [] : [price]),
                new ConfiguredBudgetAdapter(settings.Provider, quota is null ? [] : [quota]),
                new BudgetPolicyEvaluator(), new PromptScopeAdvisor());
            var assessment = await service.AnalyzeAsync(new ProviderBudgetRequest(request, account,
                new ProviderBudgetPolicy(MaxAtRiskFraction: 0.5m), new WorkloadAssumptions(), ImprovePrompt: true),
                cancellationToken);

            bool underspecified = IsUnderspecified(prompt, assessment.Prompt);
            await WriteReportAsync(output, assessment, underspecified, pricingWarning);
            if (underspecified)
            {
                return 2;
            }

            return assessment.Billing.Decision switch
            {
                ProviderBudgetDecision.Execute => 0,
                ProviderBudgetDecision.NarrowScope or ProviderBudgetDecision.ReserveBudget => 2,
                ProviderBudgetDecision.Defer => 3,
                ProviderBudgetDecision.InsufficientEvidence => 4,
                _ => 1
            };
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidDataException or
            UnauthorizedAccessException or FormatException or OverflowException)
        {
            await output.WriteLineAsync("AgentForge: " + exception.Message);
            return 1;
        }
    }

    private static async Task<string> ReadPromptAsync(string[] args, TextReader input, TextWriter output,
        bool allowFileInput)
    {
        string? candidate;
        if (args.Length >= 2)
        {
            candidate = string.Join(' ', args.Skip(1));
        }
        else
        {
            await output.WriteAsync("What should the AI do? ");
            candidate = await input.ReadLineAsync();
        }

        if (string.IsNullOrWhiteSpace(candidate) || candidate.StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException("Provide a task: agentforge preflight \"Refactor the lighting engine\".");
        }

        if (allowFileInput && File.Exists(candidate))
        {
            if (new FileInfo(candidate).Length > 1024 * 1024)
            {
                throw new ArgumentException("The plan file must be 1 MiB or smaller.");
            }

            candidate = await File.ReadAllTextAsync(candidate);
        }
        else if (allowFileInput && candidate.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
            !candidate.Contains('\n') && !candidate.Contains('\r') && !candidate.Contains(' '))
        {
            throw new ArgumentException("The Markdown plan file was not found: " + candidate);
        }

        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > 64_000)
        {
            throw new ArgumentException("The prompt must contain 1 to 64,000 characters.");
        }

        return candidate;
    }

    private static BudgetSnapshot? MakeAccountReportedBalance(ProviderAccount account, AgentForgeSettings settings)
    {
        if (settings.Provider != BillingProvider.GitHubCopilot ||
            (settings.MonthlyAllowance is null && settings.AlreadyUsed is null))
        {
            return null;
        }

        return new BudgetSnapshot(account, BudgetUnit.GitHubAiCredits, settings.MonthlyAllowance,
            settings.AlreadyUsed, null, settings.BalanceObservedAt ?? DateTimeOffset.MinValue,
            null, "user-configured AI-credit allowance (not SDK-verified)");
    }

    private static bool HasGitMetadata(string directory) => Directory.Exists(Path.Combine(directory, ".git")) ||
        File.Exists(Path.Combine(directory, ".git"));

    private static bool IsUnderspecified(string prompt, PromptScopeAssessment assessment)
    {
        bool largeChange = BroadChangeVerb().IsMatch(prompt);
        bool hasFile = FileMention().IsMatch(prompt);
        bool hasAcceptance = AcceptanceBoundary().IsMatch(prompt) || assessment.MentionsTesting;
        return largeChange && !hasFile && !hasAcceptance;
    }

    private static async Task WriteReportAsync(TextWriter output, ProviderBudgetAssessment assessment,
        bool underspecified, string? pricingWarning)
    {
        ProviderCostForecast budget = assessment.Billing;
        await output.WriteLineAsync("\nAgentForge preflight — no task executed");
        await output.WriteLineAsync("Provider / model: " + budget.Account.Provider + " / " + budget.ModelId);
        await output.WriteLineAsync("Tokens P10 / P50 / P90: " + assessment.Tokens.CumulativeTokens.P10.ToString("N0") +
            " / " + assessment.Tokens.CumulativeTokens.P50.ToString("N0") +
            " / " + assessment.Tokens.CumulativeTokens.P90.ToString("N0"));
        await output.WriteLineAsync("Provider cost P10 / P50 / P90: " + FormatCosts(budget));
        await output.WriteLineAsync("Remaining " + budget.Unit + ": " +
            (budget.RemainingBefore?.ToString("N2", CultureInfo.InvariantCulture) ?? "unknown"));
        await output.WriteLineAsync("Budget decision: " + budget.Decision);
        await output.WriteLineAsync("Prompt scope: " +
            (underspecified || assessment.Prompt.TooBroad ? "Needs narrowing" : "No broad-scope warning detected"));

        if (underspecified)
        {
            await output.WriteLineAsync("Reason: broad change requested without a file boundary or testable acceptance criteria.");
            await output.WriteLineAsync("Recommended first step: identify affected files and dependencies, then agree on one bounded milestone.");
        }

        if (pricingWarning is not null)
        {
            await output.WriteLineAsync("Pricing: " + pricingWarning);
        }

        foreach (string warning in budget.Warnings.Concat(assessment.Prompt.Notes).Take(8))
        {
            await output.WriteLineAsync("- " + warning);
        }

        if (assessment.Prompt.ImprovedPrompt is { } improved)
        {
            await output.WriteLineAsync("\nSuggested executable prompt (original requirements preserved):\n");
            await output.WriteLineAsync(improved);
        }
    }

    private static string FormatCosts(ProviderCostForecast budget)
    {
        if (budget.Costs is not { } costs)
        {
            return "unknown (current model prices unavailable)";
        }

        return $"{costs.P10:N2} / {costs.P50:N2} / {costs.P90:N2} {budget.Unit}";
    }

    private static async Task<(ModelPricing? Price, string? Warning)> TryReadCopilotPriceAsync(string modelId)
    {
        await using var client = new CopilotClient();
        bool started = false;
        try
        {
            await client.StartAsync();
            started = true;
            ModelPricing? price = await new CopilotSdkPricingProvider(client).GetPricingAsync(modelId);
            return price is null ? (null, "SDK has no price for the configured model.") : (price, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (null, "Copilot SDK pricing unavailable (" + exception.GetType().Name + ").");
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
                    // The engine remains read-only; shutdown failure cannot upgrade a decision.
                    _ = exception.GetType();
                }
            }
        }
    }

    [GeneratedRegex(@"\b(refactor|rewrite|overhaul|improve|optimize|redesign)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BroadChangeVerb();

    [GeneratedRegex(@"\.(cs|tsx?|jsx?|py|json|md|yaml|yml)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileMention();

    [GeneratedRegex(@"\b(acceptance|accept when|done when|must pass|verify|test|tests|specification)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AcceptanceBoundary();
}
