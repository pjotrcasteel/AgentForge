using Microsoft.VisualStudio.TestTools.UnitTesting;
using TokenBudget.Application;
using TokenBudget.Domain;

namespace TokenBudget.Tests;

[TestClass]
public sealed class MarkdownPlanAnalyzerTests
{
    private readonly MarkdownPlanAnalyzer _analyzer = new();

    [TestMethod]
    public async Task AnalyzeAsync_ExplicitMilestones_PreservesDependenciesAndGates()
    {
        const string plan = """
            # My plan
            ## M0 — Foundation
            Implement src/Engine.cs and add tests.
            ## M1 — Visual verification
            Depends on M0. Stop at mandatory visual approval gate. Capture screenshot.
            """;

        PlanGraph graph = await _analyzer.AnalyzeAsync(Request(plan), TestContext.CancellationToken);

        Assert.HasCount(2, graph.Milestones);
        Assert.AreEqual("M0", graph.Milestones[0].Id);
        Assert.IsTrue(graph.Milestones[0].RequiresTests);
        CollectionAssert.Contains(graph.Milestones[0].ReferencedFiles.ToArray(), "src/Engine.cs");
        CollectionAssert.Contains(graph.Milestones[1].Dependencies.ToArray(), "M0");
        Assert.IsTrue(graph.Milestones[1].RequiresApproval);
        Assert.IsTrue(graph.Milestones[1].RequiresVisualProof);
    }

    [TestMethod]
    public async Task AnalyzeAsync_WithoutMilestones_FallsBackToOneStage()
    {
        PlanGraph graph = await _analyzer.AnalyzeAsync(Request("Please refactor all components carefully."), TestContext.CancellationToken);
        Assert.HasCount(1, graph.Milestones);
        Assert.AreEqual("PLAN", graph.Milestones[0].Id);
        Assert.IsTrue(graph.Warnings.Count > 0);
    }

    [TestMethod]
    public async Task AnalyzeAsync_HeadingsWithoutExplicitIds_UsesSections()
    {
        PlanGraph graph = await _analyzer.AnalyzeAsync(Request("## Analyze\nRead inputs\n## Implement\nWrite code"), TestContext.CancellationToken);
        Assert.HasCount(2, graph.Milestones);
        Assert.AreEqual("S1", graph.Milestones[0].Id);
    }

    [TestMethod]
    public async Task AnalyzeAsync_ReportsDependencyCycle()
    {
        PlanGraph graph = await _analyzer.AnalyzeAsync(Request("## M0 - First\nDepends on M1\n## M1 - Second\nDepends on M0"), TestContext.CancellationToken);
        Assert.IsTrue(graph.Warnings.Any(static warning => warning.Contains("cycle", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task AnalyzeAsync_UntrustedCodeIsParsedAsText_NotExecuted()
    {
        const string plan = "## M0 - Never run this\n```powershell\nRemove-Item -Recurse C:\\important\n```";
        PlanGraph graph = await _analyzer.AnalyzeAsync(Request(plan), TestContext.CancellationToken);
        StringAssert.Contains(graph.Milestones[0].Body, "Remove-Item");
    }

    [TestMethod]
    public async Task AnalyzeAsync_MilestonesInsideCodeFence_AreNotExecutedOrCounted()
    {
        const string plan = """
            ## M0 - Real stage
            This stage implements the plan.
            ```markdown
            ## M99 - Not a real milestone
            ```
            ## M1 - Follow-up
            Depends on M0.
            """;
        PlanGraph graph = await _analyzer.AnalyzeAsync(Request(plan), TestContext.CancellationToken);
        Assert.HasCount(2, graph.Milestones);
    }

    [TestMethod]
    public async Task AnalyzeAsync_OverlongInputRejected()
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
        {
            await _analyzer.AnalyzeAsync(Request(new string('a', 200_001)), TestContext.CancellationToken);
        });
    }

    private static AnalyzePlanRequest Request(string plan) => new(plan, "model", ExecutionTarget.CodexCli, new());

    public TestContext TestContext { get; set; } = null!;
}
