using TokenBudget.Domain;

namespace TokenBudget.Application;

public sealed class AnalyzePlanService(
    IPlanAnalyzer analyzer,
    IRepositoryImpactAnalyzer repository,
    IInputTokenCounter counter,
    ITokenForecastEngine forecaster)
{
    public async Task<EstimateReport> AnalyzeAsync(AnalyzePlanRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ModelId))
        {
            throw new ArgumentException("Model ID is required for provenance.", nameof(request));
        }

        if (request.Budget is null || request.Budget.RiskTolerance is <= 0 or >= 0.5 ||
            request.SimulationCount is < 1_000 or > 100_000 ||
            request.Budget.MaxCumulativeTokens is <= 0 ||
            request.Budget.MaxPeakContextTokens is <= 0 ||
            request.Budget.ModelContextWindowTokens is <= 0 ||
            request.Budget.MaxApiCostUsd is <= 0)
        {
            throw new ArgumentException("Invalid risk tolerance, simulation count, or budget constraint.", nameof(request));
        }

        PlanGraph graph = await analyzer.AnalyzeAsync(request, cancellationToken);
        RepositoryImpact impact = await repository.InspectAsync(request.RepositoryPath, graph, cancellationToken);
        TokenCountResult initial = await counter.CountAsync(request, cancellationToken);
        return await forecaster.ForecastAsync(request, graph, impact, initial, cancellationToken);
    }

    public static AnalyzePlanService CreateDefault() => new(
        new MarkdownPlanAnalyzer(),
        new ReadOnlyRepositoryImpactAnalyzer(),
        new ProviderAwareInputTokenCounter(),
        new MonteCarloForecastEngine(new ConservativeBudgetPolicy()));
}
