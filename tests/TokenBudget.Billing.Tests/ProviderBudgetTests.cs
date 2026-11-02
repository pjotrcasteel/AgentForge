using Microsoft.VisualStudio.TestTools.UnitTesting;
using TokenBudget.Billing;
using TokenBudget.Domain;

namespace TokenBudget.Billing.Tests;

[TestClass]
public sealed class ProviderBudgetTests
{
    private static readonly ProviderAccount Copilot = new(BillingProvider.GitHubCopilot, "pjotr-personal");
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Credits_AreCalculatedFromInputOutputAndCacheWithoutDoubleCounting()
    {
        ModelPricing pricing = Pricing(input: 2m, output: 8m, cache: 0.5m);
        var assumptions = new WorkloadAssumptions(0.25m);
        CostRange? result = ProviderCostCalculator.Calculate(Usage(10_000, 2_000), pricing, assumptions);

        Assert.IsNotNull(result);
        Assert.AreEqual(32.25m, result.P50);
    }

    [TestMethod]
    public void PricingUsesFractionalBatchesNotCeilingPerBatch()
    {
        CostRange? result = ProviderCostCalculator.Calculate(Usage(100, 50), Pricing(2m, 8m), new WorkloadAssumptions());
        Assert.AreEqual(0.60m, result!.P50);
    }

    [TestMethod]
    public void NoCacheRateForAssumedCache_ReturnsUnknownNotZero()
    {
        Assert.IsNull(ProviderCostCalculator.Calculate(Usage(1000, 100), Pricing(2m, 8m), new WorkloadAssumptions(0.3m)));
    }

    [TestMethod]
    public void MissingInputOrOutputPrices_ReturnsUnknown()
    {
        Assert.IsNull(ProviderCostCalculator.Calculate(Usage(1000, 100), Pricing(null, 8m), new WorkloadAssumptions()));
        Assert.IsNull(ProviderCostCalculator.Calculate(Usage(1000, 100), Pricing(2m, null), new WorkloadAssumptions()));
    }

    [TestMethod]
    public void NegativeOrInvalidCacheRatio_ReturnsUnknown()
    {
        Assert.IsNull(ProviderCostCalculator.Calculate(Usage(1000, 100), Pricing(2m, 8m), new WorkloadAssumptions(-0.1m)));
        Assert.IsNull(ProviderCostCalculator.Calculate(Usage(1000, 100), Pricing(2m, 8m), new WorkloadAssumptions(1.1m)));
    }

    [TestMethod]
    public void LegacyPremiumRequests_AreNotMispricedAsTokenBasedCredits()
    {
        ModelPricing legacy = Pricing(2m, 8m) with
        {
            Mode = BillingMode.LegacyPremiumRequests,
            Unit = BudgetUnit.PremiumInteractions
        };
        Assert.IsNull(ProviderCostCalculator.Calculate(Usage(1000, 100), legacy, new WorkloadAssumptions()));
    }

    [TestMethod]
    public void UnknownQuotaIsNotZeroAndRequiresMoreEvidence()
    {
        ProviderCostForecast result = Assess(quotaMissing: true);
        Assert.IsNull(result.RemainingBefore);
        Assert.AreEqual(ProviderBudgetDecision.InsufficientEvidence, result.Decision);
        Assert.IsTrue(result.Warnings.Any(message => message.Contains("not zero credits", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ExplicitZeroRemainingMeansDefer()
    {
        ProviderCostForecast result = Assess(quota: Quota(0, 0));
        Assert.AreEqual(0m, result.RemainingBefore);
        Assert.AreEqual(ProviderBudgetDecision.Defer, result.Decision);
    }

    [TestMethod]
    public void MonthlyAllowanceDerivesRemainingFromAccountReportedUsage()
    {
        ProviderCostForecast result = Assess();
        Assert.AreEqual(21_000m, result.RemainingBefore);
        Assert.AreEqual(ProviderBudgetDecision.Execute, result.Decision);
        Assert.IsTrue(result.RemainingAfterP90 < result.RemainingBefore);
    }

    [TestMethod]
    public void StaleQuotaIsNotUsedAsCurrentBindingAllowance()
    {
        BudgetSnapshot oldQuota = Quota() with { ObservedAt = Now.AddDays(-2) };
        ProviderCostForecast result = Assess(quota: oldQuota);
        Assert.IsTrue(result.QuotaStale);
        Assert.AreEqual(ProviderBudgetDecision.InsufficientEvidence, result.Decision);
    }

    [TestMethod]
    public void ExpiredResetPeriodMarksQuotaStale()
    {
        BudgetSnapshot oldQuota = Quota() with { ResetsAt = Now.AddMinutes(-1) };
        Assert.IsTrue(Assess(quota: oldQuota).QuotaStale);
    }

    [TestMethod]
    public void StaleModelPricingIsNotTreatedAsCurrent()
    {
        ProviderCostForecast result = Assess(pricing: Pricing(2m, 8m) with { ObservedAt = Now.AddDays(-3) });
        Assert.IsTrue(result.PricingStale);
        Assert.AreEqual(ProviderBudgetDecision.InsufficientEvidence, result.Decision);
    }

    [TestMethod]
    public void DynamicPriceChangeUpdatesCreditForecastWithoutChangingTokens()
    {
        ProviderCostForecast initial = Assess(pricing: Pricing(2m, 8m));
        ProviderCostForecast changed = Assess(pricing: Pricing(4m, 16m));
        Assert.AreEqual(initial.Costs!.P50 * 2, changed.Costs!.P50);
    }

    [TestMethod]
    public void P90OverAccountBalanceDefersExecution()
    {
        Assert.AreEqual(ProviderBudgetDecision.Defer, Assess(quota: Quota(10, 0)).Decision);
    }

    [TestMethod]
    public void TaskCapAppliesToP90NotP50()
    {
        decimal p50 = Assess().Costs!.P50;
        ProviderCostForecast result = Assess(policy: new ProviderBudgetPolicy(MaxTaskUnits: p50));
        Assert.AreEqual(ProviderBudgetDecision.Defer, result.Decision);
    }

    [TestMethod]
    public void ReserveCannotBeSpentByP90Forecast()
    {
        decimal p90 = Assess().Costs!.P90;
        ProviderCostForecast result = Assess(quota: Quota(p90 + 10, 0),
            policy: new ProviderBudgetPolicy(ReservedUnits: 11));
        Assert.AreEqual(ProviderBudgetDecision.Defer, result.Decision);
    }

    [TestMethod]
    public void HighFractionOfAllowanceLeadsToNarrowScope()
    {
        decimal p90 = Assess().Costs!.P90;
        ProviderCostForecast result = Assess(quota: Quota(p90 * 2, 0),
            policy: new ProviderBudgetPolicy(MaxAtRiskFraction: 0.40m));
        Assert.AreEqual(ProviderBudgetDecision.NarrowScope, result.Decision);
    }

    [TestMethod]
    public void MeaningfulRiskSuggestsRetryReserve()
    {
        decimal p90 = Assess().Costs!.P90;
        Assert.AreEqual(ProviderBudgetDecision.ReserveBudget, Assess(quota: Quota(p90 * 3, 0)).Decision);
    }

    [TestMethod]
    public void OriginalTokenBudgetRejectionCannotBeOverriddenByBilling()
    {
        ProviderCostForecast result = Assess(estimate: Estimate() with { Decision = BudgetDecision.Reject });
        Assert.AreEqual(ProviderBudgetDecision.Defer, result.Decision);
    }

    [TestMethod]
    public void AccountBalancesCannotTransferBetweenProvidersOrAccounts()
    {
        BudgetSnapshot another = Quota() with { Account = new ProviderAccount(BillingProvider.GitHubCopilot, "other") };
        Assert.ThrowsExactly<ArgumentException>(() => Assess(quota: another));
        Assert.ThrowsExactly<ArgumentException>(() => Assess(quota: Quota() with
        {
            Account = new ProviderAccount(BillingProvider.OpenAiApi, "same-name")
        }));
    }

    [TestMethod]
    public void QuotaUnitsCannotBeMixedWithCreditPricing()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Assess(quota: Quota() with { Unit = BudgetUnit.Usd }));
    }

    [TestMethod]
    public void ExplicitlyUnlimitedAllowanceStillHonorsTaskLimits()
    {
        BudgetSnapshot unlimited = Quota() with { Unlimited = true, Allowance = null, Consumed = null };
        Assert.AreEqual(ProviderBudgetDecision.Defer, Assess(quota: unlimited,
            policy: new ProviderBudgetPolicy(MaxTaskUnits: 1m)).Decision);
    }

    [TestMethod]
    public void AiCreditUsdValueIsMeteredValueNotAccountCharge()
    {
        ProviderCostForecast result = Assess();
        Assert.AreEqual(result.Costs!.P50 * 0.01m, result.MeteredValueUsd);
    }

    [TestMethod]
    public void ApiUsdSupportsExternalFixedCharge()
    {
        ModelPricing usd = Pricing(2m, 8m) with
        {
            Provider = BillingProvider.OpenAiApi,
            Mode = BillingMode.ApiUsd,
            Unit = BudgetUnit.Usd,
            AdditionalFixedCost = 2.5m
        };
        CostRange? cost = ProviderCostCalculator.Calculate(Usage(1000, 100), usd, new WorkloadAssumptions());
        Assert.AreEqual(5.3m, cost!.P50);
    }

    [TestMethod]
    public void ReportShowsApprovalGateWithoutPassingIt()
    {
        EstimateReport estimate = Estimate() with
        {
            Milestones = [new MilestoneForecast("M1", "Approval", new TokenRange(1, 2, 3),
                new TokenRange(1, 2, 3), [], [], true, false)]
        };
        Assert.IsTrue(Assess(estimate: estimate).RequiresHumanApproval);
    }

    [TestMethod]
    public void ImprovedPromptRetainsOriginalTextVerbatim()
    {
        const string prompt = "## M1\nBuild lighting. Depends on M0. Must get visual approval.\nTests required.";
        PromptScopeAssessment result = new PromptScopeAdvisor().Assess(prompt, Estimate(), generateImprovedPrompt: true);
        Assert.IsNotNull(result.ImprovedPrompt);
        Assert.IsTrue(result.ImprovedPrompt.EndsWith(prompt, StringComparison.Ordinal));
        StringAssert.Contains(result.ImprovedPrompt, "Do not bypass required approvals");
    }

    [TestMethod]
    public void BroadPromptImprovementSelectsOneUnblockedMilestoneAndPreservesAllRequirements()
    {
        const string plan = "M1 build engine; M2 integrate; M3 test; M4 approve";
        EstimateReport estimate = Estimate() with
        {
            Milestones = Enumerable.Range(1, 4).Select(index => new MilestoneForecast($"M{index}",
                $"Milestone {index}", new TokenRange(1, 2, 3), new TokenRange(1, 2, 3),
                index == 1 ? [] : [$"M{index - 1}"], [], index == 4, false)).ToArray()
        };
        PromptScopeAssessment result = new PromptScopeAdvisor().Assess(plan, estimate, true);
        Assert.IsTrue(result.TooBroad);
        StringAssert.Contains(result.ImprovedPrompt!, "ONLY milestone M1");
        Assert.IsTrue(result.ImprovedPrompt!.EndsWith(plan, StringComparison.Ordinal));
    }

    [TestMethod]
    public void CheaperCatalogModelIsAdvisoryUntilCapabilityAndQualityAreChecked()
    {
        ModelPricing selected = Pricing(3m, 9m);
        ModelPricing cheaper = Pricing(1m, 4m) with { ModelId = "economy-candidate" };
        IReadOnlyList<ModelCostAlternative> result = new ModelAlternativeAdvisor().FindCheaperModels(
            Usage(10_000, 2_000), selected, [selected, cheaper], new WorkloadAssumptions());
        Assert.AreEqual(1, result.Count);
        Assert.IsTrue(result[0].SavingsPercent > 0);
        StringAssert.Contains(result[0].SuitabilityCaveat, "Verify tool support");
    }

    [TestMethod]
    public void ModelAlternativesNeverCompareBalancesAcrossProviders()
    {
        IReadOnlyList<ModelCostAlternative> result = new ModelAlternativeAdvisor().FindCheaperModels(
            Usage(10_000, 2_000), Pricing(3m, 9m),
            [Pricing(1m, 1m) with { ModelId = "foreign", Provider = BillingProvider.AnthropicApi }],
            new WorkloadAssumptions());
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void UnavailableAdapterNeverInventsQuotaOrPricing()
    {
        var adapter = new UnavailableBillingAdapter(BillingProvider.ChatGptSubscription);
        Assert.IsNull(adapter.GetPricingAsync("model").GetAwaiter().GetResult());
        Assert.IsNull(adapter.GetBudgetAsync(new ProviderAccount(BillingProvider.ChatGptSubscription, "x"),
            BudgetUnit.ProviderQuotaUnits).GetAwaiter().GetResult());
    }

    [TestMethod]
    public void ConfiguredBalancesSelectExactAccountAndUnit()
    {
        var adapter = new ConfiguredBudgetAdapter(BillingProvider.GitHubCopilot,
        [
            Quota(),
            Quota() with { Account = new ProviderAccount(BillingProvider.GitHubCopilot, "second"), Allowance = 100 }
        ]);
        BudgetSnapshot? result = adapter.GetBudgetAsync(Copilot, BudgetUnit.GitHubAiCredits).GetAwaiter().GetResult();
        Assert.AreEqual(21_000m, result!.Remaining);
        Assert.IsNull(adapter.GetBudgetAsync(Copilot, BudgetUnit.Usd).GetAwaiter().GetResult());
    }

    private static TokenUsageForecast Usage(long inputP50, long outputP50) => new(
        new TokenRange(inputP50 / 2, inputP50, inputP50 * 2),
        new TokenRange(outputP50 / 2, outputP50, outputP50 * 2));

    private static ModelPricing Pricing(decimal? input, decimal? output, decimal? cache = null) =>
        new(BillingProvider.GitHubCopilot, "selected-model", BillingMode.MeteredAiCredits,
            BudgetUnit.GitHubAiCredits, input, output, cache, 1000, Now, "SDK test fixture");

    private static BudgetSnapshot Quota(decimal allowance = 30_000, decimal used = 9_000) =>
        new(Copilot, BudgetUnit.GitHubAiCredits, allowance, used, null, Now, Now.AddDays(28), "account-reported test quota");

    private static EstimateReport Estimate() => new(Guid.NewGuid(), Now, ExecutionTarget.GitHubCopilot,
        "selected-model", 100, MeasurementSource.Heuristic, new TokenRange(1_000, 2_000, 4_000),
        new TokenRange(100, 200, 400), 4_000, 400, ForecastConfidence.Low, BudgetDecision.Pass,
        new BudgetConstraints(), new RepositoryImpact(true, 20, 10, 2, 0, false, null), [], [], [], [], "test",
        Usage(10_000, 2_000));

    private static ProviderCostForecast Assess(ModelPricing? pricing = null, BudgetSnapshot? quota = null,
        ProviderBudgetPolicy? policy = null, EstimateReport? estimate = null, bool quotaMissing = false)
    {
        return new BudgetPolicyEvaluator().Evaluate(estimate ?? Estimate(), Copilot, pricing ?? Pricing(2m, 8m),
            quotaMissing ? null : quota ?? Quota(), policy ?? new ProviderBudgetPolicy(), new WorkloadAssumptions(), Now);
    }
}
