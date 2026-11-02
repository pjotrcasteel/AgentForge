using System.Globalization;
using GitHub.Copilot;
using TokenBudget.Billing;
using TokenBudget.Domain;

namespace TokenBudget.Provider.Copilot;

/// <summary>Live SDK pricing catalog; the caller owns and starts the authenticated CopilotClient.</summary>
public sealed class CopilotSdkPricingProvider(CopilotClient client) : IProviderPricingProvider, IProviderPricingCatalog
{
    /// <inheritdoc />
    public BillingProvider Provider => BillingProvider.GitHubCopilot;

    /// <inheritdoc />
    public async Task<ModelPricing?> GetPricingAsync(string modelId, CancellationToken cancellationToken = default)
    {
        return (await ListAsync(cancellationToken)).FirstOrDefault(item =>
            string.Equals(item.ModelId, modelId, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModelPricing>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var catalog = await client.Rpc.Models.ListAsync();
        DateTimeOffset observed = DateTimeOffset.UtcNow;
        // CachePrice is present but deprecated in SDK 1.0.27; retain nullable handling until a replacement is stable.
#pragma warning disable GHCP001
        return catalog.Models.Where(model => model.Billing?.TokenPrices is not null).Select(model =>
        {
            var prices = model.Billing!.TokenPrices!;
            return new ModelPricing(BillingProvider.GitHubCopilot, model.Id, BillingMode.MeteredAiCredits,
                BudgetUnit.GitHubAiCredits, DecimalOrNull(prices.InputPrice), DecimalOrNull(prices.OutputPrice),
                DecimalOrNull(prices.CachePrice), LongOrZero(prices.BatchSize), observed,
                "GitHub Copilot SDK models.list");
        }).ToArray();
#pragma warning restore GHCP001
    }

    internal static decimal? DecimalOrNull(object? value) => value is null
        ? null : Convert.ToDecimal(value, CultureInfo.InvariantCulture);

    internal static long LongOrZero(object? value) => value is null
        ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
}

/// <summary>
/// Official account.getQuota currently exposes premium interactions, not a verified metered AI-credit balance.
/// Never reinterpret this quota as interchangeable credits.
/// </summary>
public sealed class CopilotSdkQuotaProvider(CopilotClient client) : IProviderBudgetAdapter
{
    /// <inheritdoc />
    public BillingProvider Provider => BillingProvider.GitHubCopilot;

    /// <inheritdoc />
    public async Task<BudgetSnapshot?> GetBudgetAsync(ProviderAccount account, BudgetUnit unit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (account.Provider != Provider)
        {
            throw new ArgumentException("Not a GitHub Copilot account.", nameof(account));
        }

        // The SDK's quota snapshots commonly report *premium interactions*, not AI credits.
        if (unit != BudgetUnit.PremiumInteractions)
        {
            return null;
        }

        var result = await client.Rpc.Account.GetQuotaAsync();
        if (!result.QuotaSnapshots.TryGetValue("premium_interactions", out var premium))
        {
            return null;
        }

        decimal? limit = CopilotSdkPricingProvider.DecimalOrNull(premium.EntitlementRequests);
        bool unlimited = limit == -1;
        decimal? used = CopilotSdkPricingProvider.DecimalOrNull(premium.UsedRequests);
        return new BudgetSnapshot(account, unit, unlimited ? null : limit, used, null,
            DateTimeOffset.UtcNow, premium.ResetDate, "GitHub Copilot SDK account.getQuota", unlimited);
    }
}

/// <summary>Session metrics and ephemeral per-call observations, without starting any AI task or persisting prompts.</summary>
public sealed class CopilotSdkUsageCollector(Func<ProviderAccount, string, CopilotSession?> sessionResolver) : IProviderUsageCollector
{
    /// <inheritdoc />
    public BillingProvider Provider => BillingProvider.GitHubCopilot;

    /// <inheritdoc />
    public async Task<ActualUsageObservation?> ReadUsageAsync(ProviderAccount account, string sessionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (account.Provider != Provider)
        {
            throw new ArgumentException("Not a GitHub Copilot account.", nameof(account));
        }

        CopilotSession? session = sessionResolver(account, sessionId);
        if (session is null)
        {
            return null;
        }

#pragma warning disable GHCP001 // Official SDK marks session.usage.getMetrics experimental.
        var metrics = await session.Rpc.Usage.GetMetricsAsync();
#pragma warning restore GHCP001
        long input = 0;
        long output = 0;
        foreach (var model in metrics.ModelMetrics.Values)
        {
            input = checked(input + model.Usage.InputTokens);
            output = checked(output + model.Usage.OutputTokens);
        }

        // Nano-AIU convention is documented as provisional; keep the conversion explicitly identified.
        decimal? credits = CopilotSdkPricingProvider.DecimalOrNull(metrics.TotalNanoAiu) / 1_000_000_000m;
        decimal? premium = CopilotSdkPricingProvider.DecimalOrNull(metrics.TotalPremiumRequestCost);
        return new ActualUsageObservation(account, sessionId, new UsageBreakdown(input, output, null, null, null),
            credits, premium, DateTimeOffset.UtcNow, "GitHub Copilot SDK session.usage.getMetrics (provisional nano-AIU conversion)",
            metrics.ModelMetrics.Keys.ToArray(), IsCumulative: true, CreditsVerified: false);
    }

    /// <summary>Subscribe to non-replayed per-call assistant.usage events. Caller must dispose the subscription.</summary>
    public IDisposable ObservePerCall(ProviderAccount account, string sessionId, CopilotSession session,
        Action<ActualUsageObservation> onObservation)
    {
        ArgumentNullException.ThrowIfNull(onObservation);
        if (account.Provider != Provider)
        {
            throw new ArgumentException("Not a GitHub Copilot account.", nameof(account));
        }

        if (!ReferenceEquals(sessionResolver(account, sessionId), session))
        {
            throw new ArgumentException("The Copilot session is not registered for this provider account.", nameof(session));
        }

        return session.On<AssistantUsageEvent>(evt =>
        {
            var data = evt.Data;
#pragma warning disable GHCP001 // The per-call premium request multiplier is an experimental SDK field.
            onObservation(new ActualUsageObservation(account, sessionId,
                new UsageBreakdown(data.InputTokens ?? 0, data.OutputTokens ?? 0, null, null, null),
                null, CopilotSdkPricingProvider.DecimalOrNull(data.Cost), DateTimeOffset.UtcNow,
                "GitHub Copilot SDK assistant.usage (ephemeral per-call event)",
                [data.Model], IsCumulative: false, CreditsVerified: false));
#pragma warning restore GHCP001
        });
    }
}
