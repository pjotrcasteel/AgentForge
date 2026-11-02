using TokenBudget.Domain;

namespace TokenBudget.Billing;

/// <summary>Usage accounting systems never share balances, even if models have matching names.</summary>
public enum BillingProvider { GitHubCopilot, OpenAiApi, AnthropicApi, CodexSubscription, ChatGptSubscription }

/// <summary>Billing rules are account- and plan-specific.</summary>
public enum BillingMode { MeteredAiCredits, LegacyPremiumRequests, ApiUsd, SubscriptionQuota, Advisory }

/// <summary>Units are explicitly tagged to prevent cross-provider or cross-unit arithmetic.</summary>
public enum BudgetUnit { Tokens, GitHubAiCredits, Usd, PremiumInteractions, ProviderQuotaUnits }

/// <summary>Stable account-scoped identity; IDs are labels, never credentials.</summary>
public sealed record ProviderAccount(BillingProvider Provider, string AccountId);

/// <summary>Nullable account entitlement, measured in the declared unit only.</summary>
public sealed record BudgetSnapshot(
    ProviderAccount Account,
    BudgetUnit Unit,
    decimal? Allowance,
    decimal? Consumed,
    decimal? ReportedRemaining,
    DateTimeOffset ObservedAt,
    DateTimeOffset? ResetsAt,
    string Source,
    bool Unlimited = false)
{
    /// <summary>Unknown stays unknown; an explicitly reported zero is a real zero.</summary>
    public decimal? Remaining => Unlimited ? null : ReportedRemaining ??
        (Allowance.HasValue && Consumed.HasValue ? Math.Max(0, Allowance.Value - Consumed.Value) : null);

    /// <summary>Quota snapshots may be stale independently of the pricing catalog.</summary>
    public bool IsStale(DateTimeOffset now, TimeSpan maxAge) => ObservedAt > now || now - ObservedAt > maxAge ||
        (ResetsAt.HasValue && ResetsAt.Value <= now);
}

/// <summary>Prices from a runtime catalog or an explicitly supplied snapshot; never a built-in model tariff.</summary>
public sealed record ModelPricing(
    BillingProvider Provider,
    string ModelId,
    BillingMode Mode,
    BudgetUnit Unit,
    decimal? InputPerBatch,
    decimal? OutputPerBatch,
    decimal? CachedInputPerBatch,
    long BatchTokens,
    DateTimeOffset ObservedAt,
    string Source,
    decimal AdditionalFixedCost = 0);

/// <summary>Limits applicable only to this task and provider account.</summary>
public sealed record ProviderBudgetPolicy(decimal? MaxTaskUnits = null, decimal ReservedUnits = 0,
    decimal? MaxAtRiskFraction = null, TimeSpan? MaxDataAge = null);

/// <summary>Optional share of predicted input tokens expected to hit the prompt cache, not a measured quantity.</summary>
public sealed record WorkloadAssumptions(decimal AssumedCachedInputFraction = 0);

/// <summary>Independent scenario costs, in the unit of their own model pricing.</summary>
public sealed record CostRange(decimal P10, decimal P50, decimal P90);

/// <summary>Deterministic outcome; reviewer prose is never an input.</summary>
public enum ProviderBudgetDecision { Execute, NarrowScope, ReserveBudget, Defer, InsufficientEvidence }

/// <summary>Provider-specific forecast, with unknown and stale data preserved.</summary>
public sealed record ProviderCostForecast(
    ProviderAccount Account,
    string ModelId,
    BillingMode BillingMode,
    BudgetUnit Unit,
    TokenUsageForecast? TokenForecast,
    CostRange? Costs,
    decimal? MeteredValueUsd,
    decimal? RemainingBefore,
    decimal? RemainingAfterP50,
    decimal? RemainingAfterP90,
    decimal? P90FractionOfRemaining,
    DateTimeOffset? ResetsAt,
    bool PricingStale,
    bool QuotaStale,
    ProviderBudgetDecision Decision,
    bool RequiresHumanApproval,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Recommendations,
    string PricingSource,
    string QuotaSource);

/// <summary>Billing provider adapter, separate from provider-neutral token estimation.</summary>
public interface IProviderBudgetAdapter
{
    BillingProvider Provider { get; }

    Task<BudgetSnapshot?> GetBudgetAsync(ProviderAccount account, BudgetUnit unit,
        CancellationToken cancellationToken = default);
}

/// <summary>Obtains model prices at runtime; unsupported models return null, not zero pricing.</summary>
public interface IProviderPricingProvider
{
    BillingProvider Provider { get; }

    Task<ModelPricing?> GetPricingAsync(string modelId, CancellationToken cancellationToken = default);
}

/// <summary>Optional catalog for identifying lower-cost models; price alone does not establish task suitability.</summary>
public interface IProviderPricingCatalog
{
    Task<IReadOnlyList<ModelPricing>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>Price-only alternative awaiting independent capability and quality validation.</summary>
public sealed record ModelCostAlternative(string ModelId, decimal P90Cost, decimal SavingsPercent,
    string SuitabilityCaveat);

/// <summary>Measured usage is independent of estimates and scoped to the exact account/run.</summary>
public interface IProviderUsageCollector
{
    BillingProvider Provider { get; }

    Task<ActualUsageObservation?> ReadUsageAsync(ProviderAccount account, string sessionId,
        CancellationToken cancellationToken = default);
}

/// <summary>Source-observed totals, with optional credit and premium request accounting.</summary>
public sealed record ActualUsageObservation(ProviderAccount Account, string SessionId, UsageBreakdown Usage,
    decimal? AiCredits, decimal? PremiumRequests, DateTimeOffset ObservedAt, string Source,
    IReadOnlyList<string>? Models = null, bool IsCumulative = false, bool CreditsVerified = false);
