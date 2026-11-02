using Microsoft.VisualStudio.TestTools.UnitTesting;
using TokenBudget.Billing;
using TokenBudget.Domain;

namespace TokenBudget.Telemetry.Tests;

[TestClass]
public sealed class ProviderCalibrationAnalyzerTests
{
    private static readonly ProviderAccount Account = new(BillingProvider.GitHubCopilot, "personal");
    private static readonly DateTimeOffset MeasuredAt = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void EmptyHistoryDoesNotInventCoverageOrCosts()
    {
        ProviderCalibrationDiagnostics report = Analyze([]);

        Assert.AreEqual(ProviderCalibrationStatus.NoMatchedRuns, report.Status);
        Assert.AreEqual(0, report.MatchedCompletedSessions);
        Assert.IsNull(report.ObservedP90TokenCoverage);
        Assert.IsNull(report.P90TokenCoverageLowerBound95);
        Assert.IsNull(report.MedianActualToP50Ratio);
        Assert.IsNull(report.ObservedP90CreditCoverage);
        Assert.IsFalse(report.CreditEvidenceReadyForReview);
    }

    [TestMethod]
    public void TwentyPerfectRunsDoNotEstablishReliableP90()
    {
        ProviderCalibrationDiagnostics report = Analyze(Runs(20));

        Assert.AreEqual(ProviderCalibrationStatus.CollectMoreEvidence, report.Status);
        Assert.AreEqual(1d, report.ObservedP90TokenCoverage);
        Assert.IsTrue(report.P90TokenCoverageLowerBound95 is < 0.90);
    }

    [TestMethod]
    public void HundredPerfectRunsBecomeCandidateForHumanReviewOnly()
    {
        ProviderCalibrationDiagnostics report = Analyze(Runs(100));

        Assert.AreEqual(ProviderCalibrationStatus.ReviewCandidate, report.Status);
        Assert.IsTrue(report.P90TokenCoverageLowerBound95 is > 0.95);
        StringAssert.Contains(report.Recommendation, "manual calibration review");
    }

    [TestMethod]
    public void NinetyNinePerfectRunsAreStillBelowReviewThreshold()
    {
        Assert.AreEqual(ProviderCalibrationStatus.CollectMoreEvidence, Analyze(Runs(99)).Status);
    }

    [TestMethod]
    public void PoorP90CoverageSignalsUnderforecastingAfterTwentyRuns()
    {
        ProviderSessionCheckpoint[] runs = Enumerable.Range(0, 20)
            .Select(index => Snapshot($"session-{index}", index < 16 ? 1000 : 2000)).ToArray();
        ProviderCalibrationDiagnostics report = Analyze(runs);

        Assert.AreEqual(ProviderCalibrationStatus.InvestigateUnderforecast, report.Status);
        Assert.AreEqual(0.8d, report.ObservedP90TokenCoverage);
        StringAssert.Contains(report.Recommendation, "Investigate scope");
    }

    [TestMethod]
    public void SmallBadSampleIsDescribedButNotTreatedAsAConfirmedTrend()
    {
        ProviderCalibrationDiagnostics report = Analyze(Runs(19, actualTokens: 2000));

        Assert.AreEqual(ProviderCalibrationStatus.CollectMoreEvidence, report.Status);
        Assert.AreEqual(0d, report.ObservedP90TokenCoverage);
    }

    [TestMethod]
    public void DuplicateSessionIdsCannotInflateStatisticalSample()
    {
        ProviderSessionCheckpoint run = Snapshot("same-session", 1000);
        ProviderCalibrationDiagnostics report = Analyze(Enumerable.Repeat(run, 100));

        Assert.AreEqual(1, report.MatchedCompletedSessions);
        Assert.AreEqual(ProviderCalibrationStatus.CollectMoreEvidence, report.Status);
    }

    [TestMethod]
    public void AccountsModelsAndIncompleteRunsAreIsolated()
    {
        ProviderSessionCheckpoint match = Snapshot("matched", 1000);
        ProviderSessionCheckpoint wrongAccount = Snapshot("foreign", 1000) with
        {
            Observation = Snapshot("foreign", 1000).Observation with
            {
                Account = new ProviderAccount(BillingProvider.GitHubCopilot, "another")
            }
        };
        ProviderSessionCheckpoint unfinished = Snapshot("unfinished", 1000) with { Completed = false };
        ProviderSessionCheckpoint mixed = Snapshot("mixed", 1000) with
        {
            Observation = Snapshot("mixed", 1000).Observation with { Models = ["model-1", "model-2"] }
        };

        ProviderCalibrationDiagnostics report = Analyze([match, wrongAccount, unfinished, mixed]);
        Assert.AreEqual(1, report.MatchedCompletedSessions);
    }

    [TestMethod]
    public void P10P50P90CoverageAndMedianUseActualMeasuredTotals()
    {
        ProviderSessionCheckpoint[] observations =
        [
            Snapshot("a", 50, p10: 75, p50: 125, p90: 200),
            Snapshot("b", 100, p10: 75, p50: 125, p90: 200),
            Snapshot("c", 150, p10: 75, p50: 125, p90: 200),
            Snapshot("d", 250, p10: 75, p50: 125, p90: 200)
        ];
        ProviderCalibrationDiagnostics report = Analyze(observations);

        Assert.AreEqual(0.25d, report.ObservedP10TokenCoverage);
        Assert.AreEqual(0.50d, report.ObservedP50TokenCoverage);
        Assert.AreEqual(0.75d, report.ObservedP90TokenCoverage);
        Assert.AreEqual(1d, report.MedianActualToP50Ratio);
    }

    [TestMethod]
    public void ProvisionalCopilotCreditsNeverBecomeVerifiedCostCoverage()
    {
        ProviderCalibrationDiagnostics report = Analyze(Runs(100));

        Assert.AreEqual(ProviderCalibrationStatus.ReviewCandidate, report.Status);
        Assert.AreEqual(0, report.VerifiedCreditSessions);
        Assert.IsNull(report.ObservedP90CreditCoverage);
        Assert.IsNull(report.P90CreditCoverageLowerBound95);
        Assert.IsFalse(report.CreditEvidenceReadyForReview);
    }

    [TestMethod]
    public void VerifiedCopilotCreditsHaveAnIndependentConfidenceGate()
    {
        ProviderCalibrationDiagnostics report = Analyze(Enumerable.Range(0, 100)
            .Select(index => Snapshot($"session-{index}", 1000, verifiedCredits: true)));

        Assert.AreEqual(100, report.VerifiedCreditSessions);
        Assert.AreEqual(1d, report.ObservedP90CreditCoverage);
        Assert.IsTrue(report.P90CreditCoverageLowerBound95 is > 0.95);
        Assert.IsTrue(report.CreditEvidenceReadyForReview);
    }

    [TestMethod]
    public void VerifiedCostSampleIsNotInterchangeableWithTokenSample()
    {
        ProviderCalibrationDiagnostics report = Analyze(Enumerable.Range(0, 100)
            .Select(index => Snapshot($"session-{index}", 1000, verifiedCredits: index < 20)));

        Assert.AreEqual(100, report.MatchedCompletedSessions);
        Assert.AreEqual(20, report.VerifiedCreditSessions);
        Assert.IsFalse(report.CreditEvidenceReadyForReview);
    }

    [TestMethod]
    public void WrongCostUnitNeverQualifiesAsVerifiedCopilotCredits()
    {
        ProviderSessionCheckpoint fixture = Snapshot("one", 1000, verifiedCredits: true);
        ProviderSessionCheckpoint incompatible = fixture with
        {
            Match = fixture.Match! with { CostUnit = BudgetUnit.Usd }
        };

        Assert.AreEqual(0, Analyze([incompatible]).VerifiedCreditSessions);
    }

    [TestMethod]
    public void AnthropicUsageCannotBeAccountedAsCopilotCredits()
    {
        ProviderAccount anthropic = new(BillingProvider.AnthropicApi, "personal");
        ProviderSessionCheckpoint fixture = Snapshot("one", 1000, verifiedCredits: true);
        ProviderSessionCheckpoint otherProvider = fixture with
        {
            Observation = fixture.Observation with { Account = anthropic },
            Match = fixture.Match! with { Provider = BillingProvider.AnthropicApi }
        };

        ProviderCalibrationDiagnostics report = ProviderCalibrationAnalyzer.Evaluate(anthropic, "model-1", [otherProvider]);
        Assert.AreEqual(1, report.MatchedCompletedSessions);
        Assert.AreEqual(0, report.VerifiedCreditSessions);
    }

    [TestMethod]
    public void InvalidForecastAndPerCallEventsAreNotComparableEvidence()
    {
        ProviderSessionCheckpoint fixture = Snapshot("one", 1000);
        ProviderSessionCheckpoint badForecast = fixture with
        {
            Match = fixture.Match! with { ForecastTokens = new TokenRange(0, 0, 0) }
        };
        ProviderSessionCheckpoint perCall = fixture with
        {
            Observation = fixture.Observation with { SessionId = "per-call", IsCumulative = false }
        };

        Assert.AreEqual(0, Analyze([badForecast, perCall]).MatchedCompletedSessions);
    }

    private static ProviderCalibrationDiagnostics Analyze(IEnumerable<ProviderSessionCheckpoint> checkpoints) =>
        ProviderCalibrationAnalyzer.Evaluate(Account, "model-1", checkpoints);

    private static IEnumerable<ProviderSessionCheckpoint> Runs(int count, long actualTokens = 1000) =>
        Enumerable.Range(0, count).Select(index => Snapshot($"session-{index}", actualTokens));

    private static ProviderSessionCheckpoint Snapshot(string sessionId, long actualTokens, long p10 = 500,
        long p50 = 1000, long p90 = 1500, bool verifiedCredits = false)
    {
        var observation = new ActualUsageObservation(Account, sessionId,
            new UsageBreakdown(actualTokens, 0, null, null, null), 8m, null, MeasuredAt, "test cumulative metrics",
            ["model-1"], IsCumulative: true, CreditsVerified: verifiedCredits);
        var match = new ProviderForecastBinding(Guid.Parse("783b01df-8ff0-4b6c-9959-47b5b29f3a8d"),
            BillingProvider.GitHubCopilot, "model-1", new TokenRange(p10, p50, p90),
            new CostRange(3m, 10m, 20m), BudgetUnit.GitHubAiCredits);
        return new ProviderSessionCheckpoint(observation, Completed: true, match);
    }
}
