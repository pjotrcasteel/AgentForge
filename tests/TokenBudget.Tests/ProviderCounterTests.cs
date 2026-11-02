using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TokenBudget.Application;
using TokenBudget.Domain;

namespace TokenBudget.Tests;

[TestClass]
public sealed class ProviderCounterTests
{
    [TestMethod]
    public async Task CountAsync_WithoutOptIn_NeverCallsProvider()
    {
        var handler = new ResponseHandler();
        var counter = new ProviderAwareInputTokenCounter(new HttpClient(handler), _ => "fake-key");
        var request = Request(ExecutionTarget.OpenAiResponses, false);

        TokenCountResult result = await counter.CountAsync(request, TestContext.CancellationToken);
        Assert.AreEqual(0, handler.Calls);
        Assert.AreEqual(MeasurementSource.Heuristic, result.Source);
    }

    [TestMethod]
    public async Task CountAsync_WithOptIn_UsesOpenAiCount()
    {
        var handler = new ResponseHandler();
        var counter = new ProviderAwareInputTokenCounter(new HttpClient(handler), _ => "fake-key");
        TokenCountResult result = await counter.CountAsync(Request(ExecutionTarget.OpenAiResponses, true), TestContext.CancellationToken);

        Assert.AreEqual(123L, result.Count);
        Assert.AreEqual(MeasurementSource.ProviderCount, result.Source);
        Assert.AreEqual(1, handler.Calls);
        StringAssert.Contains(handler.LastPath ?? "", "/v1/responses/input_tokens");
    }

    [TestMethod]
    public async Task CountAsync_WithOptIn_UsesAnthropicCount()
    {
        var handler = new ResponseHandler();
        var counter = new ProviderAwareInputTokenCounter(new HttpClient(handler), _ => "fake-key");
        TokenCountResult result = await counter.CountAsync(Request(ExecutionTarget.AnthropicMessages, true), TestContext.CancellationToken);

        Assert.AreEqual(MeasurementSource.ProviderCount, result.Source);
        StringAssert.Contains(handler.LastPath ?? "", "/v1/messages/count_tokens");
    }

    [TestMethod]
    public async Task CountAsync_MissingApiKey_FallsBackWithoutNetworkRequest()
    {
        var handler = new ResponseHandler();
        var counter = new ProviderAwareInputTokenCounter(new HttpClient(handler), _ => null);
        TokenCountResult result = await counter.CountAsync(Request(ExecutionTarget.AnthropicMessages, true), TestContext.CancellationToken);

        Assert.AreEqual(MeasurementSource.Heuristic, result.Source);
        Assert.AreEqual(0, handler.Calls);
        StringAssert.Contains(result.Note ?? "", "not configured");
    }

    private static AnalyzePlanRequest Request(ExecutionTarget target, bool optIn) =>
        new("Analyze this plan.", "example-model", target, new(), AllowProviderUpload: optIn);

    public TestContext TestContext { get; set; } = null!;

    private sealed class ResponseHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? LastPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastPath = request.RequestUri?.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"input_tokens\":123}")
            });
        }
    }
}
