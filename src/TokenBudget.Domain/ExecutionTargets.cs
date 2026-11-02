namespace TokenBudget.Domain;

/// <summary>Provider environment for a plan estimate.</summary>
public enum ExecutionTarget
{
    OpenAiResponses,
    OpenAiAgents,
    CodexCli,
    CodexAppServer,
    ChatGptWork,
    AnthropicMessages,
    GitHubCopilot
}

/// <summary>Level of support for a forecast.</summary>
public enum ForecastConfidence
{
    Low,
    Medium,
    High
}

/// <summary>Decision returned after applying a token budget.</summary>
public enum BudgetDecision
{
    Pass,
    Warning,
    Reject,
    InsufficientEvidence
}

/// <summary>How the initial token count was obtained.</summary>
public enum MeasurementSource
{
    ProviderCount,
    LocalTokenizer,
    Heuristic,
    ReportedUsage,
    Manual
}
