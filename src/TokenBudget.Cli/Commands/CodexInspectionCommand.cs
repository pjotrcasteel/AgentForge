using System.Text.Json;
using TokenBudget.Provider.Codex;

namespace TokenBudget.Cli.Commands;

/// <summary>Reads existing Codex JSONL events without executing Codex.</summary>
internal static class CodexInspectionCommand
{
    public static Task<int> RunAsync(string[] args)
    {
        if (args.Length != 2 || !File.Exists(args[1]))
        {
            Console.Error.WriteLine("Expected: tokenbudget inspect-codex <codex-exec.jsonl>");
            return Task.FromResult(1);
        }

        try
        {
            var summary = new CodexJsonlUsageReader().Read(File.ReadLines(args[1]));
            ConsoleJson.Write(summary);
            return Task.FromResult(summary.IsComplete ? 0 : 4);
        }
        catch (Exception exception) when (exception is IOException or JsonException or OverflowException)
        {
            Console.Error.WriteLine("TokenBudget: " + exception.Message);
            return Task.FromResult(1);
        }
    }
}
