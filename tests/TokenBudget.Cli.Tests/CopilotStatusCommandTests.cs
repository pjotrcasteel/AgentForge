using Microsoft.VisualStudio.TestTools.UnitTesting;
using TokenBudget.Billing;
using TokenBudget.Cli.Commands;
using TokenBudget.Cli.Configuration;

namespace TokenBudget.Cli.Tests;

[TestClass]
public sealed class CopilotStatusCommandTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task DeclinedConsentNeverCallsSdkOrChangesSettings()
    {
        await WithStoreAsync(async store =>
        {
            await store.SaveAsync(new AgentForgeSettings(BillingProvider.GitHubCopilot, "chosen-model", "personal"),
                TestContext.CancellationToken);
            string original = await File.ReadAllTextAsync(store.FilePath, TestContext.CancellationToken);
            int calls = 0;
            using var output = new StringWriter();

            int status = await CopilotStatusCommand.RunAsync(store, new StringReader("no\n"), output,
                (_, _, _) =>
                {
                    calls++;
                    return Task.FromResult(0);
                });

            Assert.AreEqual(2, status);
            Assert.AreEqual(0, calls);
            StringAssert.Contains(output.ToString(), "No SDK call made");
            Assert.AreEqual(original, await File.ReadAllTextAsync(store.FilePath, TestContext.CancellationToken));
        });
    }

    [TestMethod]
    public async Task ConsentUsesSavedModelAccountAndOriginalBalanceTimestamp()
    {
        await WithStoreAsync(async store =>
        {
            DateTimeOffset originallyObserved = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
            await store.SaveAsync(new AgentForgeSettings(BillingProvider.GitHubCopilot, "chosen-model", "personal",
                30_000m, 9_000m, BalanceObservedAt: originallyObserved), TestContext.CancellationToken);
            ProviderAccount? observedAccount = null;
            string? observedModel = null;
            BudgetSnapshot? observedBalance = null;
            using var output = new StringWriter();

            int status = await CopilotStatusCommand.RunAsync(store, new StringReader("yes\n"), output,
                (account, model, balance) =>
                {
                    observedAccount = account;
                    observedModel = model;
                    observedBalance = balance;
                    return Task.FromResult(0);
                });

            Assert.AreEqual(0, status);
            Assert.AreEqual(new ProviderAccount(BillingProvider.GitHubCopilot, "personal"), observedAccount);
            Assert.AreEqual("chosen-model", observedModel);
            Assert.IsNotNull(observedBalance);
            Assert.AreEqual(21_000m, observedBalance.Remaining);
            Assert.AreEqual(originallyObserved, observedBalance.ObservedAt);
            Assert.IsTrue(observedBalance.IsStale(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero),
                TimeSpan.FromHours(24)));
            StringAssert.Contains(observedBalance.Source, "account-reported");
        });
    }

    [TestMethod]
    public async Task MissingTimestampCannotBecomeFreshByRunningStatus()
    {
        await WithStoreAsync(async store =>
        {
            await store.SaveAsync(new AgentForgeSettings(BillingProvider.GitHubCopilot, "chosen-model", "personal",
                30_000m, 9_000m), TestContext.CancellationToken);
            BudgetSnapshot? observed = null;
            int status = await CopilotStatusCommand.RunAsync(store, new StringReader("y\n"), TextWriter.Null,
                (_, _, balance) =>
                {
                    observed = balance;
                    return Task.FromResult(0);
                });

            Assert.AreEqual(0, status);
            Assert.IsNotNull(observed);
            Assert.AreEqual(DateTimeOffset.UnixEpoch, observed.ObservedAt);
            Assert.IsTrue(observed.IsStale(DateTimeOffset.UtcNow, TimeSpan.FromHours(24)));
        });
    }

    [TestMethod]
    public async Task MissingBalanceRemainsUnknown()
    {
        await WithStoreAsync(async store =>
        {
            await store.SaveAsync(new AgentForgeSettings(BillingProvider.GitHubCopilot, "chosen-model", "personal"),
                TestContext.CancellationToken);
            BudgetSnapshot? observed = new(new ProviderAccount(BillingProvider.GitHubCopilot, "placeholder"),
                BudgetUnit.GitHubAiCredits, 10, 0, null, DateTimeOffset.UtcNow, null, "test");
            int status = await CopilotStatusCommand.RunAsync(store, new StringReader("yes\n"), TextWriter.Null,
                (_, _, balance) =>
                {
                    observed = balance;
                    return Task.FromResult(0);
                });

            Assert.AreEqual(0, status);
            Assert.IsNull(observed);
        });
    }

    [TestMethod]
    public async Task NonCopilotProviderAndUnconfiguredModelDoNotContactSdk()
    {
        await WithStoreAsync(async store =>
        {
            int calls = 0;
            Func<ProviderAccount, string, BudgetSnapshot?, Task<int>> fake = (_, _, _) =>
            {
                calls++;
                return Task.FromResult(0);
            };

            await store.SaveAsync(new AgentForgeSettings(BillingProvider.OpenAiApi, "api-model", "personal"),
                TestContext.CancellationToken);
            Assert.AreEqual(2, await CopilotStatusCommand.RunAsync(store, new StringReader("yes\n"),
                TextWriter.Null, fake));

            await store.SaveAsync(AgentForgeSettings.Default, TestContext.CancellationToken);
            Assert.AreEqual(2, await CopilotStatusCommand.RunAsync(store, new StringReader("yes\n"),
                TextWriter.Null, fake));
            Assert.AreEqual(0, calls);
        });
    }

    [TestMethod]
    public async Task MissingSetupIsReadOnlyAndDoesNotContactSdk()
    {
        await WithStoreAsync(async store =>
        {
            int calls = 0;
            int status = await CopilotStatusCommand.RunAsync(store, new StringReader("yes\n"), TextWriter.Null,
                (_, _, _) =>
                {
                    calls++;
                    return Task.FromResult(0);
                });

            Assert.AreEqual(2, status);
            Assert.AreEqual(0, calls);
            Assert.IsFalse(File.Exists(store.FilePath));
        });
    }

    private static async Task WithStoreAsync(Func<AgentForgeSettingsStore, Task> run)
    {
        string directory = Path.Combine(Path.GetTempPath(), "agentforge-copilot-status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await run(new AgentForgeSettingsStore(Path.Combine(directory, "settings.json")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
