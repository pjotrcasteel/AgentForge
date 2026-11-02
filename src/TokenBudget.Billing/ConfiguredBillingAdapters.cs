namespace TokenBudget.Billing;

/// <summary>User-supplied or externally fetched pricing snapshots; never preloaded with invented rates.</summary>
public sealed class ConfiguredPricingProvider(BillingProvider provider, IReadOnlyCollection<ModelPricing> prices)
    : IProviderPricingProvider, IProviderPricingCatalog
{
    /// <inheritdoc />
    public BillingProvider Provider => provider;

    /// <inheritdoc />
    public Task<ModelPricing?> GetPricingAsync(string modelId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(prices.FirstOrDefault(price => price.Provider == provider &&
            string.Equals(price.ModelId, modelId, StringComparison.OrdinalIgnoreCase)));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ModelPricing>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ModelPricing>>(prices.Where(price => price.Provider == provider).ToArray());
    }
}

/// <summary>Explicitly supplied account balances, scoped by both provider, account and unit.</summary>
public sealed class ConfiguredBudgetAdapter(BillingProvider provider, IReadOnlyCollection<BudgetSnapshot> snapshots)
    : IProviderBudgetAdapter
{
    /// <inheritdoc />
    public BillingProvider Provider => provider;

    /// <inheritdoc />
    public Task<BudgetSnapshot?> GetBudgetAsync(ProviderAccount account, BudgetUnit unit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (account.Provider != provider)
        {
            throw new ArgumentException("Provider mismatch.", nameof(account));
        }

        return Task.FromResult(snapshots.FirstOrDefault(snapshot => snapshot.Account == account && snapshot.Unit == unit));
    }
}

/// <summary>For API pricing and subscriptions with no supported live billing API; never guesses.</summary>
public sealed class UnavailableBillingAdapter(BillingProvider provider) : IProviderBudgetAdapter, IProviderPricingProvider
{
    /// <inheritdoc />
    public BillingProvider Provider => provider;

    /// <inheritdoc />
    public Task<ModelPricing?> GetPricingAsync(string modelId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ModelPricing?>(null);
    }

    /// <inheritdoc />
    public Task<BudgetSnapshot?> GetBudgetAsync(ProviderAccount account, BudgetUnit unit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<BudgetSnapshot?>(null);
    }
}
