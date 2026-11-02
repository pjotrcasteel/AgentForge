using Microsoft.VisualStudio.TestTools.UnitTesting;
using TokenBudget.Billing;
using TokenBudget.Domain;

namespace TokenBudget.Telemetry.Tests;

[TestClass]
public sealed class ProviderSessionStoreTests
{
    private static readonly ProviderAccount Copilot = new(BillingProvider.GitHubCopilot, "personal");
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task FirstCumulativeCheckpointAddsItsSessionTotalOnlyOnce()
    {
        await WithStore(async store =>
        {
            ProviderSessionCheckpoint original = Snapshot();
            ProviderSessionIngestResult first = await store.IngestAsync(original, TestContext.CancellationToken);
            ProviderSessionIngestResult replay = await store.IngestAsync(original, TestContext.CancellationToken);

            Assert.AreEqual(ProviderCheckpointChange.Inserted, first.Change);
            Assert.AreEqual(800L, first.Delta.InputTokens);
            Assert.AreEqual(200L, first.Delta.OutputTokens);
            Assert.AreEqual(8m, first.Delta.AiCredits);
            Assert.AreEqual(ProviderCheckpointChange.Duplicate, replay.Change);
            Assert.AreEqual(0L, replay.Delta.InputTokens);
            Assert.AreEqual(0m, replay.Delta.AiCredits);
        });
    }

    [TestMethod]
    public async Task LaterSessionTotalProducesOnlyTheIncrease()
    {
        await WithStore(async store =>
        {
            await store.IngestAsync(Snapshot(), TestContext.CancellationToken);
            ProviderSessionCheckpoint next = Snapshot() with
            {
                Observation = Observation(input: 1250, output: 310, credits: 11m) with { ObservedAt = Now.AddMinutes(1) }
            };
            ProviderSessionIngestResult result = await store.IngestAsync(next, TestContext.CancellationToken);

            Assert.AreEqual(ProviderCheckpointChange.Advanced, result.Change);
            Assert.AreEqual(450L, result.Delta.InputTokens);
            Assert.AreEqual(110L, result.Delta.OutputTokens);
            Assert.AreEqual(3m, result.Delta.AiCredits);
            Assert.AreEqual(1250L, (await store.GetAsync(Copilot, "session-1", TestContext.CancellationToken))!.Observation.Usage.InputTokens);
        });
    }

    [TestMethod]
    public async Task CompletionAtUnchangedCountersIsNotASecondUsageCharge()
    {
        await WithStore(async store =>
        {
            await store.IngestAsync(Snapshot(), TestContext.CancellationToken);
            ProviderSessionIngestResult result = await store.IngestAsync(Snapshot(completed: true, match: Binding()),
                TestContext.CancellationToken);

            Assert.AreEqual(ProviderCheckpointChange.Finalized, result.Change);
            Assert.AreEqual(0L, result.Delta.InputTokens);
            Assert.IsTrue(result.EligibleForTokenComparison);
            Assert.IsFalse(result.EligibleForCostComparison);
        });
    }

    [TestMethod]
    public async Task PerCallEventCannotBeMisinterpretedAsCumulativeSessionTotals()
    {
        await WithStore(async store =>
        {
            ProviderSessionCheckpoint eventValue = Snapshot() with
            {
                Observation = Observation() with { IsCumulative = false, Source = "assistant.usage" }
            };
            await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            {
                await store.IngestAsync(eventValue, TestContext.CancellationToken);
            });
        });
    }

    [TestMethod]
    public async Task DecreasingTokenCountersAreRejectedNotSubtracted()
    {
        await WithStore(async store =>
        {
            await store.IngestAsync(Snapshot(), TestContext.CancellationToken);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            {
                await store.IngestAsync(Snapshot() with
                {
                    Observation = Observation(input: 799) with { ObservedAt = Now.AddMinutes(1) }
                }, TestContext.CancellationToken);
            });
        });
    }

    [TestMethod]
    public async Task DecreasingCreditCountersAreRejected()
    {
        await WithStore(async store =>
        {
            await store.IngestAsync(Snapshot(), TestContext.CancellationToken);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            {
                await store.IngestAsync(Snapshot() with
                {
                    Observation = Observation(credits: 7m) with { ObservedAt = Now.AddMinutes(1) }
                }, TestContext.CancellationToken);
            });
        });
    }

    [TestMethod]
    public async Task StaleGrowingCheckpointIsRejected()
    {
        await WithStore(async store =>
        {
            await store.IngestAsync(Snapshot(), TestContext.CancellationToken);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            {
                await store.IngestAsync(Snapshot() with
                {
                    Observation = Observation(input: 900) with { ObservedAt = Now.AddMinutes(-1) }
                }, TestContext.CancellationToken);
            });
        });
    }

    [TestMethod]
    public async Task SameSessionIdNeverMergesDifferentAccounts()
    {
        await WithStore(async store =>
        {
            ProviderAccount other = new(BillingProvider.GitHubCopilot, "another-user");
            await store.IngestAsync(Snapshot(), TestContext.CancellationToken);
            await store.IngestAsync(Snapshot() with
            {
                Observation = Observation(input: 50, output: 10, credits: 1m) with { Account = other }
            }, TestContext.CancellationToken);

            Assert.AreEqual(800L, (await store.GetAsync(Copilot, "session-1", TestContext.CancellationToken))!.Observation.Usage.InputTokens);
            Assert.AreEqual(50L, (await store.GetAsync(other, "session-1", TestContext.CancellationToken))!.Observation.Usage.InputTokens);
        });
    }

    [TestMethod]
    public async Task SameSessionIdNeverMergesDifferentProviders()
    {
        await WithStore(async store =>
        {
            ProviderAccount other = new(BillingProvider.OpenAiApi, "personal");
            await store.IngestAsync(Snapshot(), TestContext.CancellationToken);
            await store.IngestAsync(Snapshot() with
            {
                Observation = Observation(input: 60, output: 10, credits: null) with { Account = other }
            }, TestContext.CancellationToken);
            Assert.AreEqual(800L, (await store.GetAsync(Copilot, "session-1", TestContext.CancellationToken))!.Observation.Usage.InputTokens);
            Assert.AreEqual(60L, (await store.GetAsync(other, "session-1", TestContext.CancellationToken))!.Observation.Usage.InputTokens);
        });
    }

    [TestMethod]
    public async Task MixedModelSessionCannotAttachSingleModelForecast()
    {
        await WithStore(async store =>
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            {
                await store.IngestAsync(Snapshot(completed: true, match: Binding()) with
                {
                    Observation = Observation() with { Models = ["model-1", "model-2"] }
                }, TestContext.CancellationToken);
            });
        });
    }

    [TestMethod]
    public async Task CompletedSessionWithoutExplicitBindingDoesNotCalibrate()
    {
        await WithStore(async store =>
        {
            await store.IngestAsync(Snapshot(completed: true), TestContext.CancellationToken);
            ProviderSessionHistorySummary result = await store.SummarizeAsync(Copilot, "model-1", TestContext.CancellationToken);
            Assert.AreEqual(1, result.CompletedSessions);
            Assert.AreEqual(0, result.MatchedCompletedSessions);
            Assert.IsFalse(result.SufficientForCalibration);
        });
    }

    [TestMethod]
    public async Task CompletedMatchComputesTokensButExcludesProvisionalCredits()
    {
        await WithStore(async store =>
        {
            await store.IngestAsync(Snapshot(completed: true, match: Binding()), TestContext.CancellationToken);
            ProviderSessionHistorySummary result = await store.SummarizeAsync(Copilot, "model-1", TestContext.CancellationToken);

            Assert.AreEqual(1, result.MatchedCompletedSessions);
            Assert.AreEqual(1.0, result.MedianActualToP50Ratio);
            Assert.AreEqual(1.0, result.ObservedP90TokenCoverage);
            Assert.AreEqual(0, result.VerifiedCostMatches);
            Assert.IsNull(result.ObservedP90CostCoverage);
        });
    }

    [TestMethod]
    public async Task VerifiedCreditsHaveSeparateCostCoverage()
    {
        await WithStore(async store =>
        {
            ProviderSessionCheckpoint checkpoint = Snapshot(completed: true, match: Binding()) with
            {
                Observation = Observation() with { CreditsVerified = true }
            };
            ProviderSessionIngestResult result = await store.IngestAsync(checkpoint, TestContext.CancellationToken);
            ProviderSessionHistorySummary history = await store.SummarizeAsync(Copilot, "model-1", TestContext.CancellationToken);

            Assert.IsTrue(result.EligibleForCostComparison);
            Assert.AreEqual(1, history.VerifiedCostMatches);
            Assert.AreEqual(1.0, history.ObservedP90CostCoverage);
        });
    }

    [TestMethod]
    public async Task VerifiedCreditEvidenceSurvivesAnUnverifiedIdenticalReplay()
    {
        await WithStore(async store =>
        {
            ProviderSessionCheckpoint verified = Snapshot(completed: true, match: Binding()) with
            {
                Observation = Observation() with { CreditsVerified = true }
            };
            await store.IngestAsync(verified, TestContext.CancellationToken);
            ProviderSessionIngestResult replay = await store.IngestAsync(verified with
            {
                Observation = Observation()
            }, TestContext.CancellationToken);

            Assert.AreEqual(ProviderCheckpointChange.Duplicate, replay.Change);
            Assert.IsTrue(replay.EligibleForCostComparison);
        });
    }

    [TestMethod]
    public async Task UnverifiedGrowingUsageCannotKeepOldVerifiedCostStatus()
    {
        await WithStore(async store =>
        {
            await store.IngestAsync(Snapshot() with
            {
                Observation = Observation() with { CreditsVerified = true }
            }, TestContext.CancellationToken);
            ProviderSessionIngestResult updated = await store.IngestAsync(Snapshot() with
            {
                Observation = Observation(input: 900, credits: null) with { ObservedAt = Now.AddMinutes(1) }
            }, TestContext.CancellationToken);

            Assert.AreEqual(ProviderCheckpointChange.Advanced, updated.Change);
            Assert.IsFalse((await store.GetAsync(Copilot, "session-1", TestContext.CancellationToken))!
                .Observation.CreditsVerified);
        });
    }

    [TestMethod]
    public async Task CacheWriteSubsetCannotRegressBetweenCheckpoints()
    {
        await WithStore(async store =>
        {
            await store.IngestAsync(Snapshot() with
            {
                Observation = Observation() with { Usage = new UsageBreakdown(800, 200, 50, 30, null) }
            }, TestContext.CancellationToken);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            {
                await store.IngestAsync(Snapshot() with
                {
                    Observation = Observation(input: 900) with
                    {
                        Usage = new UsageBreakdown(900, 200, 50, 29, null), ObservedAt = Now.AddMinutes(1)
                    }
                }, TestContext.CancellationToken);
            });
        });
    }

    [TestMethod]
    public async Task OlderEqualUsageReplayCannotReplaceFreshTimestamp()
    {
        await WithStore(async store =>
        {
            await store.IngestAsync(Snapshot() with
            {
                Observation = Observation() with { ObservedAt = Now.AddMinutes(1) }
            }, TestContext.CancellationToken);
            ProviderSessionIngestResult replay = await store.IngestAsync(Snapshot(), TestContext.CancellationToken);

            Assert.AreEqual(ProviderCheckpointChange.Duplicate, replay.Change);
            Assert.AreEqual(Now.AddMinutes(1), (await store.GetAsync(Copilot, "session-1",
                TestContext.CancellationToken))!.Observation.ObservedAt);
        });
    }

    [TestMethod]
    public async Task TwentyUniqueMatchedSessionsRemainDescriptiveUntilCalibrationIsEnabled()
    {
        await WithStore(async store =>
        {
            for (int index = 0; index < 20; index++)
            {
                await store.IngestAsync(Snapshot(completed: true, match: Binding()) with
                {
                    Observation = Observation() with { SessionId = $"run-{index}" }
                }, TestContext.CancellationToken);
            }

            ProviderSessionHistorySummary result = await store.SummarizeAsync(Copilot, "model-1", TestContext.CancellationToken);
            Assert.AreEqual(20, result.MatchedCompletedSessions);
            Assert.IsTrue(result.SufficientForCalibration);
            StringAssert.Contains(result.Note, "remains manual and disabled");
        });
    }

    [TestMethod]
    public async Task CalibrationDiagnosticsUseTheSameDeduplicatedSessionLedger()
    {
        await WithStore(async store =>
        {
            ProviderSessionCheckpoint fixture = Snapshot(completed: true, match: Binding());
            await store.IngestAsync(fixture, TestContext.CancellationToken);
            await store.IngestAsync(fixture, TestContext.CancellationToken);

            ProviderCalibrationDiagnostics report = await store.AnalyzeCalibrationAsync(Copilot, "model-1",
                TestContext.CancellationToken);
            Assert.AreEqual(1, report.MatchedCompletedSessions);
            Assert.AreEqual(ProviderCalibrationStatus.CollectMoreEvidence, report.Status);
            Assert.AreEqual(1d, report.MedianActualToP50Ratio);
            Assert.AreEqual(0, report.VerifiedCreditSessions);
        });
    }

    [TestMethod]
    public async Task CalibrationDiagnosticsDoNotLeakOtherAccountsUsage()
    {
        await WithStore(async store =>
        {
            ProviderAccount other = new(BillingProvider.GitHubCopilot, "other-account");
            await store.IngestAsync(Snapshot(completed: true, match: Binding()) with
            {
                Observation = Observation() with { Account = other }
            }, TestContext.CancellationToken);

            ProviderCalibrationDiagnostics personal = await store.AnalyzeCalibrationAsync(Copilot, "model-1",
                TestContext.CancellationToken);
            Assert.AreEqual(0, personal.MatchedCompletedSessions);
            Assert.AreEqual(ProviderCalibrationStatus.NoMatchedRuns, personal.Status);
        });
    }

    [TestMethod]
    public async Task CompleteSessionCountersAreImmutable()
    {
        await WithStore(async store =>
        {
            await store.IngestAsync(Snapshot(completed: true), TestContext.CancellationToken);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            {
                await store.IngestAsync(Snapshot(completed: true) with
                {
                    Observation = Observation(input: 900) with { ObservedAt = Now.AddMinutes(1) }
                }, TestContext.CancellationToken);
            });
        });
    }

    [TestMethod]
    public async Task SourceSwitchWithinSessionIsRejected()
    {
        await WithStore(async store =>
        {
            await store.IngestAsync(Snapshot(), TestContext.CancellationToken);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            {
                await store.IngestAsync(Snapshot() with
                {
                    Observation = Observation() with { Source = "different usage pipeline" }
                }, TestContext.CancellationToken);
            });
        });
    }

    [TestMethod]
    public async Task CacheSubsetLargerThanInputIsRejected()
    {
        await WithStore(async store =>
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            {
                await store.IngestAsync(Snapshot() with
                {
                    Observation = Observation() with { Usage = new UsageBreakdown(800, 200, 801, null, null) }
                }, TestContext.CancellationToken);
            });
        });
    }

    [TestMethod]
    public async Task MissingNewCreditMetricDoesNotWipeKnownCumulativeValue()
    {
        await WithStore(async store =>
        {
            await store.IngestAsync(Snapshot(), TestContext.CancellationToken);
            await store.IngestAsync(Snapshot() with
            {
                Observation = Observation(input: 900, credits: null) with { ObservedAt = Now.AddMinutes(1) }
            }, TestContext.CancellationToken);

            ProviderSessionCheckpoint? result = await store.GetAsync(Copilot, "session-1", TestContext.CancellationToken);
            Assert.AreEqual(8m, result!.Observation.AiCredits);
        });
    }

    [TestMethod]
    public async Task SessionCannotForgetPreviouslyRecordedModel()
    {
        await WithStore(async store =>
        {
            await store.IngestAsync(Snapshot() with
            {
                Observation = Observation() with { Models = ["model-1", "model-2"] }
            }, TestContext.CancellationToken);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            {
                await store.IngestAsync(Snapshot() with
                {
                    Observation = Observation(input: 900) with { ObservedAt = Now.AddMinutes(1) }
                }, TestContext.CancellationToken);
            });
        });
    }

    [TestMethod]
    public async Task ForecastBindingMustMatchProviderAndModel()
    {
        await WithStore(async store =>
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            {
                await store.IngestAsync(Snapshot(completed: true, match: Binding() with
                {
                    Provider = BillingProvider.OpenAiApi
                }), TestContext.CancellationToken);
            });
            await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            {
                await store.IngestAsync(Snapshot(completed: true, match: Binding() with
                {
                    ModelId = "another-model"
                }), TestContext.CancellationToken);
            });
        });
    }

    private async Task WithStore(Func<SqliteProviderSessionStore, Task> test)
    {
        string directory = Path.Combine(Path.GetTempPath(), "agentforge-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await test(new SqliteProviderSessionStore(Path.Combine(directory, "history.db")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ProviderForecastBinding Binding() => new(Guid.NewGuid(), BillingProvider.GitHubCopilot,
        "model-1", new TokenRange(500, 1000, 1500), new CostRange(2m, 10m, 20m), BudgetUnit.GitHubAiCredits);

    private static ActualUsageObservation Observation(long input = 800, long output = 200, decimal? credits = 8m) =>
        new(Copilot, "session-1", new UsageBreakdown(input, output, null, null, null), credits, 1m,
            Now, "SDK cumulative session metrics", ["model-1"], IsCumulative: true, CreditsVerified: false);

    private static ProviderSessionCheckpoint Snapshot(bool completed = false, ProviderForecastBinding? match = null) =>
        new(Observation(), completed, match);
}
