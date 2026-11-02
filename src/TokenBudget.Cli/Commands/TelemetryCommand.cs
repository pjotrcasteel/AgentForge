using System.Text.Json;
using Microsoft.Data.Sqlite;
using TokenBudget.Domain;
using TokenBudget.Telemetry;

namespace TokenBudget.Cli.Commands;

/// <summary>Imports completed run counters or reads local calibration evidence.</summary>
internal static class TelemetryCommand
{
    private static readonly string[] IngestionOptions = ["db", "profile", "model", "run-id", "estimate-json"];
    private static readonly string[] HistoryOptions = ["db", "profile", "model"];

    public static async Task<int> RunAsync(string[] args, bool ingestion)
    {
        try
        {
            if (ingestion && args.Length < 2)
            {
                throw new ArgumentException("Expected: ingest-codex <events.jsonl> --db <history.db> --profile <name> --model <id>.");
            }

            CliOptions options = CliOptions.Parse(args, ingestion ? 2 : 1, ingestion ? IngestionOptions : HistoryOptions);
            string database = options.Require("db");
            string profile = options.Require("profile");
            string model = options.Require("model");
            var store = new SqliteUsageHistoryStore(database);

            if (ingestion)
            {
                EstimateReport? estimate = options.Get("estimate-json") is { } reportPath
                    ? CodexTelemetryImporter.ReadEstimate(reportPath)
                    : null;
                CodexRunObservation run = new CodexTelemetryImporter().Read(args[1], profile, model,
                    options.Get("run-id"), estimate);
                IngestResult result = await store.IngestAsync(run);
                ConsoleJson.Write(new { result, run.Complete, run.Usage, run.Warnings });
                return run.Complete ? 0 : 4;
            }

            HistorySummary history = await store.SummarizeAsync(profile, model);
            ConsoleJson.Write(history);
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or
            JsonException or InvalidOperationException or OverflowException or SqliteException)
        {
            Console.Error.WriteLine("TokenBudget telemetry: " + exception.Message);
            return 1;
        }
    }
}
