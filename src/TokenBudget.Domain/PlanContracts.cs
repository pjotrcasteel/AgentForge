namespace TokenBudget.Domain;

/// <summary>Limits and risk tolerance used to evaluate an estimate.</summary>
public sealed record BudgetConstraints(
    long? MaxCumulativeTokens = null,
    long? MaxPeakContextTokens = null,
    decimal? MaxApiCostUsd = null,
    double RiskTolerance = 0.10,
    long? ModelContextWindowTokens = null);

/// <summary>A read-only plan and its estimation options.</summary>
public sealed record AnalyzePlanRequest(
    string Plan,
    string ModelId,
    ExecutionTarget Target,
    BudgetConstraints Budget,
    string? RepositoryPath = null,
    string? RepositoryRevision = null,
    string? HistoricalProfileId = null,
    bool AllowProviderUpload = false,
    int RandomSeed = 1729,
    int SimulationCount = 10_000);

/// <summary>One parsed work item from a plan.</summary>
public sealed record PlanMilestone(
    string Id,
    string Description,
    string Body,
    int StartLine,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> ReferencedFiles,
    bool RequiresTests,
    bool RequiresApproval,
    bool RequiresVisualProof);

/// <summary>Milestones and parsing warnings derived from a plan.</summary>
public sealed record PlanGraph(IReadOnlyList<PlanMilestone> Milestones, IReadOnlyList<string> Warnings);

/// <summary>Evidence available from a read-only repository inspection.</summary>
public sealed record RepositoryImpact(
    bool Available,
    int ScannedFiles,
    int SourceFiles,
    int ProjectFiles,
    int MatchingFileReferences,
    bool Truncated,
    string? Reason);
