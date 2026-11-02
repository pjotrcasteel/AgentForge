using Microsoft.VisualStudio.TestTools.UnitTesting;
using TokenBudget.Cli.Configuration;

namespace TokenBudget.Cli.Tests;

[TestClass]
public sealed class AgentForgeMcpToolsTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task OnePromptReturnsDeterministicTokenScopeAndUnknownBillingAdvice()
    {
        await WithStoreAsync(async (store, workspace) =>
        {
            string report = await AgentForgeMcpTools.PreflightTaskAsync("Refactor the lighting engine", store,
                workspace, TestContext.CancellationToken);

            StringAssert.Contains(report, "Tokens P10 / P50 / P90");
            StringAssert.Contains(report, "Prompt scope: Needs narrowing");
            StringAssert.Contains(report, "current model prices unavailable");
            StringAssert.Contains(report, "no task executed");
        });
    }

    [TestMethod]
    public async Task McpPromptIsNeverTreatedAsFilePath()
    {
        await WithStoreAsync(async (store, workspace) =>
        {
            string path = Path.Combine(workspace, "plan.md");
            await File.WriteAllTextAsync(path, "PRIVATE_FILE_CONTENT_MUST_NOT_BE_READ", TestContext.CancellationToken);

            string report = await AgentForgeMcpTools.PreflightTaskAsync(path, store, workspace,
                TestContext.CancellationToken);

            StringAssert.Contains(report, "Tokens P10 / P50 / P90");
            Assert.IsFalse(report.Contains("PRIVATE_FILE_CONTENT_MUST_NOT_BE_READ", StringComparison.Ordinal));
        });
    }

    [TestMethod]
    public async Task InvalidPromptCannotBeReportedAsAValidEstimate()
    {
        await WithStoreAsync(async (store, workspace) =>
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => AgentForgeMcpTools.PreflightTaskAsync(
                "", store, workspace, TestContext.CancellationToken));
        });
    }

    private static async Task WithStoreAsync(Func<AgentForgeSettingsStore, string, Task> test)
    {
        string workspace = Path.Combine(Path.GetTempPath(), "agentforge-mcp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            await test(new AgentForgeSettingsStore(Path.Combine(workspace, "settings.json")), workspace);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }
}
