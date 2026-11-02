using System.Text;
using TokenBudget.Domain;

namespace TokenBudget.Billing;

/// <summary>Read-only quality heuristics that never remove original requirements or approval gates.</summary>
public sealed class PromptScopeAdvisor
{
    /// <summary>Assess scope and optionally wrap the complete original prompt in safer execution boundaries.</summary>
    public PromptScopeAssessment Assess(string original, EstimateReport estimate, bool generateImprovedPrompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(original);
        ArgumentNullException.ThrowIfNull(estimate);
        bool broad = estimate.Milestones.Count >= 4 || estimate.CumulativeTokens.P90 > 300_000 ||
            estimate.Repository.SourceFiles > 1_000;
        bool hasTests = original.Contains("test", StringComparison.OrdinalIgnoreCase) ||
            original.Contains("acceptance", StringComparison.OrdinalIgnoreCase);
        bool hasApproval = estimate.Milestones.Any(static item => item.RequiresApproval);
        var notes = new List<string>();
        if (broad)
        {
            notes.Add("High scope: execute dependency-safe milestones separately and re-estimate each stage.");
        }

        if (!hasTests)
        {
            notes.Add("Explicit acceptance and regression tests are not clearly stated; define them before execution.");
        }

        if (estimate.Repository.Truncated || !estimate.Repository.Available)
        {
            notes.Add("Repository scope is incomplete; resolve source and dependency uncertainty.");
        }

        string? improved = null;
        if (generateImprovedPrompt)
        {
            var result = new StringBuilder();
            result.AppendLine("# Execution contract (AgentForge suggested)");
            result.AppendLine("Execute the ORIGINAL REQUIREMENTS below without dropping any dependencies, acceptance criteria or approval gates.");
            if (broad)
            {
                MilestoneForecast? firstUnblocked = estimate.Milestones.FirstOrDefault(static milestone => milestone.Dependencies.Count == 0);
                if (firstUnblocked is not null)
                {
                    result.AppendLine($"Current execution scope: ONLY milestone {firstUnblocked.Id}: {firstUnblocked.Description}.");
                    result.AppendLine("Do not start later milestones in this execution. Re-estimate and obtain fresh approval for the next stage.");
                }
                else
                {
                    result.AppendLine("STOP before editing: no dependency-free milestone was identified; validate prerequisites first.");
                }
            }

            result.AppendLine("Inspect the relevant repository and dependency boundaries before editing.");
            result.AppendLine("Work in the stated dependency order; complete one independently testable milestone at a time.");
            result.AppendLine("At each milestone: run focused tests, report actual work/usage when observable, and re-estimate the next stage.");
            result.AppendLine("Do not bypass required approvals. Stop at any explicitly mandated approval gate.");
            result.AppendLine("Do not upload repository content or invoke an AI reviewer without explicit consent.");
            result.AppendLine();
            result.AppendLine("# ORIGINAL REQUIREMENTS (verbatim; authoritative)");
            result.Append(original);
            improved = result.ToString();
        }

        return new PromptScopeAssessment(broad, hasTests, hasApproval, notes, improved);
    }
}

/// <summary>Quality observations and an optional conservative, requirement-preserving prompt.</summary>
public sealed record PromptScopeAssessment(bool TooBroad, bool MentionsTesting, bool RequiresHumanApproval,
    IReadOnlyList<string> Notes, string? ImprovedPrompt);
