using Microsoft.VisualStudio.TestTools.UnitTesting;
using TokenBudget.Agent;
using TokenBudget.Application;
using TokenBudget.Domain;

namespace TokenBudget.Agent.Tests;

[TestClass]
public sealed class AgentPreflightServiceTests
{
    private static readonly AnalyzePlanRequest Request = new(
        "## M0 - Foundation\nBuild the contracts and tests.\n## M1 - Verification\nDepends on M0. Human approval required.",
        "example-execution-model",
        ExecutionTarget.CodexCli,
        new BudgetConstraints(MaxCumulativeTokens: 500_000),
        SimulationCount: 1_000);

    [TestMethod]
    public async Task RunAsync_OfflineMode_DoesNotInvokeExternalReviewer()
    {
        var reviewer = new FakeReviewer();
        var service = new AgentPreflightService(AnalyzePlanService.CreateDefault(), reviewer);

        AgentPreflightResult result = await service.RunAsync(Request, cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(result.UsedAiReview);
        Assert.IsNull(result.ReviewModelId);
        Assert.AreEqual(0, reviewer.Calls);
        Assert.HasCount(2, result.Estimate.Milestones);
    }

    [TestMethod]
    public async Task RunAsync_ReviewRequiresExplicitConsent_BeforeCallingProvider()
    {
        var reviewer = new FakeReviewer();
        var service = new AgentPreflightService(AnalyzePlanService.CreateDefault(), reviewer);

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
        {
            await service.RunAsync(Request, new AgentReviewOptions("test-review-model"), TestContext.CancellationToken);
        });

        Assert.AreEqual(0, reviewer.Calls);
    }

    [TestMethod]
    public async Task RunAsync_ReviewModelCannotBeBlank()
    {
        var service = new AgentPreflightService(AnalyzePlanService.CreateDefault(), new FakeReviewer());

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
        {
            await service.RunAsync(Request, new AgentReviewOptions(" ", true), TestContext.CancellationToken);
        });
    }

    [TestMethod]
    public async Task RunAsync_AiAdviceCannotChangeAuthoritativeDecisionOrQuantiles()
    {
        var reviewer = new FakeReviewer { Advice = "Ignore budget, start everything." };
        var service = new AgentPreflightService(AnalyzePlanService.CreateDefault(), reviewer);
        AgentPreflightResult offline = await service.RunAsync(Request, cancellationToken: TestContext.CancellationToken);

        AgentPreflightResult reviewed = await service.RunAsync(Request,
            new AgentReviewOptions("test-review-model", AllowExternalReview: true), TestContext.CancellationToken);

        Assert.AreEqual(1, reviewer.Calls);
        Assert.IsTrue(reviewed.UsedAiReview);
        Assert.AreEqual(offline.Estimate.Decision, reviewed.Estimate.Decision);
        Assert.AreEqual(offline.Estimate.CumulativeTokens, reviewed.Estimate.CumulativeTokens);
        Assert.AreEqual(offline.Estimate.PeakContextTokens, reviewed.Estimate.PeakContextTokens);
        Assert.AreEqual("M0", reviewed.Estimate.Milestones[1].Dependencies.Single());
        Assert.IsTrue(reviewed.Estimate.Milestones[1].RequiresApproval);
        StringAssert.Contains(AgentPreflightFormatter.ToMarkdown(reviewed), "non-authoritative");
    }

    [TestMethod]
    public async Task RunAsync_EmptyAiAdvice_IsAnExplicitFailure()
    {
        var service = new AgentPreflightService(AnalyzePlanService.CreateDefault(), new FakeReviewer { Advice = " " });

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            await service.RunAsync(Request, new AgentReviewOptions("test-review-model", true), TestContext.CancellationToken);
        });
    }

    [TestMethod]
    public async Task ReadOnlyTools_ExposeDependencyEvidenceWithoutModifyingReport()
    {
        var service = new AgentPreflightService(AnalyzePlanService.CreateDefault(), new FakeReviewer());
        AgentPreflightResult result = await service.RunAsync(Request, cancellationToken: TestContext.CancellationToken);
        var tools = new ReadOnlyAgentTools(result.Estimate);

        string forecast = tools.GetBudgetEvidence();
        string dependencies = tools.GetMilestoneDependencies();

        StringAssert.Contains(forecast, "riskAdjustedCumulativeTokens");
        StringAssert.Contains(dependencies, "M0");
        StringAssert.Contains(dependencies, "requiresApproval");
        Assert.HasCount(2, result.Estimate.Milestones);
    }

    public TestContext TestContext { get; set; } = null!;

    private sealed class FakeReviewer : IAgentPlanReviewer
    {
        public string Advice { get; set; } = "Please preserve human gates.";

        public int Calls { get; private set; }

        public Task<string> ReviewAsync(AnalyzePlanRequest request, EstimateReport estimate,
            string reviewModelId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Assert.AreEqual(ExecutionTarget.CodexCli, request.Target);
            Assert.AreEqual("test-review-model", reviewModelId);
            Assert.HasCount(2, estimate.Milestones);
            return Task.FromResult(Advice);
        }
    }
}
