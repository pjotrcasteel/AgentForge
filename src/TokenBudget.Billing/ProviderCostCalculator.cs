using TokenBudget.Domain;

namespace TokenBudget.Billing;

/// <summary>Pure decimal accounting over already-estimated token categories; no provider IO or rates are embedded.</summary>
public static class ProviderCostCalculator
{
    /// <summary>Compute costs with proportional pricing per batch, without premature integer or monetary rounding.</summary>
    public static CostRange? Calculate(TokenUsageForecast? workload, ModelPricing? pricing, WorkloadAssumptions assumptions)
    {
        if (workload is null || pricing is null || pricing.BatchTokens <= 0 ||
            pricing.Mode is not (BillingMode.MeteredAiCredits or BillingMode.ApiUsd) ||
            pricing.InputPerBatch is null || pricing.OutputPerBatch is null ||
            pricing.InputPerBatch < 0 || pricing.OutputPerBatch < 0 ||
            pricing.AdditionalFixedCost < 0 ||
            assumptions.AssumedCachedInputFraction is < 0 or > 1)
        {
            return null;
        }

        if (assumptions.AssumedCachedInputFraction > 0 && pricing.CachedInputPerBatch is null)
        {
            return null;
        }

        if (pricing.CachedInputPerBatch is < 0)
        {
            return null;
        }

        Validate(workload.InputTokens);
        Validate(workload.OutputTokens);
        return new CostRange(
            CalculateOne(workload.InputTokens.P10, workload.OutputTokens.P10, pricing, assumptions),
            CalculateOne(workload.InputTokens.P50, workload.OutputTokens.P50, pricing, assumptions),
            CalculateOne(workload.InputTokens.P90, workload.OutputTokens.P90, pricing, assumptions));
    }

    private static decimal CalculateOne(long input, long output, ModelPricing pricing, WorkloadAssumptions assumptions)
    {
        decimal cached = input * assumptions.AssumedCachedInputFraction;
        decimal uncached = input - cached;
        return checked((uncached * pricing.InputPerBatch!.Value +
            cached * (pricing.CachedInputPerBatch ?? 0) + output * pricing.OutputPerBatch!.Value) /
            pricing.BatchTokens + pricing.AdditionalFixedCost);
    }

    private static void Validate(TokenRange values)
    {
        if (values.P10 < 0 || values.P10 > values.P50 || values.P50 > values.P90)
        {
            throw new ArgumentException("Token quantiles must be nonnegative and ordered.");
        }
    }
}
