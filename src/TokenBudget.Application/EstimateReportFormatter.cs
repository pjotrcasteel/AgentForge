using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TokenBudget.Domain;

namespace TokenBudget.Application;

public static class EstimateReportFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ToJson(EstimateReport report) => JsonSerializer.Serialize(report, JsonOptions);

    public static string ToMarkdown(EstimateReport report)
    {
        var output = new StringBuilder();
        output.AppendLine("# TokenBudget preflight").AppendLine();
        output.AppendLine($"- Decision: **{report.Decision}** (confidence: **{report.Confidence}**)");
        output.AppendLine($"- Target: {report.Target}; model: `{report.ModelId.Replace("`", "", StringComparison.Ordinal)}`");
        output.AppendLine($"- Initial input: {report.CountedInitialInputTokens?.ToString("N0", CultureInfo.InvariantCulture) ?? "unknown"} ({report.InitialInputCountSource})");
        output.AppendLine($"- Estimated cumulative tokens: **{Range(report.CumulativeTokens)}**");
        output.AppendLine($"- Estimated peak context (including output reserve): **{Range(report.PeakContextTokens)}**");
        output.AppendLine($"- Risk-adjusted cumulative: **{report.RiskAdjustedCumulativeTokens:N0}**");
        output.AppendLine($"- Risk-adjusted peak context: **{report.RiskAdjustedPeakContextTokens:N0}**");
        string cumulativeBudget = report.Budget.MaxCumulativeTokens?.ToString("N0", CultureInfo.InvariantCulture) ?? "not set";
        string contextBudget = report.Budget.MaxPeakContextTokens?.ToString("N0", CultureInfo.InvariantCulture) ?? "not set";
        output.AppendLine($"- Total budget: {cumulativeBudget}; peak budget: {contextBudget}");
        string repository = report.Repository.Available
            ? $"{report.Repository.SourceFiles:N0} C# files, {report.Repository.ProjectFiles:N0} projects"
            : "not available";
        output.AppendLine($"- Repository: {repository}");
        output.AppendLine($"- Estimator: `{report.EstimatorVersion}`").AppendLine();
        output.AppendLine("## Milestones").AppendLine();
        output.AppendLine("| ID | Description | Cumulative P50 | P90 | Peak P90 | Gates |");
        output.AppendLine("|---|---|---:|---:|---:|---|");
        foreach (MilestoneForecast milestone in report.Milestones)
        {
            string description = milestone.Description.Replace('|', '/');
            string gates = milestone switch
            {
                { RequiresApproval: true } => "Approval",
                { RequiresVisualProof: true } => "Visual proof",
                _ => "—"
            };
            output.AppendLine($"| {milestone.Id} | {description} | {milestone.CumulativeTokens.P50:N0} | " +
                $"{milestone.CumulativeTokens.P90:N0} | {milestone.PeakContextTokens.P90:N0} | {gates} |");
        }

        AddSection(output, "Risks", report.Risks);
        AddSection(output, "Recommendations", report.Recommendations);
        AddSection(output, "Assumptions", report.Assumptions);
        output.AppendLine("**Warning:** This forecast uses uncalibrated priors. Quantiles are hypothetical workload scenarios, not empirically demonstrated probabilities.");
        return output.ToString();
    }

    private static string Range(TokenRange value) =>
        $"P10 {value.P10:N0} / P50 {value.P50:N0} / P90 {value.P90:N0}";

    private static void AddSection(StringBuilder output, string title, IReadOnlyList<string> values)
    {
        output.AppendLine().AppendLine($"## {title}").AppendLine();
        if (values.Count == 0)
        {
            output.AppendLine("None.");
        }
        else
        {
            foreach (string item in values)
            {
                output.AppendLine($"- {item}");
            }
        }
    }
}
