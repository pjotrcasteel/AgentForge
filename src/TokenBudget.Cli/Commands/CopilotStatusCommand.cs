using TokenBudget.Billing;
using TokenBudget.Cli.Configuration;

namespace TokenBudget.Cli.Commands;

/// <summary>Checks live Copilot account signals only after an interactive, per-invocation confirmation.</summary>
internal static class CopilotStatusCommand
{
    public static Task<int> RunAsync(string[] args)
    {
        if (args.Length != 2 || !string.Equals(args[1], "status", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Use: agentforge copilot status (uses saved setup; asks before contacting GitHub).");
            return Task.FromResult(1);
        }

        return RunAsync(AgentForgeSettingsStore.ForCurrentUser(), Console.In, Console.Out,
            CopilotProbeCommand.RunLiveAsync);
    }

    internal static async Task<int> RunAsync(AgentForgeSettingsStore store, TextReader input, TextWriter output,
        Func<ProviderAccount, string, BudgetSnapshot?, Task<int>> runProbe)
    {
        ArgumentNullException.ThrowIfNull(runProbe);
        try
        {
            AgentForgeSettings settings = await store.LoadAsync();
            if (settings.Provider != BillingProvider.GitHubCopilot || settings.ModelId == "unconfigured-model")
            {
                await output.WriteLineAsync("Choose a GitHub Copilot model in agentforge setup before checking account status.");
                return 2;
            }

            await output.WriteLineAsync("Copilot account status — read-only SDK lookup, no task or prompt submitted.");
            await output.WriteLineAsync("Configured account label: " + settings.AccountId + " (not an authenticated identity).");
            await output.WriteLineAsync("Configured model: " + settings.ModelId);
            await output.WriteAsync("Query current model pricing and account quotas through the Copilot SDK? (yes/no): ");
            string? response = await input.ReadLineAsync();
            if (response?.Trim().ToLowerInvariant() is not ("yes" or "y"))
            {
                await output.WriteLineAsync("Cancelled. No SDK call made.");
                return 2;
            }

            BudgetSnapshot? reported = CreateReportedBalance(settings);
            return await runProbe(new ProviderAccount(BillingProvider.GitHubCopilot, settings.AccountId),
                settings.ModelId, reported);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or IOException or
            UnauthorizedAccessException or InvalidOperationException)
        {
            await output.WriteLineAsync("AgentForge Copilot status: " + exception.Message);
            return 1;
        }
    }

    private static BudgetSnapshot? CreateReportedBalance(AgentForgeSettings settings)
    {
        if (settings.MonthlyAllowance is null && settings.AlreadyUsed is null)
        {
            return null;
        }

        // An absent timestamp means old/unknown, never freshly verified just because this command was invoked.
        return new BudgetSnapshot(new ProviderAccount(BillingProvider.GitHubCopilot, settings.AccountId),
            BudgetUnit.GitHubAiCredits, settings.MonthlyAllowance, settings.AlreadyUsed, null,
            settings.BalanceObservedAt ?? DateTimeOffset.UnixEpoch, null,
            "saved account-reported AI credits (not SDK-verified)");
    }
}
