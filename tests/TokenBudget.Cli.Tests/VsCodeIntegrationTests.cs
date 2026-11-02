using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TokenBudget.Cli.Tests;

[TestClass]
public sealed class VsCodeIntegrationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task InitCreatesOnePromptReadOnlyCopilotIntegration()
    {
        await WithWorkspaceAsync(async workspace =>
        {
            using var output = new StringWriter();
            int status = await VsCodeIntegrationCommand.InstallAsync(workspace, output, TestContext.CancellationToken);

            Assert.AreEqual(0, status);
            string mcp = await File.ReadAllTextAsync(Path.Combine(workspace, ".vscode", "mcp.json"),
                TestContext.CancellationToken);
            using JsonDocument document = JsonDocument.Parse(mcp);
            JsonElement server = document.RootElement.GetProperty("servers").GetProperty("agentforge");
            Assert.AreEqual("stdio", server.GetProperty("type").GetString());
            Assert.AreEqual("agentforge", server.GetProperty("command").GetString());
            Assert.AreEqual("mcp", server.GetProperty("args")[0].GetString());
            Assert.AreEqual("${workspaceFolder}", server.GetProperty("cwd").GetString());

            string agent = await File.ReadAllTextAsync(Path.Combine(workspace, ".github", "agents", "agentforge.agent.md"),
                TestContext.CancellationToken);
            StringAssert.Contains(agent, "tools: ['agentforge/*']");
            StringAssert.Contains(agent, "does not edit files");
            StringAssert.Contains(agent, "PreflightTask");
            StringAssert.Contains(output.ToString(), "Copilot workspace integration is ready");
        });
    }

    [TestMethod]
    public async Task InitIsIdempotentWithoutTouchingExistingFiles()
    {
        await WithWorkspaceAsync(async workspace =>
        {
            using var output = new StringWriter();
            int first = await VsCodeIntegrationCommand.InstallAsync(workspace, output, TestContext.CancellationToken);
            string path = Path.Combine(workspace, ".vscode", "mcp.json");
            string original = await File.ReadAllTextAsync(path, TestContext.CancellationToken);
            int second = await VsCodeIntegrationCommand.InstallAsync(workspace, output, TestContext.CancellationToken);

            Assert.AreEqual(0, first);
            Assert.AreEqual(0, second);
            Assert.AreEqual(original, await File.ReadAllTextAsync(path, TestContext.CancellationToken));
        });
    }

    [TestMethod]
    public async Task InitKeepsExistingMcpServersAndUsesPortableConfiguration()
    {
        await WithWorkspaceAsync(async workspace =>
        {
            string path = Path.Combine(workspace, ".vscode", "mcp.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            const string existing = "{\"servers\":{\"other-server\":{\"type\":\"stdio\",\"command\":\"tool\"}}}";
            await File.WriteAllTextAsync(path, existing, TestContext.CancellationToken);
            using var output = new StringWriter();

            int status = await VsCodeIntegrationCommand.InstallAsync(workspace, output, TestContext.CancellationToken);

            Assert.AreEqual(0, status);
            Assert.AreEqual(existing, await File.ReadAllTextAsync(path, TestContext.CancellationToken));
            string portable = await File.ReadAllTextAsync(Path.Combine(workspace, ".mcp.json"),
                TestContext.CancellationToken);
            using JsonDocument document = JsonDocument.Parse(portable);
            Assert.AreEqual("agentforge", document.RootElement.GetProperty("mcpServers")
                .GetProperty("agentforge").GetProperty("command").GetString());
            Assert.IsTrue(File.Exists(Path.Combine(workspace, ".github", "agents", "agentforge.agent.md")));
        });
    }

    [TestMethod]
    public async Task InitDoesNotOverwriteConflictingPortableServerOrPartiallyInstall()
    {
        await WithWorkspaceAsync(async workspace =>
        {
            string vscodePath = Path.Combine(workspace, ".vscode", "mcp.json");
            Directory.CreateDirectory(Path.GetDirectoryName(vscodePath)!);
            await File.WriteAllTextAsync(vscodePath, "{\"servers\":{\"other\":{}}}", TestContext.CancellationToken);
            string portablePath = Path.Combine(workspace, ".mcp.json");
            await File.WriteAllTextAsync(portablePath, "user-owned portable config", TestContext.CancellationToken);
            using var output = new StringWriter();

            int status = await VsCodeIntegrationCommand.InstallAsync(workspace, output, TestContext.CancellationToken);

            Assert.AreEqual(1, status);
            Assert.AreEqual("user-owned portable config", await File.ReadAllTextAsync(portablePath,
                TestContext.CancellationToken));
            Assert.IsFalse(File.Exists(Path.Combine(workspace, ".github", "agents", "agentforge.agent.md")));
        });
    }

    [TestMethod]
    public async Task InitPreservesExistingCustomAgent()
    {
        await WithWorkspaceAsync(async workspace =>
        {
            string path = Path.Combine(workspace, ".github", "agents", "agentforge.agent.md");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "user-owned agent", TestContext.CancellationToken);
            using var output = new StringWriter();

            int status = await VsCodeIntegrationCommand.InstallAsync(workspace, output, TestContext.CancellationToken);

            Assert.AreEqual(1, status);
            Assert.AreEqual("user-owned agent", await File.ReadAllTextAsync(path, TestContext.CancellationToken));
            Assert.IsFalse(File.Exists(Path.Combine(workspace, ".vscode", "mcp.json")));
        });
    }

    private static async Task WithWorkspaceAsync(Func<string, Task> test)
    {
        string workspace = Path.Combine(Path.GetTempPath(), "agentforge-vscode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            await test(workspace);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }
}
