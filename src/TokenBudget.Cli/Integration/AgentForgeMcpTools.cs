using System.ComponentModel;
using ModelContextProtocol.Server;
using TokenBudget.Cli.Commands;
using TokenBudget.Cli.Configuration;

namespace TokenBudget.Cli;

/// <summary>Single-prompt MCP facade over the same deterministic engine as the AgentForge CLI.</summary>
[McpServerToolType]
public static class AgentForgeMcpTools
{
    /// <summary>Forecast a task without executing it, editing repository files, or uploading its contents.</summary>
    [McpServerTool, Description("Read-only token, provider-budget and prompt-scope preflight. The only required input is the complete task prompt.")]
    public static Task<string> PreflightTask(
        [Description("Complete development task or Markdown plan; interpreted as text, never as a file path.")] string prompt,
        CancellationToken cancellationToken = default) =>
        PreflightTaskAsync(prompt, AgentForgeSettingsStore.ForCurrentUser(), Directory.GetCurrentDirectory(),
            cancellationToken);

    internal static async Task<string> PreflightTaskAsync(string prompt, AgentForgeSettingsStore store,
        string workingDirectory, CancellationToken cancellationToken = default)
    {
        using var output = new StringWriter();
        int status = await QuickPreflightCommand.RunAsync(["preflight", prompt], store, TextReader.Null,
            output, workingDirectory, allowFileInput: false, cancellationToken: cancellationToken);
        string report = output.ToString();

        if (status == 1)
        {
            throw new ArgumentException(report.Trim());
        }

        return report;
    }
}
