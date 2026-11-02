namespace TokenBudget.Domain;

/// <summary>Estimated P10, P50 and P90 token totals.</summary>
public sealed record TokenRange(long P10, long P50, long P90);

/// <summary>Provider-neutral token categories for pricing; cached tokens remain an unknown subset until observed or configured.</summary>
public sealed record TokenUsageForecast(TokenRange InputTokens, TokenRange OutputTokens);

/// <summary>Measured input/output counters; cached and reasoning tokens are subsets.</summary>
public sealed record UsageBreakdown(
    long InputTokens,
    long OutputTokens,
    long? CachedInputTokens,
    long? CacheWriteInputTokens,
    long? ReasoningOutputTokens)
{
    /// <summary>Total billable input and output tokens without double counting subsets.</summary>
    public long TotalTokens => checked(InputTokens + OutputTokens);
}

/// <summary>Measured or approximated initial request token count.</summary>
public sealed record TokenCountResult(long Count, MeasurementSource Source, string? Note);

/// <summary>Projected tokens attributable to a plan milestone.</summary>
public sealed record MilestoneForecast(
    string Id,
    string Description,
    TokenRange CumulativeTokens,
    TokenRange PeakContextTokens,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> Assumptions,
    bool RequiresApproval,
    bool RequiresVisualProof);

/// <summary>Complete forecast and the evidence used to reach a budget decision.</summary>
public sealed record EstimateReport(
    Guid EstimateId,
    DateTimeOffset GeneratedAt,
    ExecutionTarget Target,
    string ModelId,
    long? CountedInitialInputTokens,
    MeasurementSource InitialInputCountSource,
    TokenRange CumulativeTokens,
    TokenRange PeakContextTokens,
    long RiskAdjustedCumulativeTokens,
    long RiskAdjustedPeakContextTokens,
    ForecastConfidence Confidence,
    BudgetDecision Decision,
    BudgetConstraints Budget,
    RepositoryImpact Repository,
    IReadOnlyList<MilestoneForecast> Milestones,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> Recommendations,
    IReadOnlyList<string> Assumptions,
    string EstimatorVersion,
    TokenUsageForecast? TokenCategories = null);
