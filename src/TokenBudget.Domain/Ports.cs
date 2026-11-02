namespace TokenBudget.Domain;

/// <summary>Parses a plan into a dependency graph.</summary>
public interface IPlanAnalyzer
{
    /// <summary>Analyze a plan without executing its instructions.</summary>
    Task<PlanGraph> AnalyzeAsync(AnalyzePlanRequest request, CancellationToken cancellationToken);
}

/// <summary>Inspects repository metadata without modifying a checkout.</summary>
public interface IRepositoryImpactAnalyzer
{
    /// <summary>Return bounded repository evidence.</summary>
    Task<RepositoryImpact> InspectAsync(string? repositoryPath, PlanGraph graph, CancellationToken cancellationToken);
}

/// <summary>Counts the initially supplied prompt tokens using an applicable strategy.</summary>
public interface IInputTokenCounter
{
    /// <summary>Whether this counter supports a provider and model.</summary>
    bool Supports(ExecutionTarget target, string modelId);

    /// <summary>Count or approximate the initial plan input tokens.</summary>
    Task<TokenCountResult> CountAsync(AnalyzePlanRequest request, CancellationToken cancellationToken);
}

/// <summary>Forecasts cumulative and peak context tokens from a plan graph.</summary>
public interface ITokenForecastEngine
{
    /// <summary>Return the estimated distribution and model assumptions.</summary>
    Task<EstimateReport> ForecastAsync(
        AnalyzePlanRequest request,
        PlanGraph graph,
        RepositoryImpact impact,
        TokenCountResult initialInput,
        CancellationToken cancellationToken);
}

/// <summary>Applies configured hard limits to a forecast.</summary>
public interface IBudgetPolicy
{
    /// <summary>Return a budget decision given risk-adjusted totals and evidence.</summary>
    BudgetDecision Evaluate(
        BudgetConstraints budget,
        long riskAdjustedCumulativeTokens,
        long riskAdjustedPeakContextTokens,
        bool hasReliableRepositoryEvidence,
        bool hasMandatoryApprovalGate);
}
