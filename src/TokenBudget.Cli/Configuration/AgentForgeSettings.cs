using TokenBudget.Billing;

namespace TokenBudget.Cli.Configuration;

/// <summary>User-scoped, non-secret defaults for a one-argument AgentForge preflight.</summary>
internal sealed record AgentForgeSettings(
    BillingProvider Provider,
    string ModelId,
    string AccountId,
    decimal? MonthlyAllowance = null,
    decimal? AlreadyUsed = null,
    bool AllowLiveCopilotPricing = false,
    DateTimeOffset? BalanceObservedAt = null)
{
    public static AgentForgeSettings Default => new(BillingProvider.GitHubCopilot, "unconfigured-model", "personal");

    public void Validate()
    {
        if (!Enum.IsDefined(Provider) || string.IsNullOrWhiteSpace(ModelId) || ModelId.Length > 128 ||
            string.IsNullOrWhiteSpace(AccountId) || AccountId.Length > 128 || MonthlyAllowance is < 0 || AlreadyUsed is < 0 ||
            (AllowLiveCopilotPricing && Provider != BillingProvider.GitHubCopilot))
        {
            throw new ArgumentException("Invalid AgentForge settings. Use 'agentforge setup' to correct them.");
        }
    }
}
