using System.Text.Json;
using System.Text.Json.Serialization;
using TokenBudget.Billing;
using TokenBudget.Telemetry;

namespace TokenBudget.Cli.Commands;

/// <summary>Explicitly opted-in metrics-only provider session persistence and historical backtesting.</summary>
internal static class ProviderSessionCommand
{
    private const long MaxSnapshotBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<int> RunAsync(string[] args, bool ingestion)
    {
        try
        {
            if (ingestion)
            {
                if (args.Length < 2 || !File.Exists(args[1]) || new FileInfo(args[1]).Length > MaxSnapshotBytes)
                {
                    throw new ArgumentException("Supply an existing provider session JSON snapshot no larger than 1 MiB.");
                }

                CliOptions options = CliOptions.Parse(args, 2, "db");
                ProviderSessionCheckpoint checkpoint = JsonSerializer.Deserialize<ProviderSessionCheckpoint>(
                    await File.ReadAllTextAsync(args[1]), Options) ?? throw new ArgumentException("Invalid snapshot JSON.");
                var store = new SqliteProviderSessionStore(options.Require("db"));
                ProviderSessionIngestResult result = await store.IngestAsync(checkpoint);
                Console.WriteLine(JsonSerializer.Serialize(result, Options));
                return 0;
            }

            CliOptions history = CliOptions.Parse(args, 1, "db", "provider", "account", "model");
            var account = new ProviderAccount(ProviderBudgetCommand.ParseProvider(history.Require("provider")),
                history.Require("account"));
            var ledger = new SqliteProviderSessionStore(history.Require("db"));
            ProviderSessionHistorySummary summary = await ledger.SummarizeAsync(account, history.Require("model"));
            Console.WriteLine(JsonSerializer.Serialize(summary, Options));
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or
            JsonException or InvalidOperationException or OverflowException or FormatException)
        {
            Console.Error.WriteLine("TokenBudget: " + exception.Message);
            return 1;
        }
    }
}
