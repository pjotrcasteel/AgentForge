using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace TokenBudget.Cli;

/// <summary>Hosts the read-only AgentForge MCP tools over standard I/O for VS Code Copilot.</summary>
internal static class AgentForgeMcpServer
{
    public static async Task<int> RunAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        // Standard output belongs exclusively to MCP JSON-RPC; diagnostics must not corrupt the protocol.
        builder.Logging.ClearProviders();
        builder.Services.AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        using IHost host = builder.Build();
        await host.RunAsync();
        return 0;
    }
}
