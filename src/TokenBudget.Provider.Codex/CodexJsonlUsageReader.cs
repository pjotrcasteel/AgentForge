using System.Text.Json;
using TokenBudget.Domain;

namespace TokenBudget.Provider.Codex;

/// <summary>Offline inspection of Codex CLI JSONL. No command execution and no persistence.</summary>
public sealed class CodexJsonlUsageReader
{
    public sealed record Summary(
        string? ThreadId,
        int CompletedTurns,
        int FailedTurns,
        UsageBreakdown? Usage,
        bool IsComplete,
        IReadOnlyList<string> Warnings);

    public Summary Read(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        string? threadId = null;
        int completed = 0;
        int failed = 0;
        long input = 0;
        long output = 0;
        long cached = 0;
        long cacheWrite = 0;
        long reasoning = 0;
        bool cachedKnown = true;
        bool cacheWriteKnown = true;
        bool reasoningKnown = true;
        bool complete = false;
        bool malformedCompletion = false;
        var warnings = new List<string>();
        int lineCount = 0;

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (++lineCount > 100_000 || line.Length > 1_000_000)
            {
                throw new InvalidDataException("JSONL event count or line-length limit exceeded.");
            }

            using JsonDocument json = JsonDocument.Parse(line);
            JsonElement root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !TryString(root, "type", out string? type))
            {
                continue;
            }

            switch (type)
            {
                case "thread.started":
                    if (TryString(root, "thread_id", out string? id))
                    {
                        threadId = id;
                    }

                    break;
                case "turn.started":
                    complete = false;
                    break;
                case "turn.failed":
                    failed++;
                    complete = false;
                    break;
                case "turn.completed":
                    if (!root.TryGetProperty("usage", out JsonElement usage) || usage.ValueKind != JsonValueKind.Object ||
                        !TryNonnegativeLong(usage, "input_tokens", out long turnInput) ||
                        !TryNonnegativeLong(usage, "output_tokens", out long turnOutput))
                    {
                        warnings.Add($"Turn completion at event {lineCount} has missing or invalid usage; run totals are incomplete.");
                        malformedCompletion = true;
                        complete = false;
                        break;
                    }

                    input = checked(input + turnInput);
                    output = checked(output + turnOutput);
                    AggregateOptional(usage, "cached_input_tokens", ref cached, ref cachedKnown);
                    AggregateOptional(usage, "cache_write_input_tokens", ref cacheWrite, ref cacheWriteKnown);
                    AggregateOptional(usage, "reasoning_output_tokens", ref reasoning, ref reasoningKnown);
                    completed++;
                    complete = true;
                    break;
                case "error":
                    complete = false;
                    break;
            }
        }

        if (failed > 0)
        {
            warnings.Add("Run contains failed turns; measurements are partial and must not calibrate completed-run forecasts.");
        }

        if (completed == 0)
        {
            warnings.Add("No completed turn with valid usage was found; total is unknown, not zero.");
        }

        if (!complete)
        {
            warnings.Add("The event stream does not end with an authoritative successful turn completion.");
        }

        UsageBreakdown? totals = completed > 0
            ? new UsageBreakdown(input, output, cachedKnown ? cached : null,
                cacheWriteKnown ? cacheWrite : null, reasoningKnown ? reasoning : null)
            : null;

        if (malformedCompletion)
        {
            warnings.Add("At least one completed turn had no valid usage; this recording must not be used for calibration.");
        }

        return new Summary(threadId, completed, failed, totals, complete && failed == 0 && !malformedCompletion && completed > 0, warnings);
    }

    private static bool TryString(JsonElement json, string property, out string? value)
    {
        value = null;
        if (!json.TryGetProperty(property, out JsonElement item) || item.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = item.GetString();
        return !string.IsNullOrEmpty(value);
    }

    private static bool TryNonnegativeLong(JsonElement json, string property, out long value)
    {
        value = 0;
        return json.TryGetProperty(property, out JsonElement item) && item.ValueKind == JsonValueKind.Number &&
            item.TryGetInt64(out value) && value >= 0;
    }

    private static void AggregateOptional(JsonElement json, string property, ref long total, ref bool known)
    {
        if (!TryNonnegativeLong(json, property, out long value))
        {
            known = false;
            return;
        }

        total = checked(total + value);
    }
}
