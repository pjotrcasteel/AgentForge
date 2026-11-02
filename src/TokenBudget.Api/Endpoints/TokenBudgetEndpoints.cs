using TokenBudget.Agent;
using TokenBudget.Application;
using TokenBudget.Billing;
using TokenBudget.Domain;

namespace TokenBudget.Api.Endpoints;

/// <summary>Stable HTTP endpoints; estimation and review logic live in reusable services.</summary>
internal static class TokenBudgetEndpoints
{
    public static WebApplication MapTokenBudgetEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/v1/capabilities", () => Results.Ok(ProviderCapabilities.All));
        app.MapPost("/v1/estimates", AnalyzeAsync);
        app.MapPost("/v1/provider-budgets", ProviderBudgetAsync);
        app.MapPost("/v1/agent/preflight", PreflightAsync);
        return app;
    }

    private static async Task<IResult> AnalyzeAsync(
        AnalyzePlanRequest request, AnalyzePlanService service, CancellationToken token)
    {
        try
        {
            AnalyzePlanRequest safeRequest = request with { RepositoryPath = ResolveRepository(request.RepositoryPath) };
            EstimateReport report = await service.AnalyzeAsync(safeRequest, token);
            return Results.Ok(report);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or InvalidOperationException)
        {
            return Results.Problem(title: "Repository access denied", detail: exception.Message, statusCode: 403);
        }
        catch (DirectoryNotFoundException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> PreflightAsync(
        ApiAgentPreflightRequest request, AgentPreflightService service, CancellationToken token)
    {
        if (!request.AllowAiUpload || string.IsNullOrWhiteSpace(request.ReviewModelId))
        {
            return Results.BadRequest(new { error = "Explicit allowAiUpload and reviewModelId required." });
        }

        try
        {
            AnalyzePlanRequest safeRequest = request.Estimate with
            {
                RepositoryPath = ResolveRepository(request.Estimate.RepositoryPath)
            };
            AgentPreflightResult result = await service.RunAsync(safeRequest,
                new AgentReviewOptions(request.ReviewModelId, request.AllowAiUpload), token);
            return Results.Ok(result);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(title: "AI review unavailable", detail: exception.Message, statusCode: 503);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Results.Problem(title: "Repository access denied", detail: exception.Message, statusCode: 403);
        }
        catch (DirectoryNotFoundException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> ProviderBudgetAsync(ApiProviderBudgetRequest request,
        AnalyzePlanService analyzer, BudgetPolicyEvaluator policy, PromptScopeAdvisor scope, CancellationToken token)
    {
        try
        {
            if (request.Account is null || request.Plan is null || request.Policy is null || request.Assumptions is null)
            {
                return Results.BadRequest(new { error = "Plan, account, policy and assumptions are required." });
            }

            AnalyzePlanRequest safePlan = request.Plan with
            {
                RepositoryPath = ResolveRepository(request.Plan.RepositoryPath)
            };
            BillingProvider provider = request.Account.Provider;
            var service = new ProviderBudgetAssessmentService(analyzer,
                new ConfiguredPricingProvider(provider, request.Pricing is null ? [] : [request.Pricing]),
                new ConfiguredBudgetAdapter(provider, request.Quota is null ? [] : [request.Quota]),
                policy, scope);
            ProviderBudgetAssessment result = await service.AnalyzeAsync(new ProviderBudgetRequest(safePlan,
                request.Account, request.Policy, request.Assumptions, request.ImprovePrompt), token);
            return Results.Ok(result);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (UnauthorizedAccessException exception)
        {
            return Results.Problem(title: "Repository access denied", detail: exception.Message, statusCode: 403);
        }
        catch (DirectoryNotFoundException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static string? ResolveRepository(string? repositoryPath) =>
        RepositoryPathPolicy.Resolve(repositoryPath, Environment.GetEnvironmentVariable("TOKENBUDGET_REPO_ROOT"));
}

/// <summary>Input snapshots and optional improved prompt request for the authenticated local API.</summary>
internal sealed record ApiProviderBudgetRequest(AnalyzePlanRequest Plan, ProviderAccount Account,
    ProviderBudgetPolicy Policy, WorkloadAssumptions Assumptions, ModelPricing? Pricing = null,
    BudgetSnapshot? Quota = null, bool ImprovePrompt = false);
