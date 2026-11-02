using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using TokenBudget.Application;
using TokenBudget.Domain;

namespace TokenBudget.Mcp;

/// <summary>Read-only preflight MCP tools. Never executes a plan or sends its contents to a provider.</summary>
[McpServerToolType]
public static class TokenBudgetTools
{
    private const int MaxPlanCharacters = 64_000;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [McpServerTool, Description("Estimate cumulative tokens and peak context for a Markdown plan without executing it or uploading it to an AI provider.")]
    public static async Task<string> Preflight(
        [Description("Markdown plan or handover text, up to 64,000 characters.")] string plan,
        [Description("Actual execution model identifier.")] string modelId,
        [Description("Execution target: CodexCli, CodexAppServer, ChatGptWork, OpenAiResponses, OpenAiAgents, or AnthropicMessages.")] string target,
        [Description("Maximum cumulative model tokens for the whole plan.")] long maxCumulativeTokens,
        [Description("Optional maximum peak context tokens.")] long? maxPeakContextTokens = null,
        [Description("Optional repository directory under TOKENBUDGET_REPO_ROOT (metadata only). Not enabled unless configured on the server.")] string? repositoryPath = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plan) || plan.Length > MaxPlanCharacters)
        {
            throw new ArgumentException("Plan must contain 1..64000 characters.", nameof(plan));
        }

        if (string.IsNullOrWhiteSpace(modelId) || modelId.Length > 128)
        {
            throw new ArgumentException("Model ID must contain 1..128 characters.", nameof(modelId));
        }

        if (!Enum.TryParse(target, ignoreCase: true, out ExecutionTarget executionTarget) ||
            !Enum.IsDefined(executionTarget))
        {
            throw new ArgumentException("Unknown execution target.", nameof(target));
        }

        if (maxCumulativeTokens <= 0 || maxPeakContextTokens is <= 0)
        {
            throw new ArgumentException("Budgets must be positive.");
        }

        string? safePath = ResolveRepositoryPath(repositoryPath, Environment.GetEnvironmentVariable("TOKENBUDGET_REPO_ROOT"));
        var request = new AnalyzePlanRequest(plan, modelId, executionTarget,
            new BudgetConstraints(MaxCumulativeTokens: maxCumulativeTokens, MaxPeakContextTokens: maxPeakContextTokens),
            RepositoryPath: safePath, AllowProviderUpload: false, SimulationCount: 1_000);
        EstimateReport report = await AnalyzePlanService.CreateDefault().AnalyzeAsync(request, cancellationToken);
        return EstimateReportFormatter.ToJson(report);
    }

    [McpServerTool, Description("List supported estimation targets, exact-count limitations and available telemetry integrations.")]
    public static string Capabilities() => JsonSerializer.Serialize(ProviderCapabilities.All, Options);

    /// <summary>Allows only configured read-only subtrees and rejects paths escaping through symlinks.</summary>
    public static string? ResolveRepositoryPath(string? requested, string? allowedRoot) =>
        RepositoryPathPolicy.Resolve(requested, allowedRoot);
}
