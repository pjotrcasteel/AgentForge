using System.Globalization;
using System.Text.Json;
using TokenBudget.Billing;
using TokenBudget.Cli.Configuration;

namespace TokenBudget.Cli;

/// <summary>Diagnoses local Copilot integration without executing tools or contacting a provider.</summary>
internal static class VsCodeDoctorCommand
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    public static Task<int> RunAsync() => RunAsync(Directory.GetCurrentDirectory(),
        AgentForgeSettingsStore.ForCurrentUser(), Console.Out, DateTimeOffset.UtcNow);

    internal static async Task<int> RunAsync(string workspaceDirectory, AgentForgeSettingsStore store,
        TextWriter output, DateTimeOffset now, Func<bool>? toolOnPath = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string workspace = Path.GetFullPath(workspaceDirectory);
            if (!Directory.Exists(workspace))
            {
                throw new DirectoryNotFoundException("Workspace directory does not exist: " + workspace);
            }

            await output.WriteLineAsync("AgentForge doctor — local, read-only, no provider calls");
            await output.WriteLineAsync("Workspace: " + workspace);

            bool agentReady = await CheckAgentAsync(workspace, output, cancellationToken);
            bool mcpReady = await CheckMcpAsync(workspace, output, cancellationToken);
            bool pathReady = (toolOnPath ?? IsToolOnPath)();
            await output.WriteLineAsync("CLI available on PATH: " + (pathReady ? "yes" : "no"));
            if (!pathReady)
            {
                await output.WriteLineAsync("  Fix: add the .NET global tools folder to PATH and restart VS Code.");
            }

            bool settingsReady = await CheckSettingsAsync(store, output, now, cancellationToken);
            bool ready = agentReady && mcpReady && pathReady && settingsReady;
            await output.WriteLineAsync(ready
                ? "Readiness: READY for the first VS Code Copilot Chat test."
                : "Readiness: ACTION REQUIRED. Fix the items above and run agentforge doctor again.");
            await output.WriteLineAsync("This does not verify VS Code authentication, the chat-selected model, or billed AI credits.");
            return ready ? 0 : 2;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or JsonException or
            UnauthorizedAccessException or InvalidOperationException)
        {
            await output.WriteLineAsync("AgentForge doctor: " + exception.Message);
            return 1;
        }
    }

    private static async Task<bool> CheckAgentAsync(string workspace, TextWriter output,
        CancellationToken cancellationToken)
    {
        string path = Path.Combine(workspace, ".github", "agents", "agentforge.agent.md");
        if (!File.Exists(path))
        {
            await output.WriteLineAsync("Copilot custom agent: missing. Fix: agentforge vscode init");
            return false;
        }

        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            await output.WriteLineAsync("Copilot custom agent: linked file; inspect it manually.");
            return false;
        }

        if (new FileInfo(path).Length > 64 * 1024)
        {
            await output.WriteLineAsync("Copilot custom agent: larger than 64 KiB; inspect it manually.");
            return false;
        }

        string agent = await File.ReadAllTextAsync(path, cancellationToken);
        bool valid = agent.Contains("agentforge/*", StringComparison.Ordinal) &&
            agent.Contains("preflight_task", StringComparison.Ordinal);
        await output.WriteLineAsync("Copilot custom agent: " + (valid ? "configured" : "needs review (tool binding missing)"));
        return valid;
    }

    private static async Task<bool> CheckMcpAsync(string workspace, TextWriter output,
        CancellationToken cancellationToken)
    {
        string[] configs =
        [
            Path.Combine(workspace, ".vscode", "mcp.json"),
            Path.Combine(workspace, ".mcp.json")
        ];
        bool valid = false;
        int found = 0;

        foreach (string path in configs)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
                new FileInfo(path).Length > 64 * 1024)
            {
                await output.WriteLineAsync("MCP configuration: linked or oversized file needs review: " + path);
                return false;
            }

            try
            {
                using JsonDocument json = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken),
                    JsonOptions);
                JsonElement root = json.RootElement;
                string section = Path.GetFileName(path).Equals("mcp.json", StringComparison.OrdinalIgnoreCase)
                    ? "servers" : "mcpServers";
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(section, out JsonElement servers) ||
                    servers.ValueKind != JsonValueKind.Object || !servers.TryGetProperty("agentforge", out JsonElement server))
                {
                    continue;
                }

                found++;
                bool serverValid = IsValidServer(server);
                valid = serverValid || valid;
                if (!serverValid)
                {
                    await output.WriteLineAsync("MCP configuration: AgentForge server has unexpected settings in " + path);
                }
            }
            catch (JsonException)
            {
                await output.WriteLineAsync("MCP configuration: invalid JSON in " + path);
                return false;
            }
        }

        if (found > 1)
        {
            await output.WriteLineAsync("MCP configuration: duplicate AgentForge servers; keep only one.");
            return false;
        }

        await output.WriteLineAsync("MCP configuration: " + (valid ? "configured" : "missing or invalid"));
        if (!valid)
        {
            await output.WriteLineAsync("  Fix: agentforge vscode init (or review an existing conflicting configuration).");
        }

        return valid;
    }

    private static bool IsValidServer(JsonElement server)
    {
        if (server.ValueKind != JsonValueKind.Object ||
            !server.TryGetProperty("command", out JsonElement command) ||
            command.ValueKind != JsonValueKind.String || command.GetString() != "agentforge" ||
            !server.TryGetProperty("args", out JsonElement args) || args.ValueKind != JsonValueKind.Array ||
            args.GetArrayLength() != 1 || args[0].ValueKind != JsonValueKind.String ||
            args[0].GetString() != "mcp")
        {
            return false;
        }

        return !server.TryGetProperty("cwd", out JsonElement cwd) ||
            (cwd.ValueKind == JsonValueKind.String && cwd.GetString() == "${workspaceFolder}");
    }

    private static async Task<bool> CheckSettingsAsync(AgentForgeSettingsStore store, TextWriter output,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        AgentForgeSettings settings = await store.LoadAsync(cancellationToken);
        bool ready = settings.ModelId != "unconfigured-model" && settings.Provider == BillingProvider.GitHubCopilot;
        await output.WriteLineAsync("Provider/model: " + settings.Provider + " / " + settings.ModelId);
        if (settings.ModelId == "unconfigured-model")
        {
            await output.WriteLineAsync("  Fix: agentforge setup (choose the model you intend to use in Copilot).");
        }

        if (settings.Provider != BillingProvider.GitHubCopilot)
        {
            await output.WriteLineAsync("  Fix: select GitHub Copilot in agentforge setup for a Copilot AI-credit forecast.");
        }

        if (settings.Provider == BillingProvider.GitHubCopilot)
        {
            string allowance = Format(settings.MonthlyAllowance);
            string used = Format(settings.AlreadyUsed);
            string remaining = settings.MonthlyAllowance is { } total && settings.AlreadyUsed is { } consumed
                ? Format(Math.Max(0, total - consumed)) : "unknown";
            await output.WriteLineAsync("Copilot AI credits (account-reported): allowance " + allowance +
                ", used " + used + ", remaining " + remaining);
            if (settings.BalanceObservedAt is { } observed && now - observed > TimeSpan.FromDays(30))
            {
                await output.WriteLineAsync("  Note: account-reported allowance/usage is older than 30 days; refresh via setup.");
            }
        }

        return ready;
    }

    private static string Format(decimal? value) =>
        value?.ToString("N0", CultureInfo.InvariantCulture) ?? "unknown";

    private static bool IsToolOnPath()
    {
        string? environmentPath = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(environmentPath))
        {
            return false;
        }

        string executable = OperatingSystem.IsWindows() ? "agentforge.exe" : "agentforge";
        foreach (string folder in environmentPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (File.Exists(Path.Combine(folder.Trim('"'), executable)))
            {
                return true;
            }
        }

        return false;
    }
}
