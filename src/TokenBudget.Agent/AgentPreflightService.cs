using TokenBudget.Application;
using TokenBudget.Domain;

namespace TokenBudget.Agent;

/// <summary>
/// Runs deterministic preflight before any optional AI review. The reviewer cannot modify its result,
/// confidence, dependencies, budget verdict or gating requirements.
/// </summary>
public sealed class AgentPreflightService(AnalyzePlanService estimator, IAgentPlanReviewer reviewer)
{
    public async Task<AgentPreflightResult> RunAsync(
        AnalyzePlanRequest request,
        AgentReviewOptions? reviewOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Fail closed on accidental uploads: asking for a model is not consent to transmit the plan.
        if (reviewOptions is { AllowExternalReview: false })
        {
            throw new ArgumentException("External AI review requires explicit consent (--allow-ai-upload true).", nameof(reviewOptions));
        }

        if (reviewOptions is not null && string.IsNullOrWhiteSpace(reviewOptions.ReviewModelId))
        {
            throw new ArgumentException("A valid review model ID is required.", nameof(reviewOptions));
        }

        EstimateReport estimate = await estimator.AnalyzeAsync(request, cancellationToken);
        if (reviewOptions is null)
        {
            return new AgentPreflightResult(estimate, null, null);
        }

        string advice = await reviewer.ReviewAsync(request, estimate, reviewOptions.ReviewModelId, cancellationToken);
        if (string.IsNullOrWhiteSpace(advice))
        {
            throw new InvalidOperationException("The AI reviewer returned no advice; the deterministic estimate remains valid.");
        }

        return new AgentPreflightResult(estimate, advice, reviewOptions.ReviewModelId);
    }
}
