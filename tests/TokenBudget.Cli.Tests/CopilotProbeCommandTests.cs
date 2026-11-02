using Microsoft.VisualStudio.TestTools.UnitTesting;
using TokenBudget.Cli.Commands;

namespace TokenBudget.Cli.Tests;

[TestClass]
public sealed class CopilotProbeCommandTests
{
    [TestMethod]
    public async Task LiveBillingLookupRequiresExplicitConsent()
    {
        int exitCode = await CopilotProbeCommand.RunAsync(["copilot-probe",
            "--account", "personal", "--model", "example-model"]);

        Assert.AreEqual(1, exitCode);
    }

    [TestMethod]
    public async Task ExplicitFalseConsentNeverStartsSdk()
    {
        int exitCode = await CopilotProbeCommand.RunAsync(["copilot-probe",
            "--account", "personal", "--model", "example-model", "--allow-live-account-lookup", "false"]);

        Assert.AreEqual(1, exitCode);
    }

    [TestMethod]
    public async Task MissingAccountFailsWithoutAnyLiveCall()
    {
        int exitCode = await CopilotProbeCommand.RunAsync(["copilot-probe",
            "--model", "example-model", "--allow-live-account-lookup", "true"]);

        Assert.AreEqual(1, exitCode);
    }

    [TestMethod]
    public async Task UnknownOptionsFailBeforeCreatingSdkClient()
    {
        int exitCode = await CopilotProbeCommand.RunAsync(["copilot-probe", "--account", "personal",
            "--model", "example-model", "--allow-live-account-lookup", "true", "--send-prompt", "true"]);

        Assert.AreEqual(1, exitCode);
    }
}
