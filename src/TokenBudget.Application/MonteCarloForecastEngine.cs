using TokenBudget.Domain;

namespace TokenBudget.Application;

/// <summary>
/// Seeded, uncalibrated Monte Carlo workload forecast. The distributions are conservative design priors,
/// not empirically validated probabilities. Only measured executions can calibrate them.
/// </summary>
public sealed class MonteCarloForecastEngine(IBudgetPolicy policy) : ITokenForecastEngine
{
    public const string Version = "0.1.0-prior";

    public Task<EstimateReport> ForecastAsync(
        AnalyzePlanRequest request,
        PlanGraph graph,
        RepositoryImpact impact,
        TokenCountResult initialInput,
        CancellationToken cancellationToken)
    {
        int runs = request.SimulationCount;
        var random = new Random(request.RandomSeed);
        var cumulative = new long[runs];
        var inputTotals = new long[runs];
        var outputTotals = new long[runs];
        var peaks = new long[runs];
        var milestoneTotals = graph.Milestones.Select(_ => new long[runs]).ToArray();
        var milestonePeaks = graph.Milestones.Select(_ => new long[runs]).ToArray();

        for (int sample = 0; sample < runs; sample++)
        {
            if ((sample & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            // One run-wide factor correlates complexity and debugging across all milestones.
            double runWideDifficulty = 0.65 + 1.15 * random.NextDouble();
            for (int stageIndex = 0; stageIndex < graph.Milestones.Count; stageIndex++)
            {
                PlanMilestone milestone = graph.Milestones[stageIndex];
                (long input, long output, long peak) = SimulateMilestone(
                    random, runWideDifficulty, milestone, impact, initialInput.Count);

                long total = checked(input + output);

                milestoneTotals[stageIndex][sample] = total;
                milestonePeaks[stageIndex][sample] = peak;
                cumulative[sample] = checked(cumulative[sample] + total);
                inputTotals[sample] = checked(inputTotals[sample] + input);
                outputTotals[sample] = checked(outputTotals[sample] + output);
                peaks[sample] = Math.Max(peaks[sample], peak);
            }
        }

        var cumulativeTokens = Quantiles(cumulative);
        var peakContextTokens = Quantiles(peaks);
        long riskTotal = Quantile(cumulative, 1.0 - request.Budget.RiskTolerance);
        long riskPeak = Quantile(peaks, 1.0 - request.Budget.RiskTolerance);
        bool approval = graph.Milestones.Any(static milestone => milestone.RequiresApproval);
        bool unknownScope = !impact.Available || impact.Truncated;
        var risks = new List<string>(graph.Warnings);
        var recommendations = new List<string>();
        var assumptions = new List<string>
        {
            $"Uncalibrated Monte Carlo priors ({runs:N0} seeded samples; seed={request.RandomSeed}).",
            "Estimated model-call counts, reasoning/output, tool schemas, retained history, tool results and retries.",
            "Peak context includes a reserved output allowance; cumulative usage sums input and output across calls.",
            "No previous completed execution has been used for calibration."
        };

        if (!string.IsNullOrWhiteSpace(initialInput.Note))
        {
            assumptions.Add(initialInput.Note);
        }

        risks.Add("P10/P50/P90 are modeled scenarios, not validated statistical coverage probabilities.");
        if (initialInput.Source == MeasurementSource.Heuristic)
        {
            risks.Add("Initial prompt size is an offline heuristic; the provider's exact token count is unknown.");
        }

        if (unknownScope)
        {
            risks.Add(impact.Reason ?? "Repository scope is incomplete.");
            recommendations.Add("Supply a local repository checkout to improve the impact estimate.");
        }

        if (approval)
        {
            risks.Add("The plan has mandatory approval gates; budget approval never bypasses human acceptance.");
        }

        if (request.AllowProviderUpload)
        {
            assumptions.Add("Provider counting is only attempted for supported direct API targets; Codex/Work preflight remains offline.");
        }

        if (request.Budget.MaxApiCostUsd is not null)
        {
            risks.Add("USD cost cannot yet be validated because provider-specific prices and caching are not implemented.");
        }

        if (request.Target == ExecutionTarget.ChatGptWork)
        {
            risks.Add("ChatGPT Work internal token consumption is not exposed to this estimator.");
        }

        bool hasReliableEvidence = impact.Available && !impact.Truncated && initialInput.Source != MeasurementSource.Heuristic;
        BudgetDecision decision = policy.Evaluate(
            request.Budget, riskTotal, riskPeak, hasReliableEvidence, approval);

        if (decision == BudgetDecision.Reject)
        {
            recommendations.Add("Reduce the scope at explicit milestone boundaries and re-estimate each dependency-safe stage.");
        }
        else if (decision == BudgetDecision.Warning)
        {
            recommendations.Add("Treat this forecast as advisory until actual run telemetry can calibrate the model.");
        }

        if (graph.Milestones.Count > 1)
        {
            recommendations.Add("Use existing milestone boundaries for execution checkpoints; preserve every dependency and approval gate.");
        }

        var milestones = graph.Milestones.Select((milestone, index) => new MilestoneForecast(
            milestone.Id,
            milestone.Description,
            Quantiles(milestoneTotals[index]),
            Quantiles(milestonePeaks[index]),
            milestone.Dependencies,
            ["Sampled call count, input context growth, output tokens and retry overhead."],
            milestone.RequiresApproval,
            milestone.RequiresVisualProof)).ToArray();

        return Task.FromResult(new EstimateReport(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            request.Target,
            request.ModelId,
            initialInput.Count,
            initialInput.Source,
            cumulativeTokens,
            peakContextTokens,
            riskTotal,
            riskPeak,
            ForecastConfidence.Low,
            decision,
            request.Budget,
            impact,
            milestones,
            risks,
            recommendations,
            assumptions,
            Version,
            new TokenUsageForecast(Quantiles(inputTotals), Quantiles(outputTotals))));
    }

    private static (long Input, long Output, long Peak) SimulateMilestone(
        Random random,
        double difficulty,
        PlanMilestone milestone,
        RepositoryImpact impact,
        long initialPromptTokens)
    {
        int listedFiles = milestone.ReferencedFiles.Count;
        double complexity = Math.Clamp(1 + milestone.Body.Length / 3_000.0 + listedFiles * 0.15, 1, 7);
        int baselineCalls = 3 + (int)Math.Ceiling(complexity * 1.4) + (milestone.RequiresTests ? 3 : 0) +
            (milestone.RequiresVisualProof ? 2 : 0);
        int calls = Math.Max(1, (int)Math.Ceiling(baselineCalls * difficulty * (0.7 + random.NextDouble() * 0.65)));
        double debugChance = milestone.RequiresTests ? 0.24 : 0.13;
        if (random.NextDouble() < debugChance * Math.Min(1.6, difficulty))
        {
            calls += Math.Max(1, (int)Math.Ceiling(2 * complexity * random.NextDouble()));
        }

        // The repository scale only contributes metadata-based context uncertainty; no source is uploaded/read.
        double repoContext = impact.Available
            ? Math.Min(5_000, 300 + Math.Sqrt(impact.SourceFiles) * 90 + Math.Min(8, listedFiles) * 350)
            : 1_400 + Math.Min(8, listedFiles) * 400;
        long accumulatedInput = 0;
        long accumulatedOutput = 0;
        long peak = 0;
        for (int call = 0; call < calls; call++)
        {
            double contextGrowth = Math.Min(12_000, call * (100 + 45 * complexity));
            double input = initialPromptTokens * (0.5 + random.NextDouble() * 0.75) +
                2_200 + repoContext * (0.55 + random.NextDouble()) + contextGrowth;
            double output = (500 + 550 * complexity + (milestone.RequiresTests ? 400 : 0)) *
                (0.65 + random.NextDouble() * 0.95);
            long inputTokens = (long)Math.Ceiling(input);
            long outputTokens = (long)Math.Ceiling(output);
            long reservedOutput = Math.Max(2_048, (long)Math.Ceiling(output * 1.4));
            accumulatedInput = checked(accumulatedInput + inputTokens);
            accumulatedOutput = checked(accumulatedOutput + outputTokens);
            peak = Math.Max(peak, inputTokens + reservedOutput);
        }

        return (accumulatedInput, accumulatedOutput, peak);
    }

    private static TokenRange Quantiles(long[] values) => new(
        Quantile(values, 0.10), Quantile(values, 0.50), Quantile(values, 0.90));

    private static long Quantile(long[] values, double probability)
    {
        // Does not mutate callers' samples; summation and per-stage arrays remain usable.
        long[] sorted = (long[])values.Clone();
        Array.Sort(sorted);
        int index = (int)Math.Floor((sorted.Length - 1) * probability);
        return sorted[index];
    }
}
