using TokenBudget.Billing;
using TokenBudget.Domain;

namespace TokenBudget.Telemetry;

/// <summary>
/// Pure, deterministic historical coverage analysis. It never modifies token forecasts,
/// provider prices, quota balances or execution approval decisions.
/// </summary>
public static class ProviderCalibrationAnalyzer
{
    private const int MinimumEarlyWarningRuns = 20;
    private const int MinimumReviewRuns = 100;
    private const double ExpectedP90Coverage = 0.90;
    private const double NormalZ95 = 1.95996398454005;

    /// <summary>
    /// Evaluate completed, uniquely identified, explicitly matched runs for one provider/account/model.
    /// The reported lower bound uses the Wilson score interval and is not an accuracy guarantee.
    /// </summary>
    public static ProviderCalibrationDiagnostics Evaluate(ProviderAccount account, string modelId,
        IEnumerable<ProviderSessionCheckpoint> checkpoints)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(account.AccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(checkpoints);

        var runIds = new HashSet<string>(StringComparer.Ordinal);
        var ratios = new List<double>();
        int withinP10 = 0;
        int withinP50 = 0;
        int withinP90 = 0;
        int verifiedCredits = 0;
        int withinCreditP90 = 0;

        foreach (ProviderSessionCheckpoint checkpoint in checkpoints)
        {
            if (!IsComparable(checkpoint, account, modelId))
            {
                continue;
            }

            if (!runIds.Add(checkpoint.Observation.SessionId))
            {
                // Additional copies of one execution can never improve statistical confidence.
                continue;
            }

            long actual = checkpoint.Observation.Usage.TotalTokens;
            TokenRange predicted = checkpoint.Match!.ForecastTokens;
            ratios.Add((double)actual / predicted.P50);
            if (actual <= predicted.P10)
            {
                withinP10++;
            }

            if (actual <= predicted.P50)
            {
                withinP50++;
            }

            if (actual <= predicted.P90)
            {
                withinP90++;
            }

            if (HasComparableVerifiedCredits(checkpoint))
            {
                verifiedCredits++;
                if (checkpoint.Observation.AiCredits <= checkpoint.Match.ForecastCosts!.P90)
                {
                    withinCreditP90++;
                }
            }
        }

        ratios.Sort();
        int count = ratios.Count;
        double? p90Lower = count == 0 ? null : WilsonLowerBound(withinP90, count);
        double? creditLower = verifiedCredits == 0 ? null : WilsonLowerBound(withinCreditP90, verifiedCredits);
        double? median = Median(ratios);
        ProviderCalibrationStatus status = AssessReadiness(count, withinP90, p90Lower);
        bool creditReady = verifiedCredits >= MinimumReviewRuns && creditLower >= ExpectedP90Coverage;

        return new ProviderCalibrationDiagnostics(account, modelId, count, verifiedCredits,
            Fraction(withinP10, count), Fraction(withinP50, count), Fraction(withinP90, count), p90Lower,
            median, Fraction(withinCreditP90, verifiedCredits), creditLower, status, creditReady,
            Recommendation(status, count));
    }

    private static bool IsComparable(ProviderSessionCheckpoint? checkpoint, ProviderAccount account, string modelId)
    {
        if (checkpoint is not { Completed: true, Match: { } match } ||
            checkpoint.Observation is not { IsCumulative: true } observation ||
            observation.Account != account || match.Provider != account.Provider ||
            !string.Equals(match.ModelId, modelId, StringComparison.OrdinalIgnoreCase) ||
            observation.Models is not [var measuredModel] ||
            !string.Equals(measuredModel, modelId, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(observation.SessionId) ||
            match.ForecastTokens is not { P50: > 0 } tokens ||
            tokens.P10 < 0 || tokens.P10 > tokens.P50 || tokens.P90 < tokens.P50 ||
            observation.Usage.InputTokens < 0 || observation.Usage.OutputTokens < 0)
        {
            return false;
        }

        return true;
    }

    private static bool HasComparableVerifiedCredits(ProviderSessionCheckpoint checkpoint)
    {
        return checkpoint.Observation.Account.Provider == BillingProvider.GitHubCopilot &&
            checkpoint.Observation.CreditsVerified && checkpoint.Observation.AiCredits is >= 0 &&
            checkpoint.Match?.CostUnit == BudgetUnit.GitHubAiCredits &&
            checkpoint.Match.ForecastCosts is { P50: > 0 } costs &&
            costs.P10 >= 0 && costs.P10 <= costs.P50 && costs.P90 >= costs.P50;
    }

    private static double? Median(IReadOnlyList<double> sortedValues)
    {
        if (sortedValues.Count == 0)
        {
            return null;
        }

        int center = sortedValues.Count / 2;
        if (sortedValues.Count % 2 != 0)
        {
            return sortedValues[center];
        }

        return (sortedValues[center - 1] + sortedValues[center]) / 2;
    }

    private static double? Fraction(int covered, int total) => total == 0 ? null : (double)covered / total;

    private static double WilsonLowerBound(int covered, int total)
    {
        double proportion = (double)covered / total;
        double zSquared = NormalZ95 * NormalZ95;
        double denominator = 1 + zSquared / total;
        double center = proportion + zSquared / (2 * total);
        double uncertainty = NormalZ95 * Math.Sqrt(proportion * (1 - proportion) / total +
            zSquared / (4d * total * total));
        return Math.Max(0, (center - uncertainty) / denominator);
    }

    private static ProviderCalibrationStatus AssessReadiness(int samples, int covered, double? p90Lower)
    {
        if (samples == 0)
        {
            return ProviderCalibrationStatus.NoMatchedRuns;
        }

        if (samples >= MinimumEarlyWarningRuns && (double)covered / samples < ExpectedP90Coverage)
        {
            return ProviderCalibrationStatus.InvestigateUnderforecast;
        }

        if (samples >= MinimumReviewRuns && p90Lower >= ExpectedP90Coverage)
        {
            return ProviderCalibrationStatus.ReviewCandidate;
        }

        return ProviderCalibrationStatus.CollectMoreEvidence;
    }

    private static string Recommendation(ProviderCalibrationStatus status, int sampleCount)
    {
        return status switch
        {
            ProviderCalibrationStatus.NoMatchedRuns =>
                "No comparable completed executions. Collect explicitly bound provider/account/model run evidence.",
            ProviderCalibrationStatus.InvestigateUnderforecast =>
                "Observed P90 coverage is below 90%. Investigate scope, retries and model usage; do not raise budget confidence.",
            ProviderCalibrationStatus.ReviewCandidate =>
                "The conservative P90 lower bound meets 90%; manual calibration review may begin, with no automatic changes.",
            _ => $"Only {sampleCount} matched {(sampleCount == 1 ? "run" : "runs")} or insufficient P90 lower-bound evidence. " +
                $"At least {MinimumReviewRuns} distinct executions and a 95% Wilson lower bound of 90% are required."
        };
    }
}
