using TokenBudget.Domain;

namespace TokenBudget.Billing;

/// <summary>Account-scoped P90 decision that cannot be overridden by a language model.</summary>
public sealed class BudgetPolicyEvaluator
{
    /// <summary>Evaluate a completed deterministic estimate against a separate provider budget.</summary>
    public ProviderCostForecast Evaluate(EstimateReport estimate, ProviderAccount account,
        ModelPricing? pricing, BudgetSnapshot? quota, ProviderBudgetPolicy policy,
        WorkloadAssumptions assumptions, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(estimate);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(policy);
        if (string.IsNullOrWhiteSpace(account.AccountId) || policy.ReservedUnits < 0 ||
            policy.MaxTaskUnits is <= 0 || policy.MaxAtRiskFraction is <= 0 or > 1 ||
            (policy.MaxDataAge.HasValue && policy.MaxDataAge.Value <= TimeSpan.Zero))
        {
            throw new ArgumentException("Account or provider budget policy is invalid.");
        }

        if (pricing is not null && (pricing.Provider != account.Provider ||
            !string.Equals(pricing.ModelId, estimate.ModelId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Model pricing does not belong to the selected provider and model.");
        }

        if (quota is not null && quota.Account != account)
        {
            throw new ArgumentException("The quota belongs to a different provider account.");
        }

        if (quota is not null && (quota.Allowance is < 0 || quota.Consumed is < 0 || quota.ReportedRemaining is < 0))
        {
            throw new ArgumentException("Account balances cannot be negative.");
        }

        if (pricing is not null && ((pricing.Mode == BillingMode.ApiUsd && pricing.Unit != BudgetUnit.Usd) ||
            (pricing.Mode == BillingMode.MeteredAiCredits && pricing.Unit != BudgetUnit.GitHubAiCredits)))
        {
            throw new ArgumentException("Pricing mode is incompatible with its billing unit.");
        }

        if (pricing is not null && pricing.Unit == BudgetUnit.GitHubAiCredits && account.Provider != BillingProvider.GitHubCopilot)
        {
            throw new ArgumentException("GitHub AI-credit prices cannot be applied to another provider account.");
        }

        BudgetUnit unit = pricing?.Unit ?? quota?.Unit ?? DefaultUnit(account.Provider);
        if (quota is not null && quota.Unit != unit)
        {
            throw new ArgumentException("Quota and pricing must be expressed in the same billing unit.");
        }

        var warnings = new List<string>();
        var recommendations = new List<string>();
        TimeSpan freshness = policy.MaxDataAge ?? TimeSpan.FromHours(24);
        bool priceStale = pricing is not null && (pricing.ObservedAt > now || now - pricing.ObservedAt > freshness);
        bool quotaStale = quota is not null && quota.IsStale(now, freshness);
        CostRange? costs = ProviderCostCalculator.Calculate(estimate.TokenCategories, pricing, assumptions);
        decimal? remaining = quota?.Remaining;
        decimal? afterP50 = remaining.HasValue && costs is not null ? remaining.Value - costs.P50 : null;
        decimal? afterP90 = remaining.HasValue && costs is not null ? remaining.Value - costs.P90 : null;
        decimal? atRisk = remaining > 0 && costs is not null ? costs.P90 / remaining.Value : null;
        bool approval = estimate.Milestones.Any(static milestone => milestone.RequiresApproval);

        if (pricing is null || costs is null)
        {
            warnings.Add("Current model pricing or billable token categories unavailable; cost is unknown, not free.");
        }

        if (quota is null || (remaining is null && quota.Unlimited == false))
        {
            warnings.Add("Remaining allowance unavailable; absence of quota information is not zero credits.");
        }

        if (quota?.Unlimited == true)
        {
            warnings.Add("Account reports unlimited entitlement; provider admission and task limits still apply.");
        }

        if (priceStale || quotaStale)
        {
            warnings.Add("Pricing or quota information is stale; refresh it before a binding budget decision.");
        }

        if (assumptions.AssumedCachedInputFraction > 0)
        {
            warnings.Add("Cached input share is a user-supplied assumption, not measured cache reuse.");
        }

        if (account.Provider is BillingProvider.ChatGptSubscription or BillingProvider.CodexSubscription)
        {
            warnings.Add("Subscription activity may be subject to dynamic limits that have no public credit-to-token formula.");
        }

        ProviderBudgetDecision decision;
        if (estimate.Decision == BudgetDecision.Reject)
        {
            decision = ProviderBudgetDecision.Defer;
            recommendations.Add("The existing deterministic token budget rejects this workload.");
        }
        else if (costs is null || priceStale || quotaStale ||
            (remaining is null && quota?.Unlimited != true) || estimate.Decision == BudgetDecision.InsufficientEvidence)
        {
            decision = ProviderBudgetDecision.InsufficientEvidence;
            recommendations.Add("Refresh live billing information or supply an explicitly labeled account balance and pricing snapshot.");
        }
        else if (policy.MaxTaskUnits.HasValue && costs.P90 > policy.MaxTaskUnits.Value)
        {
            decision = ProviderBudgetDecision.Defer;
            recommendations.Add("P90 cost exceeds the per-task budget. Narrow the task or increase its approved limit.");
        }
        else if (remaining.HasValue && costs.P90 + policy.ReservedUnits > remaining.Value)
        {
            decision = ProviderBudgetDecision.Defer;
            recommendations.Add("P90 cost plus the protected reserve exceeds the account's remaining allowance.");
        }
        else if (estimate.Decision == BudgetDecision.Warning ||
            (atRisk.HasValue && atRisk.Value > (policy.MaxAtRiskFraction ?? 0.50m)))
        {
            decision = ProviderBudgetDecision.NarrowScope;
            recommendations.Add("Split the plan at dependency-safe milestones, and reassess each independently.");
        }
        else if (atRisk >= 0.25m)
        {
            decision = ProviderBudgetDecision.ReserveBudget;
            recommendations.Add("Reserve budget for tests, retries and debugging before starting.");
        }
        else
        {
            decision = ProviderBudgetDecision.Execute;
        }

        if (approval)
        {
            recommendations.Add("Preserve every mandatory human approval gate regardless of the budget decision.");
        }

        // $0.01 is the currently documented *metered value* of one Copilot AI credit, not an invoice.
        decimal? valueUsd = costs is not null && unit == BudgetUnit.GitHubAiCredits ? costs.P50 * 0.01m :
            costs is not null && unit == BudgetUnit.Usd ? costs.P50 : null;

        return new ProviderCostForecast(account, estimate.ModelId, pricing?.Mode ?? BillingMode.Advisory,
            unit, estimate.TokenCategories, costs, valueUsd, remaining, afterP50, afterP90,
            atRisk, quota?.ResetsAt, priceStale, quotaStale, decision, approval, warnings,
            recommendations, pricing?.Source ?? "unavailable", quota?.Source ?? "unavailable");
    }

    private static BudgetUnit DefaultUnit(BillingProvider provider) => provider switch
    {
        BillingProvider.GitHubCopilot => BudgetUnit.GitHubAiCredits,
        BillingProvider.OpenAiApi or BillingProvider.AnthropicApi => BudgetUnit.Usd,
        _ => BudgetUnit.ProviderQuotaUnits
    };
}
