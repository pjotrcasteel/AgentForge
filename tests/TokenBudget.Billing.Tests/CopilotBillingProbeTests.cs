using Microsoft.VisualStudio.TestTools.UnitTesting;
using TokenBudget.Billing;
using TokenBudget.Provider.Copilot;

namespace TokenBudget.Billing.Tests;

[TestClass]
public sealed class CopilotBillingProbeTests
{
    private static readonly ProviderAccount Account = new(BillingProvider.GitHubCopilot, "pjotr-personal");
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task PremiumQuotaNeverBecomesAnAiCreditBalance()
    {
        CopilotBillingProbeReport report = await ProbeAsync(Price(), null, Premium(100, 20));

        Assert.AreEqual(CopilotProbeState.Available, report.PremiumQuotaState);
        Assert.AreEqual(80m, report.PremiumInteractionQuota!.Remaining);
        Assert.AreEqual(CopilotProbeState.Unavailable, report.CreditBalanceState);
        Assert.IsNull(report.CreditBalance);
    }

    [TestMethod]
    public async Task AccountReportedCreditsAreLabeledAndRemainDistinctFromPremiumQuota()
    {
        CopilotBillingProbeReport report = await ProbeAsync(Price(), null, Premium(100, 20), Reported());

        Assert.AreEqual(CopilotProbeState.AccountReported, report.CreditBalanceState);
        Assert.AreEqual(21_000m, report.CreditBalance!.Remaining);
        Assert.AreEqual(80m, report.PremiumInteractionQuota!.Remaining);
        Assert.IsFalse(report.AuthenticatedAccountIdentityVerified);
    }

    [TestMethod]
    public async Task PricesReflectAChangeFromTheInjectedLiveCatalog()
    {
        CopilotBillingProbeReport earlier = await ProbeAsync(Price(input: 0.75m), null, null);
        CopilotBillingProbeReport later = await ProbeAsync(Price(input: 1.50m), null, null);

        Assert.AreEqual(CopilotProbeState.Available, earlier.ModelPriceState);
        Assert.AreEqual(earlier.Pricing!.InputPerBatch * 2, later.Pricing!.InputPerBatch);
    }

    [TestMethod]
    public async Task SdkReportedAiCreditQuotaTakesPrecedenceOverManualSnapshot()
    {
        CopilotBillingProbeReport report = await ProbeAsync(Price(), Credits(25_000, 5_000), null, Reported());

        Assert.AreEqual(CopilotProbeState.Available, report.CreditBalanceState);
        Assert.AreEqual(20_000m, report.CreditBalance!.Remaining);
        Assert.AreEqual("sdk quota", report.CreditBalance.Source);
    }

    [TestMethod]
    public async Task MissingCatalogEntryDoesNotImplyFreeModel()
    {
        CopilotBillingProbeReport report = await ProbeAsync(null, null, null);

        Assert.AreEqual(CopilotProbeState.Unavailable, report.ModelPriceState);
        Assert.IsNull(report.Pricing);
        Assert.IsTrue(report.Warnings.Any(message => message.Contains("no current SDK", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task PartialPricingWithMissingOutputRateIsNotMarkedAvailable()
    {
        CopilotBillingProbeReport report = await ProbeAsync(Price() with { OutputPerBatch = null }, null, null);

        Assert.AreEqual(CopilotProbeState.Partial, report.ModelPriceState);
    }

    [TestMethod]
    public async Task StalePricingIsNotMarkedCurrent()
    {
        CopilotBillingProbeReport report = await ProbeAsync(Price() with { ObservedAt = Now.AddDays(-2) }, null, null);

        Assert.AreEqual(CopilotProbeState.Partial, report.ModelPriceState);
    }

    [TestMethod]
    public async Task MissingAccountReportedUsedValueRemainsUnknown()
    {
        CopilotBillingProbeReport report = await ProbeAsync(Price(), null, null,
            Reported() with { Consumed = null });

        Assert.AreEqual(CopilotProbeState.Partial, report.CreditBalanceState);
        Assert.IsNull(report.CreditBalance!.Remaining);
    }

    [TestMethod]
    public async Task ExplicitZeroRemainingIsNotConfusedWithUnavailable()
    {
        CopilotBillingProbeReport report = await ProbeAsync(Price(), null, null,
            Reported() with { ReportedRemaining = 0 });

        Assert.AreEqual(CopilotProbeState.AccountReported, report.CreditBalanceState);
        Assert.AreEqual(0m, report.CreditBalance!.Remaining);
    }

    [TestMethod]
    public async Task StaleReportedQuotaRemainsVisiblyPartial()
    {
        CopilotBillingProbeReport report = await ProbeAsync(Price(), null, null,
            Reported() with { ObservedAt = Now.AddDays(-2) });

        Assert.AreEqual(CopilotProbeState.Partial, report.CreditBalanceState);
        Assert.IsTrue(report.Warnings.Any(message => message.Contains("stale", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ExpiredQuotaResetCannotBeTrustedAsCurrent()
    {
        CopilotBillingProbeReport report = await ProbeAsync(Price(), null,
            Premium(100, 20) with { ResetsAt = Now.AddSeconds(-1) });

        Assert.AreEqual(CopilotProbeState.Partial, report.PremiumQuotaState);
    }

    [TestMethod]
    public async Task UnavailablePricingDoesNotBlockIndependentPremiumQuota()
    {
        var prices = new FakePricingProvider(null) { Failure = new TimeoutException() };
        var quotas = new FakeBudgetAdapter { Premium = Premium(100, 20) };
        CopilotBillingProbeReport report = await new CopilotBillingProbeService(prices, quotas)
            .ProbeAsync(Account, "model-1", Now);

        Assert.AreEqual(CopilotProbeState.Failed, report.ModelPriceState);
        Assert.AreEqual(CopilotProbeState.Available, report.PremiumQuotaState);
        Assert.IsTrue(report.Warnings.Any(message => message.Contains("TimeoutException", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task QuotaFailureRetainsExplicitAccountReportedBalance()
    {
        var quotas = new FakeBudgetAdapter { CreditFailure = new TimeoutException(), Premium = Premium(100, 20) };
        CopilotBillingProbeReport report = await new CopilotBillingProbeService(new FakePricingProvider(Price()), quotas)
            .ProbeAsync(Account, "model-1", Now, Reported());

        Assert.AreEqual(CopilotProbeState.AccountReported, report.CreditBalanceState);
        Assert.AreEqual(21_000m, report.CreditBalance!.Remaining);
        Assert.AreEqual(CopilotProbeState.Available, report.PremiumQuotaState);
    }

    [TestMethod]
    public async Task PremiumQuotaFailureDoesNotEraseIndependentModelPricing()
    {
        var quotas = new FakeBudgetAdapter { PremiumFailure = new TimeoutException() };
        CopilotBillingProbeReport report = await new CopilotBillingProbeService(new FakePricingProvider(Price()), quotas)
            .ProbeAsync(Account, "model-1", Now);

        Assert.AreEqual(CopilotProbeState.Failed, report.PremiumQuotaState);
        Assert.AreEqual(CopilotProbeState.Available, report.ModelPriceState);
    }

    [TestMethod]
    public async Task MismatchedSdkModelOrProviderCannotSupplyPrices()
    {
        CopilotBillingProbeReport wrongModel = await ProbeAsync(Price() with { ModelId = "foreign" }, null, null);
        CopilotBillingProbeReport wrongProvider = await ProbeAsync(Price() with
        {
            Provider = BillingProvider.OpenAiApi
        }, null, null);

        Assert.AreEqual(CopilotProbeState.Failed, wrongModel.ModelPriceState);
        Assert.AreEqual(CopilotProbeState.Failed, wrongProvider.ModelPriceState);
    }

    [TestMethod]
    public async Task WrongAccountOrCurrencyCannotBeConfiguredAsCopilotCredits()
    {
        var probe = new CopilotBillingProbeService(new FakePricingProvider(Price()), new FakeBudgetAdapter());
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
        {
            await probe.ProbeAsync(Account, "model-1", Now, Reported() with
            {
                Account = new ProviderAccount(BillingProvider.GitHubCopilot, "someone-else")
            });
        });
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
        {
            await probe.ProbeAsync(Account, "model-1", Now, Reported() with { Unit = BudgetUnit.Usd });
        });
    }

    [TestMethod]
    public async Task NegativeAccountReportedBalanceIsRejected()
    {
        var probe = new CopilotBillingProbeService(new FakePricingProvider(Price()), new FakeBudgetAdapter());
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
        {
            await probe.ProbeAsync(Account, "model-1", Now, Reported() with { Consumed = -1 });
        });
    }

    [TestMethod]
    public async Task LiveSdkQuotaWithWrongUnitOrAccountIsRejectedAsFailed()
    {
        CopilotBillingProbeReport wrongUnit = await ProbeAsync(Price(), Credits(100, 10) with
        {
            Unit = BudgetUnit.Usd
        }, null);
        CopilotBillingProbeReport wrongAccount = await ProbeAsync(Price(), Credits(100, 10) with
        {
            Account = new ProviderAccount(BillingProvider.GitHubCopilot, "other")
        }, null);

        Assert.AreEqual(CopilotProbeState.Failed, wrongUnit.CreditBalanceState);
        Assert.AreEqual(CopilotProbeState.Failed, wrongAccount.CreditBalanceState);
    }

    private static async Task<CopilotBillingProbeReport> ProbeAsync(ModelPricing? price, BudgetSnapshot? credits,
        BudgetSnapshot? premium, BudgetSnapshot? reported = null)
    {
        var probe = new CopilotBillingProbeService(new FakePricingProvider(price), new FakeBudgetAdapter
        {
            Credits = credits,
            Premium = premium
        });
        return await probe.ProbeAsync(Account, "model-1", Now, reported);
    }

    private static ModelPricing Price(decimal input = 0.75m) => new(BillingProvider.GitHubCopilot,
        "model-1", BillingMode.MeteredAiCredits, BudgetUnit.GitHubAiCredits,
        input, 4m, null, 1000, Now, "SDK models.list");

    private static BudgetSnapshot Credits(decimal allowance, decimal consumed) => new(Account,
        BudgetUnit.GitHubAiCredits, allowance, consumed, null, Now, Now.AddDays(28), "sdk quota");

    private static BudgetSnapshot Premium(decimal allowance, decimal consumed) => new(Account,
        BudgetUnit.PremiumInteractions, allowance, consumed, null, Now, Now.AddDays(28), "SDK account.getQuota");

    private static BudgetSnapshot Reported() => Credits(30_000, 9_000) with
    {
        Source = "account-reported"
    };

    private sealed class FakePricingProvider(ModelPricing? pricing) : IProviderPricingProvider
    {
        public BillingProvider Provider => BillingProvider.GitHubCopilot;
        public Exception? Failure { get; init; }

        public Task<ModelPricing?> GetPricingAsync(string modelId, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(pricing);
        }
    }

    private sealed class FakeBudgetAdapter : IProviderBudgetAdapter
    {
        public BillingProvider Provider => BillingProvider.GitHubCopilot;
        public BudgetSnapshot? Credits { get; init; }
        public BudgetSnapshot? Premium { get; init; }
        public Exception? CreditFailure { get; init; }
        public Exception? PremiumFailure { get; init; }

        public Task<BudgetSnapshot?> GetBudgetAsync(ProviderAccount account, BudgetUnit unit,
            CancellationToken cancellationToken = default)
        {
            if (unit == BudgetUnit.GitHubAiCredits && CreditFailure is not null)
            {
                throw CreditFailure;
            }

            if (unit == BudgetUnit.PremiumInteractions && PremiumFailure is not null)
            {
                throw PremiumFailure;
            }

            return Task.FromResult(unit == BudgetUnit.GitHubAiCredits ? Credits : Premium);
        }
    }
}
