using Microsoft.VisualStudio.TestTools.UnitTesting;
using TokenBudget.Cli.Commands;
using TokenBudget.Cli.Configuration;

namespace TokenBudget.Cli.Tests;

[TestClass]
public sealed class QuickPreflightCommandTests
{
    [TestMethod]
    public async Task SingleNaturalLanguageArgumentProducesTokenAndPromptAdvice()
    {
        await WithStore(async (store, directory) =>
        {
            using var output = new StringWriter();
            int exitCode = await QuickPreflightCommand.RunAsync(
                ["preflight", "Refactor the lighting engine"], store, new StringReader(""), output, directory);

            Assert.AreEqual(2, exitCode);
            StringAssert.Contains(output.ToString(), "Tokens P10 / P50 / P90");
            StringAssert.Contains(output.ToString(), "Prompt scope: Needs narrowing");
            StringAssert.Contains(output.ToString(), "Refactor the lighting engine");
            StringAssert.Contains(output.ToString(), "no task executed");
        });
    }

    [TestMethod]
    public async Task NoSetupStillExplainsThatProviderCostIsUnknown()
    {
        await WithStore(async (store, directory) =>
        {
            using var output = new StringWriter();
            int exitCode = await QuickPreflightCommand.RunAsync(
                ["preflight", "Fix the null guard in Foo.cs and add tests"],
                store, new StringReader(""), output, directory);

            Assert.AreEqual(4, exitCode);
            StringAssert.Contains(output.ToString(), "current model prices unavailable");
            StringAssert.Contains(output.ToString(), "No broad-scope warning detected");
        });
    }

    [TestMethod]
    public async Task UserCanEnterOnePromptInteractively()
    {
        await WithStore(async (store, directory) =>
        {
            using var input = new StringReader("Refactor the lighting engine\n");
            using var output = new StringWriter();
            int exitCode = await QuickPreflightCommand.RunAsync(["preflight"], store, input, output, directory);

            Assert.AreEqual(2, exitCode);
            StringAssert.Contains(output.ToString(), "What should the AI do?");
        });
    }

    [TestMethod]
    public async Task MarkdownFileIsAcceptedAsTheSingleArgument()
    {
        await WithStore(async (store, directory) =>
        {
            const string plan = "## M1 - Fix lighting\nChange the shadow direction in Lighting.cs; run tests.";
            string path = Path.Combine(directory, "plan.md");
            await File.WriteAllTextAsync(path, plan);
            using var output = new StringWriter();
            int exitCode = await QuickPreflightCommand.RunAsync(["preflight", path],
                store, new StringReader(""), output, directory);

            Assert.IsTrue(exitCode is 0 or 2 or 4);
            StringAssert.Contains(output.ToString(), plan);
        });
    }

    [TestMethod]
    public async Task EmptyPromptReturnsValidationErrorBeforeAnyUpload()
    {
        await WithStore(async (store, directory) =>
        {
            using var output = new StringWriter();
            int exitCode = await QuickPreflightCommand.RunAsync(["preflight"],
                store, new StringReader(""), output, directory);

            Assert.AreEqual(1, exitCode);
            StringAssert.Contains(output.ToString(), "Provide a task");
        });
    }

    [TestMethod]
    public async Task OversizedPlanFileIsRejected()
    {
        await WithStore(async (store, directory) =>
        {
            string path = Path.Combine(directory, "huge.md");
            await File.WriteAllTextAsync(path, new string('X', 1024 * 1024 + 1));
            using var output = new StringWriter();
            int exitCode = await QuickPreflightCommand.RunAsync(["preflight", path],
                store, new StringReader(""), output, directory);

            Assert.AreEqual(1, exitCode);
            StringAssert.Contains(output.ToString(), "1 MiB");
        });
    }

    [TestMethod]
    public async Task MissingMarkdownFileIsNotMistakenForTaskText()
    {
        await WithStore(async (store, directory) =>
        {
            using var output = new StringWriter();
            int exitCode = await QuickPreflightCommand.RunAsync(["preflight", "not-found.md"],
                store, new StringReader(""), output, directory);

            Assert.AreEqual(1, exitCode);
            StringAssert.Contains(output.ToString(), "plan file was not found");
        });
    }

    private static async Task WithStore(Func<AgentForgeSettingsStore, string, Task> test)
    {
        string directory = Path.Combine(Path.GetTempPath(), "agentforge-quick-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await test(new AgentForgeSettingsStore(Path.Combine(directory, "settings.json")), directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
