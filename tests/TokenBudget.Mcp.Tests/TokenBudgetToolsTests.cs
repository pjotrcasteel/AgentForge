using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TokenBudget.Mcp;

namespace TokenBudget.Mcp.Tests;

[TestClass]
public sealed class TokenBudgetToolsTests
{
    [TestMethod]
    public async Task Preflight_IsOfflineAndReturnsConservativeVerdict()
    {
        string result = await TokenBudgetTools.Preflight("## M0 - Build\nImplement a small feature and tests.",
            "codex-model", "CodexCli", 250_000, cancellationToken: TestContext.CancellationToken);
        using JsonDocument json = JsonDocument.Parse(result);
        Assert.AreEqual("CodexCli", json.RootElement.GetProperty("target").GetString());
        Assert.AreEqual("Heuristic", json.RootElement.GetProperty("initialInputCountSource").GetString());
        Assert.AreNotEqual("Pass", json.RootElement.GetProperty("decision").GetString());
    }

    [TestMethod]
    public async Task Preflight_InvalidModelAndBudget_AreRejected()
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
        {
            await TokenBudgetTools.Preflight("plan", "", "CodexCli", 100, cancellationToken: TestContext.CancellationToken);
        });
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
        {
            await TokenBudgetTools.Preflight("plan", "valid-model", "CodexCli", 0, cancellationToken: TestContext.CancellationToken);
        });
    }

    [TestMethod]
    public void RepositoryPath_WithoutAllowlist_IsRejected()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => TokenBudgetTools.ResolveRepositoryPath(".", null));
    }

    [TestMethod]
    public void RepositoryPath_OutsideAllowlist_IsRejected()
    {
        string root = Path.Combine(Path.GetTempPath(), $"tokenbudget-mcp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            Assert.ThrowsExactly<UnauthorizedAccessException>(() =>
                TokenBudgetTools.ResolveRepositoryPath("../outside", root));
        }
        finally
        {
            Directory.Delete(root);
        }
    }

    [TestMethod]
    public void RepositoryPath_InsideAllowlist_IsAccepted()
    {
        string root = Path.Combine(Path.GetTempPath(), $"tokenbudget-mcp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "repo"));
        try
        {
            Assert.AreEqual(Path.Combine(root, "repo"), TokenBudgetTools.ResolveRepositoryPath("repo", root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public TestContext TestContext { get; set; } = null!;
}
