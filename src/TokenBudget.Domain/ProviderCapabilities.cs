namespace TokenBudget.Domain;

/// <summary>Implemented capability state, not a declaration of what upstream APIs theoretically support.</summary>
public sealed record ProviderCapability(
    ExecutionTarget Target,
    bool CanCountExactInput,
    bool CanIngestUsage,
    bool CanAnalyzeOffline,
    string Limitations);

public static class ProviderCapabilities
{
    public static IReadOnlyList<ProviderCapability> All { get; } =
    [
        new(ExecutionTarget.OpenAiResponses, true, false, true, "Exact count of submitted plan text only, requires opt-in and OPENAI_API_KEY."),
        new(ExecutionTarget.OpenAiAgents, false, false, true, "Session and subagent usage ingestion not implemented."),
        new(ExecutionTarget.CodexCli, false, true, true, "Codex JSONL inspection and opt-in SQLite ingestion; calibration statistics require matched completed runs."),
        new(ExecutionTarget.CodexAppServer, false, false, true, "Cumulative snapshot reconciliation not implemented yet."),
        new(ExecutionTarget.ChatGptWork, false, false, true, "External preflight only; internal Work token telemetry unavailable."),
        new(ExecutionTarget.AnthropicMessages, true, false, true, "Exact count of submitted plan text only, requires opt-in and ANTHROPIC_API_KEY."),
        new(ExecutionTarget.GitHubCopilot, false, true, true,
            "Offline token priors and SDK model pricing, premium quota and optional usage. Metered AI-credit balance may need explicit account figures.")
    ];
}
