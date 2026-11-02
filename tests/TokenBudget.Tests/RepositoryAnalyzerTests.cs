using Microsoft.VisualStudio.TestTools.UnitTesting;
using TokenBudget.Application;
using TokenBudget.Domain;

namespace TokenBudget.Tests;

[TestClass]
public sealed class RepositoryAnalyzerTests
{
    [TestMethod]
    public async Task InspectAsync_SkipsBuildOutputAndScansMetadata()
    {
        string root = Path.Combine(Path.GetTempPath(), $"tokenbudget-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "src", "A.cs"), "class A {}", TestContext.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(root, "src", "A.csproj"), "<Project/>", TestContext.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(root, "bin", "Generated.cs"), "class Generated {}", TestContext.CancellationToken);
            var graph = new PlanGraph([new PlanMilestone("M0", "Code", "A.cs", 1, [], ["A.cs"], false, false, false)], []);

            RepositoryImpact impact = await new ReadOnlyRepositoryImpactAnalyzer().InspectAsync(root, graph, TestContext.CancellationToken);
            Assert.IsTrue(impact.Available);
            Assert.AreEqual(1, impact.SourceFiles);
            Assert.AreEqual(1, impact.ProjectFiles);
            Assert.AreEqual(1, impact.MatchingFileReferences);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task InspectAsync_MissingCheckoutIsNotZeroFileEvidence()
    {
        RepositoryImpact impact = await new ReadOnlyRepositoryImpactAnalyzer().InspectAsync(null,
            new PlanGraph([], []), TestContext.CancellationToken);
        Assert.IsFalse(impact.Available);
        Assert.IsNotNull(impact.Reason);
    }

    public TestContext TestContext { get; set; } = null!;
}
