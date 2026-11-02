using Microsoft.VisualStudio.TestTools.UnitTesting;
using TokenBudget.Billing;
using TokenBudget.Cli.Commands;
using TokenBudget.Domain;
using TokenBudget.Telemetry;

namespace TokenBudget.Cli.Tests;

[TestClass]
public sealed class ProviderCalibrationCommandTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task CalibrationCommandReadsMatchedProviderSessionEvidence()
    {
        string folder = CreateTemporaryDirectory();
        try
        {
            string db = Path.Combine(folder, "usage.db");
            var account = new ProviderAccount(BillingProvider.GitHubCopilot, "personal");
            var observation = new ActualUsageObservation(account, "session-1",
                new UsageBreakdown(800, 200, null, null, null), 8m, null, DateTimeOffset.UtcNow,
                "test cumulative usage", ["model-1"], IsCumulative: true);
            var matched = new ProviderForecastBinding(Guid.NewGuid(), BillingProvider.GitHubCopilot,
                "model-1", new TokenRange(500, 1000, 1500));
            var store = new SqliteProviderSessionStore(db);
            await store.IngestAsync(new ProviderSessionCheckpoint(observation, true, matched),
                TestContext.CancellationToken);

            int exitCode = await ProviderCalibrationCommand.RunAsync(["provider-calibration", "--db", db,
                "--provider", "copilot", "--account", "personal", "--model", "model-1"]);

            Assert.AreEqual(0, exitCode);
            ProviderCalibrationDiagnostics result = await store.AnalyzeCalibrationAsync(account, "model-1",
                TestContext.CancellationToken);
            Assert.AreEqual(1, result.MatchedCompletedSessions);
            Assert.AreEqual(ProviderCalibrationStatus.CollectMoreEvidence, result.Status);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public async Task InvalidProviderAliasFailsWithoutInventingEvidence()
    {
        int exitCode = await ProviderCalibrationCommand.RunAsync(["provider-calibration", "--db", "test.db",
            "--provider", "unknown", "--account", "personal", "--model", "model-1"]);
        Assert.AreEqual(1, exitCode);
    }

    [TestMethod]
    public async Task MissingAccountFailsBeforeDatabaseAccess()
    {
        int exitCode = await ProviderCalibrationCommand.RunAsync(["provider-calibration", "--db", "test.db",
            "--provider", "copilot", "--model", "model-1"]);
        Assert.AreEqual(1, exitCode);
    }

    [TestMethod]
    public async Task ReadOnlyCalibrationCommandDoesNotCreateAnAbsentDatabase()
    {
        string folder = CreateTemporaryDirectory();
        try
        {
            string absentDatabase = Path.Combine(folder, "does-not-exist.db");
            int exitCode = await ProviderCalibrationCommand.RunAsync(["provider-calibration", "--db", absentDatabase,
                "--provider", "copilot", "--account", "personal", "--model", "model-1"]);

            Assert.AreEqual(1, exitCode);
            Assert.IsFalse(File.Exists(absentDatabase));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string folder = Path.Combine(Path.GetTempPath(), "agentforge-calibration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
