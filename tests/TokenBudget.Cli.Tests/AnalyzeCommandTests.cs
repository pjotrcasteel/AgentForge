using Microsoft.VisualStudio.TestTools.UnitTesting;
using TokenBudget.Cli.Commands;
using TokenBudget.Domain;

namespace TokenBudget.Cli.Tests;

[TestClass]
public sealed class AnalyzeCommandTests
{
    [TestMethod]
    [DataRow("codex", ExecutionTarget.CodexCli)]
    [DataRow("codex-cli", ExecutionTarget.CodexCli)]
    [DataRow("codex-app-server", ExecutionTarget.CodexAppServer)]
    [DataRow("openai", ExecutionTarget.OpenAiResponses)]
    [DataRow("openai-agents", ExecutionTarget.OpenAiAgents)]
    [DataRow("work", ExecutionTarget.ChatGptWork)]
    [DataRow("anthropic", ExecutionTarget.AnthropicMessages)]
    public void ResolveTarget_PreservesAllProviderAliases(string alias, ExecutionTarget expected)
    {
        Assert.AreEqual(expected, AnalyzeCommand.ResolveTarget(alias));
    }

    [TestMethod]
    public void ResolveTarget_RejectsUnsupportedProviders()
    {
        Assert.ThrowsExactly<ArgumentException>(() => AnalyzeCommand.ResolveTarget("unknown"));
    }
}
