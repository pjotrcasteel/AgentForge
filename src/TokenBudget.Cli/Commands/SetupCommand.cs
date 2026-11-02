using System.Globalization;
using TokenBudget.Billing;
using TokenBudget.Cli.Configuration;

namespace TokenBudget.Cli.Commands;

/// <summary>One-time interactive configuration with no API keys, repository files or prompt uploads.</summary>
internal static class SetupCommand
{
    public static Task<int> RunAsync() => RunAsync(AgentForgeSettingsStore.ForCurrentUser(), Console.In, Console.Out);

    internal static async Task<int> RunAsync(AgentForgeSettingsStore store, TextReader input, TextWriter output)
    {
        try
        {
            AgentForgeSettings previous = await store.LoadAsync();
            await output.WriteLineAsync("AgentForge setup (press Enter to keep a suggested value)");
            await output.WriteLineAsync("No passwords, API keys or prompt content are stored.");

            string providerText = await ReadAsync(input, output, "Provider (copilot/openai/anthropic/codex/chatgpt)",
                previous.Provider switch
                {
                    BillingProvider.GitHubCopilot => "copilot",
                    BillingProvider.OpenAiApi => "openai",
                    BillingProvider.AnthropicApi => "anthropic",
                    BillingProvider.CodexSubscription => "codex",
                    _ => "chatgpt"
                });
            BillingProvider provider = ProviderBudgetCommand.ParseProvider(providerText);
            string model = await ReadAsync(input, output, "Model ID (or unconfigured-model for token-only advice)",
                previous.ModelId);
            string account = await ReadAsync(input, output, "Account label", previous.AccountId);

            decimal? allowance = null;
            decimal? consumed = null;
            bool livePricing = false;
            DateTimeOffset? observed = null;
            if (provider == BillingProvider.GitHubCopilot)
            {
                allowance = await ReadWholeCreditsAsync(input, output, "Monthly AI-credit allowance",
                    previous.MonthlyAllowance);
                consumed = await ReadWholeCreditsAsync(input, output, "Already used AI credits",
                    previous.AlreadyUsed);
                livePricing = await ReadConsentAsync(input, output,
                    "Use the local Copilot SDK for current model prices on each preflight?",
                    previous.AllowLiveCopilotPricing);
                observed = allowance.HasValue || consumed.HasValue ? DateTimeOffset.UtcNow : null;
            }

            var settings = new AgentForgeSettings(provider, model, account, allowance, consumed, livePricing, observed);
            await store.SaveAsync(settings);
            await output.WriteLineAsync("Saved preferences to " + store.FilePath);
            await output.WriteLineAsync("Next: agentforge \"Refactor the lighting engine\"");
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or IOException or
            InvalidDataException or UnauthorizedAccessException)
        {
            await output.WriteLineAsync("AgentForge setup: " + exception.Message);
            return 1;
        }
    }

    private static async Task<string> ReadAsync(TextReader input, TextWriter output, string question, string defaultValue)
    {
        await output.WriteAsync($"{question} [{defaultValue}]: ");
        string? typed = await input.ReadLineAsync();
        return string.IsNullOrWhiteSpace(typed) ? defaultValue : typed.Trim();
    }

    private static async Task<decimal?> ReadWholeCreditsAsync(TextReader input, TextWriter output,
        string question, decimal? existing)
    {
        string fallback = existing?.ToString("0", CultureInfo.InvariantCulture) ?? "unknown";
        string entered = await ReadAsync(input, output, question + " (digits only; no thousands separators)", fallback);
        if (string.Equals(entered, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!long.TryParse(entered, NumberStyles.None, CultureInfo.InvariantCulture, out long credits) || credits < 0)
        {
            throw new ArgumentException("Enter nonnegative whole AI credits without separators, e.g. 30000.");
        }

        return credits;
    }

    private static async Task<bool> ReadConsentAsync(TextReader input, TextWriter output, string question, bool previous)
    {
        string answer = await ReadAsync(input, output, question + " (yes/no)", previous ? "yes" : "no");
        return answer.ToLowerInvariant() switch
        {
            "yes" or "y" => true,
            "no" or "n" => false,
            _ => throw new ArgumentException("Reply yes or no to the Copilot SDK lookup consent question.")
        };
    }
}
