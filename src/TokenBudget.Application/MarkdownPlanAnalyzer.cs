using System.Text.RegularExpressions;
using TokenBudget.Domain;

namespace TokenBudget.Application;

/// <summary>Extracts task boundaries and explicit dependencies from untrusted Markdown without executing its content.</summary>
public sealed partial class MarkdownPlanAnalyzer : IPlanAnalyzer
{
    private const int MaxMilestones = 100;

    [GeneratedRegex(@"^\s{0,3}#{1,5}\s+(?<title>.+?)\s*#*\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^(?:\s*[-*+]\s+)?\s*(?:\*\*)?(?<id>(?:[A-Za-z]{1,8}\d{1,3})(?:[-–]\d{1,3})?)\s*(?:[-—–:.]|\*\*\s*[-—–:])\s*(?<title>.+)", RegexOptions.CultureInvariant)]
    private static partial Regex MilestoneRegex();

    [GeneratedRegex(@"(?<![\w])(?<file>[\w./\\-]+\.(?:cs|csproj|sln|slnx|json|md|yml|yaml|razor|ts|tsx|js|css))(?![\w])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileRegex();

    [GeneratedRegex(@"\b(?:depends\s+on|requires|after|blocked\s+by)\s+(?<id>[a-z]{1,8}\d{1,3})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DependencyRegex();

    public Task<PlanGraph> AnalyzeAsync(AnalyzePlanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Plan) || request.Plan.Length > 200_000)
        {
            throw new ArgumentException("Plan must contain between 1 and 200,000 characters.", nameof(request));
        }

        string[] lines = request.Plan.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var boundaries = new List<(int Line, string Id, string Title)>();
        var fallbackHeadings = new List<(int Line, string Id, string Title)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? fence = null;

        for (int i = 0; i < lines.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                string marker = trimmed[..3];
                if (fence is null)
                {
                    fence = marker;
                }
                else if (fence == marker)
                {
                    fence = null;
                }

                continue;
            }

            if (fence is not null)
            {
                continue;
            }

            Match heading = HeadingRegex().Match(lines[i]);
            string content = heading.Success ? heading.Groups["title"].Value : lines[i];
            Match match = MilestoneRegex().Match(content);

            if (match.Success && seen.Add(match.Groups["id"].Value))
            {
                boundaries.Add((i, match.Groups["id"].Value, CleanTitle(match.Groups["title"].Value)));
            }
            else if (heading.Success && lines[i].TrimStart().StartsWith("## ", StringComparison.Ordinal))
            {
                fallbackHeadings.Add((i, $"S{fallbackHeadings.Count + 1}", CleanTitle(content)));
            }
        }

        if (boundaries.Count == 0)
        {
            if (fallbackHeadings.Count > 1)
            {
                boundaries.AddRange(fallbackHeadings);
            }
            else
            {
                boundaries.Add((0, "PLAN", "Complete plan"));
            }
        }

        if (boundaries.Count > MaxMilestones)
        {
            throw new ArgumentException($"Plan contains more than {MaxMilestones} milestones.", nameof(request));
        }

        boundaries.Sort(static (a, b) => a.Line.CompareTo(b.Line));
        var ids = boundaries.Select(static b => b.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var milestones = new List<PlanMilestone>();
        for (int i = 0; i < boundaries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var boundary = boundaries[i];
            int end = i + 1 < boundaries.Count ? boundaries[i + 1].Line : lines.Length;
            string body = string.Join('\n', lines[boundary.Line..end]);
            string[] dependencies = DependencyRegex().Matches(body)
                .Select(static m => m.Groups["id"].Value)
                .Where(id => !id.Equals(boundary.Id, StringComparison.OrdinalIgnoreCase) && ids.Contains(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string[] files = FileRegex().Matches(body)
                .Select(static m => m.Groups["file"].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(100)
                .ToArray();
            bool tests = HasAny(body, "test", "verify", "build", "spec", "regression");
            bool approval = HasAny(body, "approval", "sign-off", "signoff", "acceptance gate", "stop at", "human gate");
            bool visual = HasAny(body, "screenshot", "visual", "rendered", "image proof");
            milestones.Add(new PlanMilestone(boundary.Id, boundary.Title, body, boundary.Line + 1, dependencies, files, tests, approval, visual));
        }

        var warnings = new List<string>();
        if (boundaries.Count == 1 && boundaries[0].Id == "PLAN")
        {
            warnings.Add("No explicit milestones were found; the plan is estimated as one unit.");
        }

        if (HasDependencyCycle(milestones))
        {
            warnings.Add("Explicit milestone dependencies contain a cycle; no automated split should be attempted.");
        }

        return Task.FromResult(new PlanGraph(milestones, warnings));
    }

    private static string CleanTitle(string text) => text.Trim().TrimEnd('*', '#').Trim();

    private static bool HasAny(string body, params string[] terms) =>
        terms.Any(term => body.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static bool HasDependencyCycle(IReadOnlyList<PlanMilestone> stages)
    {
        var graph = stages.ToDictionary(static stage => stage.Id, static stage => stage.Dependencies, StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool Visit(string node)
        {
            if (visiting.Contains(node))
            {
                return true;
            }

            if (!visited.Add(node))
            {
                return false;
            }

            visiting.Add(node);
            foreach (string dependency in graph[node])
            {
                if (graph.ContainsKey(dependency) && Visit(dependency))
                {
                    return true;
                }
            }

            visiting.Remove(node);
            return false;
        }

        return graph.Keys.Any(Visit);
    }
}
