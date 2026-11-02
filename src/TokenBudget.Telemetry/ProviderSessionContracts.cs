using TokenBudget.Billing;
using TokenBudget.Domain;

namespace TokenBudget.Telemetry;

/// <summary>Explicit match evidence for a completed, single-model provider session.</summary>
public sealed record ProviderForecastBinding(Guid EstimateId, BillingProvider Provider, string ModelId,
    TokenRange ForecastTokens, CostRange? ForecastCosts = null, BudgetUnit? CostUnit = null);

/// <summary>Cumulative provider checkpoint; a per-call event must never be imported as a session total.</summary>
public sealed record ProviderSessionCheckpoint(ActualUsageObservation Observation, bool Completed,
    ProviderForecastBinding? Match = null);

/// <summary>Idempotent checkpoint outcomes; only positive differences between cumulative states become deltas.</summary>
public enum ProviderCheckpointChange { Inserted, Advanced, Finalized, MetadataUpdated, Duplicate }

/// <summary>Usage since the previous stored cumulative checkpoint, not since the previous per-call event.</summary>
public sealed record ProviderUsageDelta(long InputTokens, long OutputTokens, decimal? AiCredits, decimal? PremiumRequests);

/// <summary>Reconciliation result; cost coverage is never claimed for provisional nano-AIU conversions.</summary>
public sealed record ProviderSessionIngestResult(ProviderCheckpointChange Change, ProviderUsageDelta Delta,
    bool EligibleForTokenComparison, bool EligibleForCostComparison);

/// <summary>Account/model-isolated, descriptive backtesting without automatically modifying forecast priors.</summary>
public sealed record ProviderSessionHistorySummary(ProviderAccount Account, string ModelId, int Sessions,
    int CompletedSessions, int MatchedCompletedSessions, int VerifiedCostMatches,
    double? MedianActualToP50Ratio, double? ObservedP90TokenCoverage,
    double? ObservedP90CostCoverage, bool SufficientForCalibration, string Note);
