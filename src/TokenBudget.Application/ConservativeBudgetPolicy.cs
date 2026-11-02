using TokenBudget.Domain;

namespace TokenBudget.Application;

public sealed class ConservativeBudgetPolicy : IBudgetPolicy
{
    public BudgetDecision Evaluate(
        BudgetConstraints budget,
        long riskAdjustedCumulativeTokens,
        long riskAdjustedPeakContextTokens,
        bool hasReliableRepositoryEvidence,
        bool hasMandatoryApprovalGate)
    {
        if (budget.ModelContextWindowTokens is long window && riskAdjustedPeakContextTokens > window)
        {
            return BudgetDecision.Reject;
        }

        if (budget.MaxCumulativeTokens is long cumulative && riskAdjustedCumulativeTokens > cumulative)
        {
            return BudgetDecision.Reject;
        }

        if (budget.MaxPeakContextTokens is long peak && riskAdjustedPeakContextTokens > peak)
        {
            return BudgetDecision.Reject;
        }

        if (budget.MaxCumulativeTokens is null && budget.MaxPeakContextTokens is null)
        {
            return BudgetDecision.InsufficientEvidence;
        }

        // A monetary cap cannot be validated before provider pricing and measured cache use are implemented.
        if (budget.MaxApiCostUsd is not null)
        {
            return BudgetDecision.InsufficientEvidence;
        }

        if (!hasReliableRepositoryEvidence || hasMandatoryApprovalGate)
        {
            return BudgetDecision.Warning;
        }

        bool nearCumulative = budget.MaxCumulativeTokens is long maxTotal && riskAdjustedCumulativeTokens > maxTotal * 0.85;
        bool nearContext = budget.MaxPeakContextTokens is long maxPeak && riskAdjustedPeakContextTokens > maxPeak * 0.85;
        return nearCumulative || nearContext ? BudgetDecision.Warning : BudgetDecision.Pass;
    }
}
