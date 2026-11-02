using TokenBudget.Billing;

namespace TokenBudget.Provider.Copilot;

/// <summary>
/// Inspects independent Copilot pricing, AI-credit and premium-interaction signals.
/// The adapters are injected; this service does not start an SDK client or run any AI task.
/// </summary>
public sealed class CopilotBillingProbeService(
    IProviderPricingProvider pricingProvider,
    IProviderBudgetAdapter budgetAdapter)
{
    private static readonly TimeSpan MaximumSnapshotAge = TimeSpan.FromHours(24);

    /// <summary>Collect as much supported evidence as possible without turning missing values into zero.</summary>
    public async Task<CopilotBillingProbeReport> ProbeAsync(ProviderAccount account, string modelId,
        DateTimeOffset now, BudgetSnapshot? accountReportedCredits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (account.Provider != BillingProvider.GitHubCopilot || string.IsNullOrWhiteSpace(account.AccountId) ||
            string.IsNullOrWhiteSpace(modelId) || now == default)
        {
            throw new ArgumentException("A Copilot account label, model identifier and observation time are required.");
        }

        if (pricingProvider.Provider != BillingProvider.GitHubCopilot || budgetAdapter.Provider != BillingProvider.GitHubCopilot)
        {
            throw new ArgumentException("The injected pricing and quota adapters must be GitHub Copilot adapters.");
        }

        if (accountReportedCredits is not null)
        {
            ValidateSnapshot(accountReportedCredits, account, BudgetUnit.GitHubAiCredits);
        }

        var warnings = new List<string>
        {
            "The account label is user-supplied; this probe cannot verify that it matches the SDK's signed-in principal."
        };

        (ModelPricing? price, CopilotProbeState priceState) = await ReadPriceAsync(modelId, now, warnings,
            cancellationToken);
        (BudgetSnapshot? credits, CopilotProbeState creditsState) = await ReadQuotaAsync(account,
            BudgetUnit.GitHubAiCredits, now, warnings, cancellationToken);
        (BudgetSnapshot? premium, CopilotProbeState premiumState) = await ReadQuotaAsync(account,
            BudgetUnit.PremiumInteractions, now, warnings, cancellationToken);

        if (creditsState != CopilotProbeState.Available && accountReportedCredits is not null)
        {
            if (accountReportedCredits.IsStale(now, MaximumSnapshotAge))
            {
                warnings.Add("The explicitly account-reported AI-credit balance is stale; do not use it for a spending decision.");
                credits = accountReportedCredits;
                creditsState = CopilotProbeState.Partial;
            }
            else if (!accountReportedCredits.Unlimited && accountReportedCredits.Remaining is null)
            {
                warnings.Add("The account-reported AI-credit balance has no known remaining allowance.");
                credits = accountReportedCredits;
                creditsState = CopilotProbeState.Partial;
            }
            else
            {
                credits = accountReportedCredits;
                creditsState = CopilotProbeState.AccountReported;
            }
        }

        if (creditsState != CopilotProbeState.Available)
        {
            warnings.Add("No verified live AI-credit balance is available; premium-interaction quota is a separate unit.");
        }

        return new CopilotBillingProbeReport(account, modelId, now, priceState, price, creditsState,
            credits, premiumState, premium, AuthenticatedAccountIdentityVerified: false, warnings);
    }

    private async Task<(ModelPricing?, CopilotProbeState)> ReadPriceAsync(string modelId, DateTimeOffset now,
        List<string> warnings, CancellationToken cancellationToken)
    {
        try
        {
            ModelPricing? pricing = await pricingProvider.GetPricingAsync(modelId, cancellationToken);
            if (pricing is null)
            {
                warnings.Add("The requested model has no current SDK token-pricing entry.");
                return (null, CopilotProbeState.Unavailable);
            }

            if (pricing.Provider != BillingProvider.GitHubCopilot ||
                !string.Equals(pricing.ModelId, modelId, StringComparison.OrdinalIgnoreCase) ||
                pricing.Unit != BudgetUnit.GitHubAiCredits)
            {
                throw new InvalidOperationException("The returned pricing has a mismatched provider, model or unit.");
            }

            bool incomplete = pricing.BatchTokens <= 0 || pricing.InputPerBatch is null ||
                pricing.OutputPerBatch is null || pricing.InputPerBatch < 0 || pricing.OutputPerBatch < 0 ||
                pricing.ObservedAt > now || now - pricing.ObservedAt > MaximumSnapshotAge;
            if (incomplete)
            {
                warnings.Add("Model pricing is incomplete or stale; no exact cost forecast should rely on it.");
            }

            return (pricing, incomplete ? CopilotProbeState.Partial : CopilotProbeState.Available);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            warnings.Add("Copilot model-pricing lookup failed (" + exception.GetType().Name + ").");
            return (null, CopilotProbeState.Failed);
        }
    }

    private async Task<(BudgetSnapshot?, CopilotProbeState)> ReadQuotaAsync(ProviderAccount account, BudgetUnit unit,
        DateTimeOffset now, List<string> warnings, CancellationToken cancellationToken)
    {
        try
        {
            BudgetSnapshot? snapshot = await budgetAdapter.GetBudgetAsync(account, unit, cancellationToken);
            if (snapshot is null)
            {
                return (null, CopilotProbeState.Unavailable);
            }

            ValidateSnapshot(snapshot, account, unit);
            if (snapshot.IsStale(now, MaximumSnapshotAge))
            {
                warnings.Add($"The {unit} quota snapshot is stale and cannot establish a current balance.");
                return (snapshot, CopilotProbeState.Partial);
            }

            bool incomplete = !snapshot.Unlimited && snapshot.Remaining is null;
            if (incomplete)
            {
                warnings.Add($"The {unit} quota exists but its remaining allowance is unavailable.");
            }

            return (snapshot, incomplete ? CopilotProbeState.Partial : CopilotProbeState.Available);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            warnings.Add("Copilot " + unit + " quota lookup failed (" + exception.GetType().Name + ").");
            return (null, CopilotProbeState.Failed);
        }
    }

    private static void ValidateSnapshot(BudgetSnapshot snapshot, ProviderAccount account, BudgetUnit unit)
    {
        if (snapshot.Account != account || snapshot.Unit != unit || snapshot.Allowance is < 0 ||
            snapshot.Consumed is < 0 || snapshot.ReportedRemaining is < 0 || snapshot.ObservedAt == default)
        {
            throw new ArgumentException("A quota cannot be transferred between accounts or units, or contain negative quantities.");
        }
    }
}
