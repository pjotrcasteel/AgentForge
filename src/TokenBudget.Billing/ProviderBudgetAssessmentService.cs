using TokenBudget.Application;
using TokenBudget.Domain;

namespace TokenBudget.Billing;

/// <summary>Joins an offline token forecast to account-specific prices, quota and budget policy.</summary>
public sealed class ProviderBudgetAssessmentService(
    AnalyzePlanService analyzer,
    IProviderPricingProvider pricingProvider,
    IProviderBudgetAdapter budgetAdapter,
    BudgetPolicyEvaluator policy,
    PromptScopeAdvisor scopeAdvisor)
{
    /// <summary>Analyze independently before consulting provider billing, without uploading content.</summary>
    public async Task<ProviderBudgetAssessment> AnalyzeAsync(ProviderBudgetRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (pricingProvider.Provider != request.Account.Provider || budgetAdapter.Provider != request.Account.Provider)
        {
            throw new ArgumentException("Both billing adapters must match the selected provider.");
        }

        EstimateReport estimate = await analyzer.AnalyzeAsync(request.Plan, cancellationToken);
        ModelPricing? pricing = await pricingProvider.GetPricingAsync(request.Plan.ModelId, cancellationToken);
        BudgetUnit unit = pricing?.Unit ?? (request.Account.Provider switch
        {
            BillingProvider.GitHubCopilot => BudgetUnit.GitHubAiCredits,
            BillingProvider.OpenAiApi or BillingProvider.AnthropicApi => BudgetUnit.Usd,
            _ => BudgetUnit.ProviderQuotaUnits
        });
        BudgetSnapshot? quota = await budgetAdapter.GetBudgetAsync(request.Account, unit, cancellationToken);
        ProviderCostForecast cost = policy.Evaluate(estimate, request.Account, pricing, quota,
            request.Policy, request.Assumptions, DateTimeOffset.UtcNow);
        PromptScopeAssessment quality = scopeAdvisor.Assess(request.Plan.Plan, estimate, request.ImprovePrompt);
        IReadOnlyList<ModelCostAlternative> alternatives = pricingProvider is IProviderPricingCatalog catalog
            ? new ModelAlternativeAdvisor().FindCheaperModels(estimate.TokenCategories, pricing,
                await catalog.ListAsync(cancellationToken), request.Assumptions)
            : [];
        return new ProviderBudgetAssessment(estimate, cost, quality, alternatives);
    }
}

/// <summary>Explicit provider/account selection; plan analysis remains an independent concern.</summary>
public sealed record ProviderBudgetRequest(AnalyzePlanRequest Plan, ProviderAccount Account,
    ProviderBudgetPolicy Policy, WorkloadAssumptions Assumptions, bool ImprovePrompt = false);

/// <summary>Complete quality, tokens and provider budget report.</summary>
public sealed record ProviderBudgetAssessment(EstimateReport Tokens, ProviderCostForecast Billing,
    PromptScopeAssessment Prompt, IReadOnlyList<ModelCostAlternative> Alternatives);
