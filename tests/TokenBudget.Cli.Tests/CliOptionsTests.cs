using Microsoft.VisualStudio.TestTools.UnitTesting;
using TokenBudget.Cli;

namespace TokenBudget.Cli.Tests;

[TestClass]
public sealed class CliOptionsTests
{
    [TestMethod]
    public void Parse_HandlesCaseInsensitiveOptions()
    {
        CliOptions options = CliOptions.Parse(["analyze", "plan.md", "--MODEL", "gpt", "--samples", "2000"],
            2, "model", "samples");

        Assert.AreEqual("gpt", options.Require("model"));
        Assert.AreEqual(2000, options.Integer("SAMPLES", 1000));
    }

    [TestMethod]
    public void Parse_RejectsDuplicateOptionsRegardlessOfCase()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            CliOptions.Parse(["history", "--db", "a.db", "--DB", "b.db"], 1, "db"));
    }

    [TestMethod]
    public void Parse_RejectsUnknownFlags()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            CliOptions.Parse(["history", "--secret", "abc"], 1, "db"));
    }

    [TestMethod]
    public void Parse_RejectsUnpairedFlags()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            CliOptions.Parse(["history", "--db"], 1, "db"));
    }

    [TestMethod]
    public void Parse_RejectsEmptyNames()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            CliOptions.Parse(["history", "--", "value"], 1, "db"));
    }

    [TestMethod]
    public void Require_RejectsMissingOrBlankValue()
    {
        CliOptions options = CliOptions.Parse(["history", "--model", " "], 1, "model");
        Assert.ThrowsExactly<ArgumentException>(() => options.Require("model"));
        Assert.ThrowsExactly<ArgumentException>(() => options.Require("db"));
    }

    [TestMethod]
    public void Parse_UsesInvariantNumbersAndExplicitDefaults()
    {
        CliOptions options = CliOptions.Parse(["analyze", "--risk", "0.25", "--budget", "1234"],
            1, "risk", "budget");

        Assert.AreEqual(0.25, options.Double("risk", 0.1));
        Assert.AreEqual(1234L, options.OptionalLong("budget"));
        Assert.AreEqual(1729, options.Integer("seed", 1729));
        Assert.IsFalse(options.Boolean("upload"));
    }
}
