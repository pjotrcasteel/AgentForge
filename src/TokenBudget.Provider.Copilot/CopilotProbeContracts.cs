using TokenBudget.Billing;

namespace TokenBudget.Provider.Copilot;

/// <summary>Availability and provenance for each independent Copilot billing signal.</summary>
public enum CopilotProbeState
{
    Available,
    Partial,
    AccountReported,
    Unavailable,
    Failed
}

/// <summary>
/// Read-only account and pricing diagnostics. The account ID is an operator-supplied label,
/// not an identity verified against the Copilot SDK's authenticated principal.
/// </summary>
public sealed record CopilotBillingProbeReport(
    ProviderAccount Account,
    string ModelId,
    DateTimeOffset ObservedAt,
    CopilotProbeState ModelPriceState,
    ModelPricing? Pricing,
    CopilotProbeState CreditBalanceState,
    BudgetSnapshot? CreditBalance,
    CopilotProbeState PremiumQuotaState,
    BudgetSnapshot? PremiumInteractionQuota,
    bool AuthenticatedAccountIdentityVerified,
    IReadOnlyList<string> Warnings);
