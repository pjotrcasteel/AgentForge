using System.Text.Json;
using System.Text.Json.Serialization;
using TokenBudget.Billing;
using TokenBudget.Telemetry;

namespace TokenBudget.Cli.Commands;

/// <summary>Read-only calibration diagnostics for exact provider/account/model historical matches.</summary>
internal static class ProviderCalibrationCommand
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            CliOptions options = CliOptions.Parse(args, 1, "db", "provider", "account", "model");
            var account = new ProviderAccount(ProviderBudgetCommand.ParseProvider(options.Require("provider")),
                options.Require("account"));
            string databasePath = options.Require("db");
            if (!File.Exists(databasePath))
            {
                throw new ArgumentException("Calibration requires an existing usage database. Import a session first.");
            }

            var store = new SqliteProviderSessionStore(databasePath);
            ProviderCalibrationDiagnostics diagnostics = await store.AnalyzeCalibrationAsync(account,
                options.Require("model"));
            Console.WriteLine(JsonSerializer.Serialize(diagnostics, Options));
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
