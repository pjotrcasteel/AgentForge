using TokenBudget.Domain;

namespace TokenBudget.Api.Endpoints;

/// <summary>An agent-review request with an explicit model and upload permission.</summary>
public sealed record ApiAgentPreflightRequest(
    AnalyzePlanRequest Estimate,
    string ReviewModelId,
    bool AllowAiUpload);
