using TokenBudget.Domain;

namespace TokenBudget.Application;

/// <summary>Offline character-based heuristic. This is not a model tokenizer or a provider count.</summary>
public sealed class HeuristicInputTokenCounter : IInputTokenCounter
{
    public bool Supports(ExecutionTarget target, string modelId) => true;

    public Task<TokenCountResult> CountAsync(AnalyzePlanRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Unicode text and source code vary substantially by tokenizer. This is an explicitly uncertain prior.
        long estimate = (long)Math.Ceiling(request.Plan.Length / 3.6);
        return Task.FromResult(new TokenCountResult(Math.Max(1, estimate), MeasurementSource.Heuristic,
            "Estimated from text length (~3.6 characters/token). Excludes unknown system instructions, tool schemas, and attachments."));
    }
}
