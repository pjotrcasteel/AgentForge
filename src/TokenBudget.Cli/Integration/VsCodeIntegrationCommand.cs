using System.Reflection;
using System.Text;

namespace TokenBudget.Cli;

/// <summary>Installs an explicit, read-only AgentForge Copilot agent in one chosen workspace.</summary>
internal static class VsCodeIntegrationCommand
{
    private const string AgentResourceName = "AgentForge.CopilotAgent";
    private const string McpConfiguration = """
        {
          "servers": {
            "agentforge": {
              "type": "stdio",
              "command": "agentforge",
              "args": ["mcp"],
              "cwd": "${workspaceFolder}"
            }
          }
        }
        """;
    private const string PortableMcpConfiguration = """
        {
          "mcpServers": {
            "agentforge": {
              "command": "agentforge",
              "args": ["mcp"],
              "cwd": "${workspaceFolder}"
            }
          }
        }
        """;

    public static Task<int> RunAsync(string[] args)
    {
        if (args.Length != 2 || !string.Equals(args[1], "init", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Use: agentforge vscode init (run from your VS Code repository root).");
            return Task.FromResult(1);
        }

        return InstallAsync(Directory.GetCurrentDirectory(), Console.Out);
    }

    internal static async Task<int> InstallAsync(string workspaceDirectory, TextWriter output,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string root = Path.GetFullPath(workspaceDirectory);
            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException("Workspace directory does not exist: " + root);
            }

            string agentPath = Path.Combine(root, ".github", "agents", "agentforge.agent.md");
            string vscodeServerPath = Path.Combine(root, ".vscode", "mcp.json");
            string serverPath = vscodeServerPath;
            string agentContent = ReadAgentDefinition();
            string serverContent = McpConfiguration + "\n";

            EnsureNoLinkedPaths(root, agentPath, vscodeServerPath, vscodeServerPath);
            if (File.Exists(vscodeServerPath))
            {
                string existing = await File.ReadAllTextAsync(vscodeServerPath, cancellationToken);
                if (!string.Equals(existing, serverContent, StringComparison.Ordinal))
                {
                    if (existing.Contains("\"agentforge\"", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException("An AgentForge MCP server is already configured differently: " + vscodeServerPath);
                    }

                    // VS Code also supports a separate portable root configuration; preserve other servers as-is.
                    serverPath = Path.Combine(root, ".mcp.json");
                    serverContent = PortableMcpConfiguration + "\n";
                }
            }

            EnsureNoLinkedPaths(root, agentPath, vscodeServerPath, serverPath);
            await EnsureNoConflictingFileAsync(agentPath, agentContent, cancellationToken);
            await EnsureNoConflictingFileAsync(serverPath, serverContent, cancellationToken);

            await WriteIfAbsentAsync(agentPath, agentContent, cancellationToken);
            await WriteIfAbsentAsync(serverPath, serverContent, cancellationToken);

            await output.WriteLineAsync("AgentForge Copilot workspace integration is ready:");
            await output.WriteLineAsync("  " + agentPath);
            await output.WriteLineAsync("  " + serverPath);
            await output.WriteLineAsync("VS Code: trust your workspace, start the agentforge MCP server, select AgentForge in Copilot Chat.");
            await output.WriteLineAsync("Example: Refactor the lighting engine");
            await output.WriteLineAsync("The MCP configuration requires the 'agentforge' .NET tool on PATH.");
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or
            UnauthorizedAccessException or InvalidOperationException)
        {
            await output.WriteLineAsync("AgentForge VS Code integration: " + exception.Message);
            return 1;
        }
    }

    private static string ReadAgentDefinition()
    {
        Assembly assembly = typeof(VsCodeIntegrationCommand).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(AgentResourceName) ??
            throw new InvalidOperationException("The embedded Copilot agent definition was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void EnsureNoLinkedPaths(string root, string agentPath, string vscodeServerPath, string serverPath)
    {
        string[] paths =
        [
            root,
            Path.Combine(root, ".github"),
            Path.Combine(root, ".github", "agents"),
            Path.Combine(root, ".vscode"),
            agentPath,
            vscodeServerPath,
            serverPath
        ];

        foreach (string path in paths)
        {
            if ((Directory.Exists(path) || File.Exists(path)) &&
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Refusing to install through a symbolic link or junction: " + path);
            }
        }
    }

    private static async Task EnsureNoConflictingFileAsync(string path, string expected,
        CancellationToken cancellationToken)
    {
        if (File.Exists(path) && !string.Equals(await File.ReadAllTextAsync(path, cancellationToken), expected,
            StringComparison.Ordinal))
        {
            throw new IOException("Existing file differs and was not overwritten: " + path +
                ". Merge the AgentForge server/agent definition manually if needed.");
        }
    }

    private static async Task WriteIfAbsentAsync(string path, string content, CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(content.AsMemory(), cancellationToken);
    }
}
