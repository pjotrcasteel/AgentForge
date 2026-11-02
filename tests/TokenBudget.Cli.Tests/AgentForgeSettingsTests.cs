using Microsoft.VisualStudio.TestTools.UnitTesting;
using TokenBudget.Billing;
using TokenBudget.Cli.Commands;
using TokenBudget.Cli.Configuration;

namespace TokenBudget.Cli.Tests;

[TestClass]
public sealed class AgentForgeSettingsTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task MissingPreferencesReturnSafeOfflineCopilotDefaults()
    {
        await WithStore(async store =>
        {
            AgentForgeSettings settings = await store.LoadAsync(TestContext.CancellationToken);
            Assert.AreEqual(BillingProvider.GitHubCopilot, settings.Provider);
            Assert.AreEqual("unconfigured-model", settings.ModelId);
            Assert.IsFalse(settings.AllowLiveCopilotPricing);
            Assert.IsNull(settings.MonthlyAllowance);
            Assert.IsFalse(File.Exists(store.FilePath));
        });
    }

    [TestMethod]
    public async Task SavedPreferencesRoundTripWithoutAnyCredentials()
    {
        await WithStore(async store =>
        {
            var settings = new AgentForgeSettings(BillingProvider.GitHubCopilot, "selected-model",
                "personal", 30_000m, 9_000m, AllowLiveCopilotPricing: true, DateTimeOffset.UtcNow);
            await store.SaveAsync(settings, TestContext.CancellationToken);
            AgentForgeSettings loaded = await store.LoadAsync(TestContext.CancellationToken);
            string json = await File.ReadAllTextAsync(store.FilePath, TestContext.CancellationToken);

            Assert.AreEqual(settings, loaded);
            Assert.IsFalse(json.Contains("apiKey", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(json.Contains("password", StringComparison.OrdinalIgnoreCase));
        });
    }

    [TestMethod]
    public async Task InvalidValuesCannotBeSaved()
    {
        await WithStore(async store =>
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            {
                await store.SaveAsync(AgentForgeSettings.Default with { AlreadyUsed = -1 }, TestContext.CancellationToken);
            });
            await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            {
                await store.SaveAsync(AgentForgeSettings.Default with
                {
                    Provider = BillingProvider.OpenAiApi, AllowLiveCopilotPricing = true
                }, TestContext.CancellationToken);
            });
            Assert.IsFalse(File.Exists(store.FilePath));
        });
    }

    [TestMethod]
    public async Task OversizedConfigurationIsRejected()
    {
        await WithStore(async store =>
        {
            await File.WriteAllTextAsync(store.FilePath, new string('X', 70_000), TestContext.CancellationToken);
            await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            {
                await store.LoadAsync(TestContext.CancellationToken);
            });
        });
    }

    [TestMethod]
    public async Task SetupWizardStoresCopilotBudgetWithoutACommandLineParameterList()
    {
        await WithStore(async store =>
        {
            using var input = new StringReader("copilot\nselected-model\npersonal\n30000\n9000\nyes\n");
            using var output = new StringWriter();
            int exitCode = await SetupCommand.RunAsync(store, input, output);

            Assert.AreEqual(0, exitCode);
            AgentForgeSettings settings = await store.LoadAsync(TestContext.CancellationToken);
            Assert.AreEqual(30_000m, settings.MonthlyAllowance);
            Assert.AreEqual(9_000m, settings.AlreadyUsed);
            Assert.IsTrue(settings.AllowLiveCopilotPricing);
            StringAssert.Contains(output.ToString(), "agentforge \"Refactor the lighting engine\"");
        });
    }

    [TestMethod]
    public async Task SetupWizardRejectsAmbiguousThousandsSeparator()
    {
        await WithStore(async store =>
        {
            using var input = new StringReader("copilot\nselected-model\npersonal\n30.000\n");
            using var output = new StringWriter();
            int exitCode = await SetupCommand.RunAsync(store, input, output);

            Assert.AreEqual(1, exitCode);
            Assert.IsFalse(File.Exists(store.FilePath));
            StringAssert.Contains(output.ToString(), "without separators");
        });
    }

    [TestMethod]
    public async Task SetupOfNonCopilotProviderNeverConfiguresCopilotCreditBalance()
    {
        await WithStore(async store =>
        {
            using var input = new StringReader("openai\napi-model\napi-personal\n");
            using var output = new StringWriter();
            Assert.AreEqual(0, await SetupCommand.RunAsync(store, input, output));

            AgentForgeSettings settings = await store.LoadAsync(TestContext.CancellationToken);
            Assert.AreEqual(BillingProvider.OpenAiApi, settings.Provider);
            Assert.IsNull(settings.MonthlyAllowance);
            Assert.IsFalse(settings.AllowLiveCopilotPricing);
        });
    }

    private static async Task WithStore(Func<AgentForgeSettingsStore, Task> test)
    {
        string directory = Path.Combine(Path.GetTempPath(), "agentforge-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await test(new AgentForgeSettingsStore(Path.Combine(directory, "settings.json")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
