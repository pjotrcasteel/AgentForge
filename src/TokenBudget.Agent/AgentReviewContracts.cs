using TokenBudget.Domain;

namespace TokenBudget.Agent;

/// <summary>Options for an optional, external AI review of an already completed deterministic forecast.</summary>
public sealed record AgentReviewOptions(string ReviewModelId, bool AllowExternalReview = false);

/// <summary>Output that keeps authoritative machine results distinct from unverified model commentary.</summary>
public sealed record AgentPreflightResult(EstimateReport Estimate, string? AiAdvisory, string? ReviewModelId)
{
    public bool UsedAiReview => AiAdvisory is not null;
}

/// <summary>Read-only interface through which a provider may examine a forecast and recommend improvements.</summary>
public interface IAgentPlanReviewer
{
    Task<string> ReviewAsync(
        AnalyzePlanRequest request,
        EstimateReport estimate,
        string reviewModelId,
        CancellationToken cancellationToken);
}
