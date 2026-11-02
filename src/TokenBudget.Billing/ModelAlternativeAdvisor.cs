using TokenBudget.Domain;

namespace TokenBudget.Billing;

/// <summary>Identifies price-only model candidates without inventing quality or capability equivalence.</summary>
public sealed class ModelAlternativeAdvisor
{
    /// <summary>Compare catalog pricing against the same token workload and billing unit.</summary>
    public IReadOnlyList<ModelCostAlternative> FindCheaperModels(TokenUsageForecast? tokens, ModelPricing? selected,
        IEnumerable<ModelPricing> candidates, WorkloadAssumptions assumptions)
    {
        CostRange? baseline = ProviderCostCalculator.Calculate(tokens, selected, assumptions);
        if (selected is null || baseline is null || baseline.P90 <= 0)
        {
            return [];
        }

        return candidates
            .Where(model => model.Provider == selected.Provider && model.Unit == selected.Unit && model.Mode == selected.Mode &&
                !string.Equals(model.ModelId, selected.ModelId, StringComparison.OrdinalIgnoreCase))
            .Select(model => (Model: model, Cost: ProviderCostCalculator.Calculate(tokens, model, assumptions)))
            .Where(item => item.Cost is not null && item.Cost.P90 < baseline.P90)
            .OrderBy(item => item.Cost!.P90)
            .Take(3)
            .Select(item => new ModelCostAlternative(item.Model.ModelId, item.Cost!.P90,
                (baseline.P90 - item.Cost.P90) / baseline.P90 * 100m,
                "Lower catalog price only. Verify tool support, context window, output quality and task suitability before switching."))
            .ToArray();
    }
}
