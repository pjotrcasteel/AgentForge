using System.Text;
using TokenBudget.Application;

namespace TokenBudget.Agent;

public static class AgentPreflightFormatter
{
    public static string ToMarkdown(AgentPreflightResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var output = new StringBuilder(EstimateReportFormatter.ToMarkdown(result.Estimate));
        if (result.UsedAiReview)
        {
            output.AppendLine().AppendLine("---").AppendLine();
            output.AppendLine("## AI advisory (non-authoritative)").AppendLine();
            output.AppendLine($"Model: `{result.ReviewModelId?.Replace("`", "", StringComparison.Ordinal)}`");
            output.AppendLine("AI suggestions cannot override the estimate, approval gates or the budget verdict.").AppendLine();
            output.AppendLine(result.AiAdvisory);
        }

        return output.ToString();
    }
}
