using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TokenBudget.Domain;

namespace TokenBudget.Application;

/// <summary>
/// Exact provider counts for the explicit submitted plan payload, only after opt-in.
/// Agent system messages, hidden tools, files and future calls remain unmeasured.
/// </summary>
public sealed class ProviderAwareInputTokenCounter : IInputTokenCounter
{
    private readonly HttpClient _httpClient;
    private readonly Func<string, string?> _getSecret;
    private readonly HeuristicInputTokenCounter _fallback = new();

    public ProviderAwareInputTokenCounter(HttpClient? httpClient = null, Func<string, string?>? getSecret = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _getSecret = getSecret ?? Environment.GetEnvironmentVariable;
    }

    public bool Supports(ExecutionTarget target, string modelId) =>
        target is ExecutionTarget.OpenAiResponses or ExecutionTarget.AnthropicMessages;

    public async Task<TokenCountResult> CountAsync(AnalyzePlanRequest request, CancellationToken cancellationToken)
    {
        if (!request.AllowProviderUpload)
        {
            return await Fallback(request, "Provider upload not authorized; no external network request was made.", cancellationToken);
        }

        string? secretName = request.Target switch
        {
            ExecutionTarget.OpenAiResponses => "OPENAI_API_KEY",
            ExecutionTarget.AnthropicMessages => "ANTHROPIC_API_KEY",
            _ => null
        };
        if (secretName is null)
        {
            return await Fallback(request, "No provider-specific exact counting for this execution target.", cancellationToken);
        }

        string? secret = _getSecret(secretName);
        if (string.IsNullOrWhiteSpace(secret))
        {
            return await Fallback(request, $"{secretName} is not configured; provider counting unavailable.", cancellationToken);
        }

        try
        {
            return request.Target switch
            {
                ExecutionTarget.OpenAiResponses => await CountOpenAi(request, secret, cancellationToken),
                ExecutionTarget.AnthropicMessages => await CountAnthropic(request, secret, cancellationToken),
                _ => throw new InvalidOperationException("Unsupported exact count target.")
            };
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException or TaskCanceledException)
        {
            return await Fallback(request, $"Provider counting unavailable ({exception.GetType().Name}); offline heuristic substituted.", cancellationToken);
        }
    }

    private async Task<TokenCountResult> CountOpenAi(AnalyzePlanRequest request, string apiKey, CancellationToken token)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses/input_tokens")
        {
            Content = JsonContent.Create(new { model = request.ModelId, input = request.Plan })
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return await SendAndExtract(message, "OpenAI", token);
    }

    private async Task<TokenCountResult> CountAnthropic(AnalyzePlanRequest request, string apiKey, CancellationToken token)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages/count_tokens")
        {
            Content = JsonContent.Create(new
            {
                model = request.ModelId,
                messages = new[] { new { role = "user", content = request.Plan } }
            })
        };
        message.Headers.Add("x-api-key", apiKey);
        message.Headers.Add("anthropic-version", "2023-06-01");
        return await SendAndExtract(message, "Anthropic", token);
    }

    private async Task<TokenCountResult> SendAndExtract(HttpRequestMessage message, string provider, CancellationToken token)
    {
        using HttpResponseMessage response = await _httpClient.SendAsync(message, token);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{provider} token count returned HTTP {(int)response.StatusCode}.");
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(token);
        using JsonDocument json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        if (!json.RootElement.TryGetProperty("input_tokens", out JsonElement count) || !count.TryGetInt64(out long tokens) || tokens <= 0)
        {
            throw new JsonException("Provider token count does not contain a valid input_tokens field.");
        }

        return new TokenCountResult(tokens, MeasurementSource.ProviderCount,
            $"{provider} counted only the submitted plan input; agent instructions, future calls and hidden context are not counted.");
    }

    private async Task<TokenCountResult> Fallback(AnalyzePlanRequest request, string detail, CancellationToken token)
    {
        TokenCountResult estimate = await _fallback.CountAsync(request, token);
        return estimate with { Note = $"{detail} {estimate.Note}" };
    }
}
