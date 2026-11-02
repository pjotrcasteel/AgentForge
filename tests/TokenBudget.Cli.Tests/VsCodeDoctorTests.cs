using Microsoft.VisualStudio.TestTools.UnitTesting;
using TokenBudget.Billing;
using TokenBudget.Cli.Configuration;

namespace TokenBudget.Cli.Tests;

[TestClass]
public sealed class VsCodeDoctorTests
{
    private static readonly DateTimeOffset CurrentTime = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ReadyWorkspaceReportsOfflineCopilotBudgetAndAction()
    {
        await WithWorkspaceAsync(async (workspace, store) =>
        {
            await InstallAsync(workspace);
            await store.SaveAsync(new AgentForgeSettings(BillingProvider.GitHubCopilot, "chosen-model", "personal",
                30_000m, 9_000m, BalanceObservedAt: CurrentTime), TestContext.CancellationToken);
            using var output = new StringWriter();

            int status = await VsCodeDoctorCommand.RunAsync(workspace, store, output, CurrentTime,
                () => true, TestContext.CancellationToken);

            Assert.AreEqual(0, status);
            StringAssert.Contains(output.ToString(), "Readiness: READY");
            StringAssert.Contains(output.ToString(), "remaining 21,000");
            StringAssert.Contains(output.ToString(), "configured");
            StringAssert.Contains(output.ToString(), "does not verify VS Code authentication");
        });
    }

    [TestMethod]
    public async Task FreshWorkspaceReportsMissingFilesAndModelWithoutMakingChanges()
    {
        await WithWorkspaceAsync(async (workspace, store) =>
        {
            using var output = new StringWriter();
            int status = await VsCodeDoctorCommand.RunAsync(workspace, store, output, CurrentTime,
                () => false, TestContext.CancellationToken);

            Assert.AreEqual(2, status);
            StringAssert.Contains(output.ToString(), "agentforge vscode init");
            StringAssert.Contains(output.ToString(), "agentforge setup");
            StringAssert.Contains(output.ToString(), "CLI available on PATH: no");
            StringAssert.Contains(output.ToString(), "Readiness: ACTION REQUIRED");
            Assert.IsFalse(Directory.Exists(Path.Combine(workspace, ".github")));
            Assert.IsFalse(File.Exists(store.FilePath));
        });
    }

    [TestMethod]
    public async Task WrongMcpCommandIsDetectedWithoutRewritingUserConfiguration()
    {
        await WithWorkspaceAsync(async (workspace, store) =>
        {
            await InstallAsync(workspace);
            await SaveConfiguredModelAsync(store);
            string path = Path.Combine(workspace, ".vscode", "mcp.json");
            string altered = (await File.ReadAllTextAsync(path, TestContext.CancellationToken))
                .Replace("\"command\": \"agentforge\"", "\"command\": \"unknown\"", StringComparison.Ordinal);
            await File.WriteAllTextAsync(path, altered, TestContext.CancellationToken);
            using var output = new StringWriter();

            int status = await VsCodeDoctorCommand.RunAsync(workspace, store, output, CurrentTime,
                () => true, TestContext.CancellationToken);

            Assert.AreEqual(2, status);
            StringAssert.Contains(output.ToString(), "unexpected settings");
            Assert.AreEqual(altered, await File.ReadAllTextAsync(path, TestContext.CancellationToken));
        });
    }

    [TestMethod]
    public async Task PortableConfigAndCommentedVsCodeServersAreAccepted()
    {
        await WithWorkspaceAsync(async (workspace, store) =>
        {
            string vscode = Path.Combine(workspace, ".vscode", "mcp.json");
            Directory.CreateDirectory(Path.GetDirectoryName(vscode)!);
            await File.WriteAllTextAsync(vscode,
                "{ // existing server\n \"servers\": {\"other\": {\"command\": \"other\",},},}",
                TestContext.CancellationToken);
            await InstallAsync(workspace);
            await SaveConfiguredModelAsync(store);
            using var output = new StringWriter();

            int status = await VsCodeDoctorCommand.RunAsync(workspace, store, output, CurrentTime,
                () => true, TestContext.CancellationToken);

            Assert.AreEqual(0, status);
            StringAssert.Contains(output.ToString(), "MCP configuration: configured");
        });
    }

    [TestMethod]
    public async Task DuplicateMcpServersMustBeResolvedExplicitly()
    {
        await WithWorkspaceAsync(async (workspace, store) =>
        {
            await InstallAsync(workspace);
            await SaveConfiguredModelAsync(store);
            await File.WriteAllTextAsync(Path.Combine(workspace, ".mcp.json"),
                "{\"mcpServers\":{\"agentforge\":{\"command\":\"agentforge\",\"args\":[\"mcp\"]}}}",
                TestContext.CancellationToken);
            using var output = new StringWriter();

            int status = await VsCodeDoctorCommand.RunAsync(workspace, store, output, CurrentTime,
                () => true, TestContext.CancellationToken);

            Assert.AreEqual(2, status);
            StringAssert.Contains(output.ToString(), "duplicate AgentForge servers");
        });
    }

    [TestMethod]
    public async Task MissingCreditAllowanceIsUnknownAndDoesNotBlockPreflight()
    {
        await WithWorkspaceAsync(async (workspace, store) =>
        {
            await InstallAsync(workspace);
            await SaveConfiguredModelAsync(store);
            using var output = new StringWriter();

            int status = await VsCodeDoctorCommand.RunAsync(workspace, store, output, CurrentTime,
                () => true, TestContext.CancellationToken);

            Assert.AreEqual(0, status);
            StringAssert.Contains(output.ToString(), "allowance unknown, used unknown, remaining unknown");
        });
    }

    [TestMethod]
    public async Task OtherProviderIsNotMistakenForCopilotCreditReadiness()
    {
        await WithWorkspaceAsync(async (workspace, store) =>
        {
            await InstallAsync(workspace);
            await store.SaveAsync(new AgentForgeSettings(BillingProvider.OpenAiApi, "api-model", "personal"),
                TestContext.CancellationToken);
            using var output = new StringWriter();

            int status = await VsCodeDoctorCommand.RunAsync(workspace, store, output, CurrentTime,
                () => true, TestContext.CancellationToken);

            Assert.AreEqual(2, status);
            StringAssert.Contains(output.ToString(), "select GitHub Copilot");
            Assert.IsFalse(output.ToString().Contains("Copilot AI credits (account-reported)", StringComparison.Ordinal));
        });
    }

    [TestMethod]
    public async Task StaleAccountReportedCreditObservationIsClearlyQualified()
    {
        await WithWorkspaceAsync(async (workspace, store) =>
        {
            await InstallAsync(workspace);
            await store.SaveAsync(new AgentForgeSettings(BillingProvider.GitHubCopilot, "chosen-model", "personal",
                30_000m, 9_000m, BalanceObservedAt: CurrentTime.AddDays(-35)), TestContext.CancellationToken);
            using var output = new StringWriter();

            int status = await VsCodeDoctorCommand.RunAsync(workspace, store, output, CurrentTime,
                () => true, TestContext.CancellationToken);

            Assert.AreEqual(0, status);
            StringAssert.Contains(output.ToString(), "older than 30 days");
            StringAssert.Contains(output.ToString(), "account-reported");
        });
    }

    [TestMethod]
    public async Task MalformedMcpJsonReturnsActionableWarning()
    {
        await WithWorkspaceAsync(async (workspace, store) =>
        {
            await InstallAsync(workspace);
            await SaveConfiguredModelAsync(store);
            await File.WriteAllTextAsync(Path.Combine(workspace, ".vscode", "mcp.json"), "{broken",
                TestContext.CancellationToken);
            using var output = new StringWriter();

            int status = await VsCodeDoctorCommand.RunAsync(workspace, store, output, CurrentTime,
                () => true, TestContext.CancellationToken);

            Assert.AreEqual(2, status);
            StringAssert.Contains(output.ToString(), "invalid JSON");
        });
    }

    private async Task InstallAsync(string workspace)
    {
        using var output = new StringWriter();
        Assert.AreEqual(0, await VsCodeIntegrationCommand.InstallAsync(workspace, output,
            TestContext.CancellationToken));
    }

    private Task SaveConfiguredModelAsync(AgentForgeSettingsStore store) => store.SaveAsync(
        AgentForgeSettings.Default with { ModelId = "chosen-model" }, TestContext.CancellationToken);

    private static async Task WithWorkspaceAsync(Func<string, AgentForgeSettingsStore, Task> test)
    {
        string directory = Path.Combine(Path.GetTempPath(), "agentforge-doctor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await test(directory, new AgentForgeSettingsStore(Path.Combine(directory, "preferences.json")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
