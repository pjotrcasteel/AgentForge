using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GitHub.Copilot;
using TokenBudget.Billing;
using TokenBudget.Provider.Copilot;

namespace TokenBudget.Cli.Commands;

/// <summary>Explicit, read-only SDK pricing and quota lookup; does not create sessions or execute prompts.</summary>
internal static class CopilotProbeCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            CliOptions options = CliOptions.Parse(args, 1,
                "account", "model", "allow-live-account-lookup", "allowance", "used", "remaining", "reset-utc");
            string accountId = options.Require("account");
            string modelId = options.Require("model");
            if (!options.Boolean("allow-live-account-lookup"))
            {
                throw new ArgumentException("Explicit --allow-live-account-lookup true is required before SDK account queries.");
            }

            var account = new ProviderAccount(BillingProvider.GitHubCopilot, accountId);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            BudgetSnapshot? reported = ParseAccountReportedBalance(options, account, now);

            await using var client = new CopilotClient();
            bool started = false;
            try
            {
                await client.StartAsync();
                started = true;
                var probe = new CopilotBillingProbeService(
                    new CopilotSdkPricingProvider(client), new CopilotSdkQuotaProvider(client));
                CopilotBillingProbeReport report = await probe.ProbeAsync(account, modelId, now, reported);
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                bool noPricing = report.ModelPriceState is CopilotProbeState.Failed or CopilotProbeState.Unavailable;
                bool noQuota = report.PremiumQuotaState is CopilotProbeState.Failed or CopilotProbeState.Unavailable;
                return noPricing && noQuota ? 4 : 0;
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
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or
            OverflowException or FormatException)
        {
            Console.Error.WriteLine("TokenBudget: " + exception.Message);
            return 1;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Console.Error.WriteLine("Copilot SDK startup unavailable (" + exception.GetType().Name + "). " +
                "No AI task was executed; retain configured balances as account-reported, not verified.");
            return 4;
        }
    }

    private static BudgetSnapshot? ParseAccountReportedBalance(CliOptions options, ProviderAccount account,
        DateTimeOffset now)
    {
        bool supplied = options.Contains("allowance") || options.Contains("used") || options.Contains("remaining");
        if (!supplied)
        {
            if (options.Contains("reset-utc"))
            {
                throw new ArgumentException("--reset-utc requires an account-reported AI-credit allowance.");
            }

            return null;
        }

        DateTimeOffset? reset = options.Get("reset-utc") is { } resetText
            ? DateTimeOffset.Parse(resetText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            : null;
        return new BudgetSnapshot(account, BudgetUnit.GitHubAiCredits, options.OptionalDecimal("allowance"),
            options.OptionalDecimal("used"), options.OptionalDecimal("remaining"), now, reset,
            "explicitly account-reported AI-credit snapshot");
    }
}
