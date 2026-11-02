using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TokenBudget.Billing;
using TokenBudget.Cli.Commands;
using TokenBudget.Domain;
using TokenBudget.Telemetry;

namespace TokenBudget.Cli.Tests;

[TestClass]
public sealed class ProviderSessionCommandTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task IngestCommandPersistsExplicitCumulativeFixture()
    {
        string folder = Temp();
        try
        {
            string file = Path.Combine(folder, "snapshot.json");
            string db = Path.Combine(folder, "usage.db");
            var account = new ProviderAccount(BillingProvider.GitHubCopilot, "example");
            var observation = new ActualUsageObservation(account, "session", new UsageBreakdown(800, 200, null, null, null),
                5m, null, DateTimeOffset.UtcNow, "test cumulative metrics", ["model"], IsCumulative: true);
            var snapshot = new ProviderSessionCheckpoint(observation, true,
                new ProviderForecastBinding(Guid.NewGuid(), BillingProvider.GitHubCopilot, "model", new TokenRange(500, 1000, 1500)));
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Converters = { new JsonStringEnumConverter() }
            }), TestContext.CancellationToken);

            int result = await ProviderSessionCommand.RunAsync(["ingest-provider-session", file, "--db", db], ingestion: true);
            Assert.AreEqual(0, result);
            var store = new SqliteProviderSessionStore(db);
            Assert.IsNotNull(await store.GetAsync(account, "session", TestContext.CancellationToken));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public async Task HistoryCommandCanReadAnEmptyAccountWithoutInventingUsage()
    {
        string folder = Temp();
        try
        {
            int result = await ProviderSessionCommand.RunAsync(["provider-history", "--provider", "copilot",
                "--account", "example", "--model", "model", "--db", Path.Combine(folder, "usage.db")], ingestion: false);
            Assert.AreEqual(0, result);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string Temp()
    {
        string folder = Path.Combine(Path.GetTempPath(), "agentforge-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
