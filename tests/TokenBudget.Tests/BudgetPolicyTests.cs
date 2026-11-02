using Microsoft.VisualStudio.TestTools.UnitTesting;
using TokenBudget.Application;
using TokenBudget.Domain;

namespace TokenBudget.Tests;

[TestClass]
public sealed class BudgetPolicyTests
{
    private readonly ConservativeBudgetPolicy _policy = new();

    [TestMethod]
    [DataRow(90_000L, BudgetDecision.Reject)]
    [DataRow(110_000L, BudgetDecision.Warning)]
    [DataRow(150_000L, BudgetDecision.Pass)]
    public void Evaluate_CumulativeBudget_UsesThresholds(long budget, BudgetDecision expected)
    {
        BudgetDecision decision = _policy.Evaluate(new BudgetConstraints(MaxCumulativeTokens: budget),
            100_000, 50_000, true, false);
        Assert.AreEqual(expected, decision);
    }

    [TestMethod]
    public void Evaluate_PeakBudget_IndependentFromCumulative()
    {
        BudgetDecision decision = _policy.Evaluate(new BudgetConstraints(MaxCumulativeTokens: 1_000_000, MaxPeakContextTokens: 40_000),
            100_000, 50_000, true, false);
        Assert.AreEqual(BudgetDecision.Reject, decision);
    }

    [TestMethod]
    public void Evaluate_ModelContextHardLimit_Rejects()
    {
        BudgetDecision decision = _policy.Evaluate(new BudgetConstraints(MaxCumulativeTokens: 1_000_000, ModelContextWindowTokens: 49_999),
            100_000, 50_000, true, false);
        Assert.AreEqual(BudgetDecision.Reject, decision);
    }

    [TestMethod]
    public void Evaluate_UnknownUsdPricing_IsInsufficientEvidence()
    {
        BudgetDecision decision = _policy.Evaluate(new BudgetConstraints(MaxCumulativeTokens: 1_000_000, MaxApiCostUsd: 5),
            100_000, 50_000, true, false);
        Assert.AreEqual(BudgetDecision.InsufficientEvidence, decision);
    }

    [TestMethod]
    public void Evaluate_ManualApprovalNeverBecomesAutomaticPass()
    {
        BudgetDecision decision = _policy.Evaluate(new BudgetConstraints(MaxCumulativeTokens: 1_000_000),
            100_000, 50_000, true, true);
        Assert.AreEqual(BudgetDecision.Warning, decision);
    }

    [TestMethod]
    public void UsageBreakdown_ExcludesCacheAndReasoningSubsetFromSum()
    {
        var usage = new UsageBreakdown(1_000, 500, 700, 300, 200);
        Assert.AreEqual(1_500L, usage.TotalTokens);
    }
}
