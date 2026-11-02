using System.ComponentModel;
using System.Text.Json;
using TokenBudget.Application;
using TokenBudget.Domain;

namespace TokenBudget.Agent;

/// <summary>
/// Tool surface for the AI agent. Only immutable, already-computed evidence is accessible.
/// There is deliberately no shell, Git write, file edit, network search or command-execution tool.
/// </summary>
public sealed class ReadOnlyAgentTools(EstimateReport report)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private int budgetEvidenceReads;
    private int milestoneDependencyReads;

    public bool UsedBothTools => Volatile.Read(ref budgetEvidenceReads) > 0 &&
        Volatile.Read(ref milestoneDependencyReads) > 0;

    [Description("Return the authoritative token forecast, risk-adjusted counts and budget verdict. This is measured/estimated evidence; do not change it.")]
    public string GetBudgetEvidence()
    {
        Interlocked.Increment(ref budgetEvidenceReads);
        return EstimateReportFormatter.ToJson(report);
    }

    [Description("Return the milestone dependencies and human approval gates from the deterministic parser.")]
    public string GetMilestoneDependencies()
    {
        Interlocked.Increment(ref milestoneDependencyReads);
        var milestones = report.Milestones.Select(static milestone => new
        {
            milestone.Id,
            milestone.Description,
            milestone.Dependencies,
            milestone.RequiresApproval,
            milestone.RequiresVisualProof,
            milestone.CumulativeTokens,
            milestone.PeakContextTokens
        });
        return JsonSerializer.Serialize(milestones, Options);
    }
}
