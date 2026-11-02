using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TokenBudget.Domain;
using TokenBudget.Provider.Codex;

namespace TokenBudget.Telemetry;

/// <summary>Imports usage counters only; raw Codex messages and transcripts are never persisted.</summary>
public sealed class CodexTelemetryImporter
{
    public const int MaxTranscriptBytes = 16 * 1024 * 1024;

    private static readonly JsonSerializerOptions ReportOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public CodexRunObservation Read(string path, string profile, string modelId,
        string? runId = null, EstimateReport? estimate = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("Codex event recording not found.", path);
        }

        if (string.IsNullOrWhiteSpace(profile) || profile.Length > 128 ||
            string.IsNullOrWhiteSpace(modelId) || modelId.Length > 128)
        {
            throw new ArgumentException("A profile and a model ID (1..128 characters) are required.");
        }

        var info = new FileInfo(path);
        if (info.Length > MaxTranscriptBytes)
        {
            throw new InvalidDataException("Codex recording exceeds the 16 MiB import limit.");
        }

        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length > MaxTranscriptBytes)
        {
            throw new InvalidDataException("Codex recording exceeds the 16 MiB import limit.");
        }

        string fingerprint = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        // Windows PowerShell 5.1 can redirect CLI stdout as UTF-16 LE; accept BOM-marked files.
        Encoding encoding = new UTF8Encoding(false, true);
        if (bytes.Length >= 2)
        {
            switch ((bytes[0], bytes[1]))
            {
                case (0xFF, 0xFE):
                    encoding = new UnicodeEncoding(false, true, true);
                    break;
                case (0xFE, 0xFF):
                    encoding = new UnicodeEncoding(true, true, true);
                    break;
            }
        }
        string content = encoding.GetString(bytes).TrimStart('\uFEFF');
        string[] lines = content.Split('\n');
        var summary = new CodexJsonlUsageReader().Read(lines);
        string identity = runId ?? (summary.ThreadId is { Length: > 0 } threadId
            ? $"codex:{threadId}"
            : $"codex:sha256:{fingerprint}");
        if (string.IsNullOrWhiteSpace(identity) || identity.Length > 256)
        {
            throw new ArgumentException("Run ID is required and must be at most 256 characters.");
        }

        if (estimate is not null && (estimate.Target is not ExecutionTarget.CodexCli and not ExecutionTarget.CodexAppServer ||
            !string.Equals(estimate.ModelId, modelId, StringComparison.Ordinal)))
        {
            throw new ArgumentException("The matched estimate must target Codex and have the same model identifier.");
        }

        return new CodexRunObservation(identity, fingerprint, profile, modelId, summary.ThreadId,
            summary.IsComplete, summary.CompletedTurns, summary.FailedTurns, summary.Usage,
            estimate?.EstimateId, estimate?.CumulativeTokens.P50, estimate?.CumulativeTokens.P90,
            summary.Warnings);
    }

    public static EstimateReport ReadEstimate(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024)
        {
            throw new InvalidDataException("Estimate JSON must exist and be at most 1 MiB.");
        }

        return JsonSerializer.Deserialize<EstimateReport>(File.ReadAllText(path), ReportOptions) ??
            throw new InvalidDataException("Estimate file did not contain a valid estimate report.");
    }
}

public sealed record CodexRunObservation(
    string RunId,
    string Fingerprint,
    string Profile,
    string ModelId,
    string? ThreadId,
    bool Complete,
    int CompletedTurns,
    int FailedTurns,
    UsageBreakdown? Usage,
    Guid? EstimateId,
    long? ForecastP50,
    long? ForecastP90,
    IReadOnlyList<string> Warnings);

public sealed record HistorySummary(
    string Profile,
    string ModelId,
    int TotalRuns,
    int CompletedRuns,
    int MatchedCompletedRuns,
    double? MedianActualToP50Ratio,
    double? ObservedP90Coverage,
    bool SufficientForCalibration,
    string Note);

public sealed record IngestResult(bool Inserted, string RunId, bool EligibleForCalibration);
