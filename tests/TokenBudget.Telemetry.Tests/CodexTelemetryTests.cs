using Microsoft.VisualStudio.TestTools.UnitTesting;
using TokenBudget.Telemetry;

namespace TokenBudget.Telemetry.Tests;

[TestClass]
public sealed class CodexTelemetryTests
{
    private static readonly string[] CompleteStream =
    [
        "{\"type\":\"thread.started\",\"thread_id\":\"thread-01\"}",
        "{\"type\":\"turn.started\"}",
        "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":800,\"cached_input_tokens\":500,\"output_tokens\":200,\"reasoning_output_tokens\":100}}"
    ];

    [TestMethod]
    public async Task ImportTwice_IsIdempotent_AndDoesNotStoreRawTranscript()
    {
        string folder = CreateFolder();
        try
        {
            string path = Path.Combine(folder, "events.jsonl");
            await File.WriteAllLinesAsync(path, CompleteStream, TestContext.CancellationToken);
            var importer = new CodexTelemetryImporter();
            CodexRunObservation observation = importer.Read(path, "WyrmForge", "codex-model", "run-01");
            var store = new SqliteUsageHistoryStore(Path.Combine(folder, "history.db"));

            IngestResult first = await store.IngestAsync(observation, TestContext.CancellationToken);
            IngestResult second = await store.IngestAsync(observation, TestContext.CancellationToken);
            HistorySummary history = await store.SummarizeAsync("WyrmForge", "codex-model", TestContext.CancellationToken);

            Assert.IsTrue(first.Inserted);
            Assert.IsFalse(second.Inserted);
            Assert.IsTrue(observation.Complete);
            Assert.AreEqual(1000L, observation.Usage?.TotalTokens);
            Assert.AreEqual(1, history.TotalRuns);
            Assert.AreEqual(1, history.CompletedRuns);
            Assert.AreEqual(0, history.MatchedCompletedRuns);
            Assert.IsFalse(history.SufficientForCalibration);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public async Task IngestSameRunIdWithDifferentTranscript_IsRejected()
    {
        string folder = CreateFolder();
        try
        {
            string path = Path.Combine(folder, "events.jsonl");
            await File.WriteAllLinesAsync(path, CompleteStream, TestContext.CancellationToken);
            var importer = new CodexTelemetryImporter();
            CodexRunObservation first = importer.Read(path, "profile", "model", "run-1");
            var store = new SqliteUsageHistoryStore(Path.Combine(folder, "history.db"));
            await store.IngestAsync(first, TestContext.CancellationToken);
            await File.AppendAllTextAsync(path, "\n{\"type\":\"turn.failed\"}\n", TestContext.CancellationToken);
            CodexRunObservation changed = importer.Read(path, "profile", "model", "run-1");

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            {
                await store.IngestAsync(changed, TestContext.CancellationToken);
            });
            HistorySummary history = await store.SummarizeAsync("profile", "model", TestContext.CancellationToken);
            Assert.AreEqual(1, history.TotalRuns);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public async Task IncompleteRun_IsRecordedButNeverCalibrates()
    {
        string folder = CreateFolder();
        try
        {
            string path = Path.Combine(folder, "events.jsonl");
            await File.WriteAllLinesAsync(path,
            [
                CompleteStream[0],
                "{\"type\":\"turn.completed\"}",
                CompleteStream[2]
            ], TestContext.CancellationToken);
            var importer = new CodexTelemetryImporter();
            CodexRunObservation incomplete = importer.Read(path, "profile", "model", "partial");
            var store = new SqliteUsageHistoryStore(Path.Combine(folder, "history.db"));
            IngestResult result = await store.IngestAsync(incomplete, TestContext.CancellationToken);
            HistorySummary summary = await store.SummarizeAsync("profile", "model", TestContext.CancellationToken);

            Assert.IsFalse(incomplete.Complete);
            Assert.IsFalse(result.EligibleForCalibration);
            Assert.AreEqual(1, summary.TotalRuns);
            Assert.AreEqual(0, summary.CompletedRuns);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public async Task MatchedRun_CalculatesActualToForecastRatioWithoutClaimingCalibration()
    {
        string folder = CreateFolder();
        try
        {
            var store = new SqliteUsageHistoryStore(Path.Combine(folder, "history.db"));
            var run = new CodexRunObservation("matched-1", "hash-one", "profile", "model", null,
                true, 1, 0, new(800, 200, 500, null, 100), Guid.NewGuid(), 800, 1200, []);
            await store.IngestAsync(run, TestContext.CancellationToken);
            HistorySummary summary = await store.SummarizeAsync("profile", "model", TestContext.CancellationToken);

            Assert.AreEqual(1, summary.MatchedCompletedRuns);
            Assert.AreEqual(1.25, summary.MedianActualToP50Ratio);
            Assert.AreEqual(1.0, summary.ObservedP90Coverage);
            Assert.IsFalse(summary.SufficientForCalibration);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public async Task Import_PowerShellUtf16Redirect_IsAccepted()
    {
        string folder = CreateFolder();
        try
        {
            string path = Path.Combine(folder, "events.jsonl");
            await File.WriteAllTextAsync(path, string.Join("\n", CompleteStream),
                new System.Text.UnicodeEncoding(false, true), TestContext.CancellationToken);

            CodexRunObservation result = new CodexTelemetryImporter().Read(path, "profile", "model");
            Assert.IsTrue(result.Complete);
            Assert.AreEqual(1000L, result.Usage?.TotalTokens);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public async Task InconsistentCacheSubset_IsRejected()
    {
        string folder = CreateFolder();
        try
        {
            var store = new SqliteUsageHistoryStore(Path.Combine(folder, "history.db"));
            var observation = new CodexRunObservation("run", "hash", "profile", "model", null, true, 1, 0,
                new(100, 100, 150, null, 0), null, null, null, []);
            await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            {
                await store.IngestAsync(observation, TestContext.CancellationToken);
            });
        }
        finally
        {
            // Validation happens before storage creation, so the directory remains empty.
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string CreateFolder()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tokenbudget-telemetry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    public TestContext TestContext { get; set; } = null!;
}
