using Microsoft.VisualStudio.TestTools.UnitTesting;
using TokenBudget.Provider.Codex;

namespace TokenBudget.Tests;

[TestClass]
public sealed class CodexJsonlUsageReaderTests
{
    [TestMethod]
    public void Read_CompletedTurn_DoesNotDoubleCountReasoningOrCachedTokens()
    {
        string[] lines =
        [
            "{\"type\":\"thread.started\",\"thread_id\":\"thread1\"}",
            "{\"type\":\"turn.started\"}",
            "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":1000,\"cached_input_tokens\":700,\"output_tokens\":500,\"reasoning_output_tokens\":200}}"
        ];

        var result = new CodexJsonlUsageReader().Read(lines);
        Assert.IsTrue(result.IsComplete);
        Assert.AreEqual(1_500L, result.Usage?.TotalTokens);
        Assert.AreEqual(700L, result.Usage?.CachedInputTokens);
        Assert.AreEqual(200L, result.Usage?.ReasoningOutputTokens);
        Assert.IsNull(result.Usage?.CacheWriteInputTokens);
    }

    [TestMethod]
    public void Read_NoCompletedTurn_MeansUnknownNotZero()
    {
        var result = new CodexJsonlUsageReader().Read(["{\"type\":\"turn.failed\",\"error\":{\"message\":\"fail\"}}"]);
        Assert.IsNull(result.Usage);
        Assert.IsFalse(result.IsComplete);
    }

    [TestMethod]
    public void Read_MissingUsage_DoesNotPretendRunComplete()
    {
        var result = new CodexJsonlUsageReader().Read(["{\"type\":\"turn.completed\"}"]);
        Assert.IsNull(result.Usage);
        Assert.IsFalse(result.IsComplete);
    }
}
