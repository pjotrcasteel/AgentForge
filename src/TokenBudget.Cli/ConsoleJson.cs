using System.Text.Json;

namespace TokenBudget.Cli;

/// <summary>Consistent human-readable JSON for CLI diagnostics and telemetry.</summary>
internal static class ConsoleJson
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static void Write<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, Options));
}
