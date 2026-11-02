using Microsoft.VisualStudio.TestTools.UnitTesting;
using TokenBudget.Application;
using TokenBudget.Domain;

namespace TokenBudget.Tests;

[TestClass]
public sealed class ForecastEngineTests
{
    private const string Plan = """
        ## M0 — Build domain models
        Add the data contracts, parser and unit tests.
        ## M1 — Build the engine
        Depends on M0. Add Monte Carlo simulation and regression tests.
        ## M2 — Verify screenshots
        Depends on M1. Mandatory approval gate. Real visual proof required.
        """;

    [TestMethod]
    public async Task ForecastAsync_SameSeedAndInputs_ProducesSameQuantiles()
    {
        AnalyzePlanRequest request = Request(Plan, 500_000);
        EstimateReport first = await Estimate(request);
        EstimateReport second = await Estimate(request);

        Assert.AreEqual(first.CumulativeTokens, second.CumulativeTokens);
        Assert.AreEqual(first.PeakContextTokens, second.PeakContextTokens);
        Assert.AreEqual(first.RiskAdjustedCumulativeTokens, second.RiskAdjustedCumulativeTokens);
        CollectionAssert.AreEqual(first.Milestones.Select(static stage => stage.CumulativeTokens.P90).ToArray(),
            second.Milestones.Select(static stage => stage.CumulativeTokens.P90).ToArray());
    }

    [TestMethod]
    public async Task ForecastAsync_ReportsMonotonicQuantiles()
    {
        EstimateReport report = await Estimate(Request(Plan, 5_000_000));
        Assert.IsTrue(report.CumulativeTokens.P10 <= report.CumulativeTokens.P50);
        Assert.IsTrue(report.CumulativeTokens.P50 <= report.CumulativeTokens.P90);
        Assert.IsTrue(report.PeakContextTokens.P10 <= report.PeakContextTokens.P50);
        Assert.IsTrue(report.PeakContextTokens.P50 <= report.PeakContextTokens.P90);
        Assert.AreEqual(ForecastConfidence.Low, report.Confidence);
    }

    [TestMethod]
    public async Task ForecastAsync_VerySmallHardBudget_Rejects()
    {
        EstimateReport report = await Estimate(Request(Plan, 1));
        Assert.AreEqual(BudgetDecision.Reject, report.Decision);
    }

    [TestMethod]
    public async Task ForecastAsync_MissingBudget_ReturnsInsufficientEvidence()
    {
        EstimateReport report = await Estimate(Request(Plan, null));
        Assert.AreEqual(BudgetDecision.InsufficientEvidence, report.Decision);
    }

    [TestMethod]
    public async Task ForecastAsync_ApprovalGatesArePreserved()
    {
        EstimateReport report = await Estimate(Request(Plan, 5_000_000));
        Assert.HasCount(3, report.Milestones);
        Assert.IsTrue(report.Milestones[2].RequiresApproval);
        Assert.IsTrue(report.Milestones[2].RequiresVisualProof);
        CollectionAssert.Contains(report.Milestones[2].Dependencies.ToArray(), "M1");
    }

    [TestMethod]
    public async Task ForecastAsync_UncalibratedCountNeverClaimsExactness()
    {
        EstimateReport report = await Estimate(Request(Plan, 5_000_000));
        Assert.AreEqual(MeasurementSource.Heuristic, report.InitialInputCountSource);
        Assert.AreNotEqual(BudgetDecision.Pass, report.Decision);
    }

    [TestMethod]
    public async Task ForecastAsync_MoreMilestones_ChangesWorkload()
    {
        EstimateReport shortReport = await Estimate(Request("Do a small edit.", 10_000_000));
        EstimateReport longReport = await Estimate(Request(Plan, 10_000_000));
        Assert.IsTrue(longReport.CumulativeTokens.P50 > shortReport.CumulativeTokens.P50);
    }

    [TestMethod]
    public async Task AnalyzeAsync_InvalidRiskToleranceRejected()
    {
        var request = Request(Plan, 200_000) with { Budget = new BudgetConstraints(200_000, RiskTolerance: 0.9) };
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
        {
            await AnalyzePlanService.CreateDefault().AnalyzeAsync(request, TestContext.CancellationToken);
        });
    }

    private static AnalyzePlanRequest Request(string plan, long? budget) =>
        new(plan, "example-model", ExecutionTarget.CodexCli, new BudgetConstraints(MaxCumulativeTokens: budget), RandomSeed: 54321, SimulationCount: 1_000);

    private async Task<EstimateReport> Estimate(AnalyzePlanRequest request) =>
        await AnalyzePlanService.CreateDefault().AnalyzeAsync(request, TestContext.CancellationToken);

    public TestContext TestContext { get; set; } = null!;
}
