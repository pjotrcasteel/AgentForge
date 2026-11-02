using System.Globalization;

namespace TokenBudget.Cli;

/// <summary>Strict, case-insensitive parser for unique --name value arguments.</summary>
internal sealed class CliOptions
{
    private readonly Dictionary<string, string> _values;

    private CliOptions(Dictionary<string, string> values)
    {
        _values = values;
    }

    public static CliOptions Parse(string[] args, int startIndex, params string[] supported)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int index = startIndex; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal) ||
                args[index].Length == 2)
            {
                throw new ArgumentException("Expected unique --name value pairs.");
            }

            if (!values.TryAdd(args[index][2..], args[index + 1]))
            {
                throw new ArgumentException("Duplicate option: " + args[index]);
            }
        }

        string? unsupported = values.Keys.FirstOrDefault(name => !supported.Contains(name, StringComparer.OrdinalIgnoreCase));
        if (unsupported is not null)
        {
            throw new ArgumentException("Unknown option --" + unsupported + ".");
        }

        return new CliOptions(values);
    }

    public bool Contains(string name) => _values.ContainsKey(name);

    public string? Get(string name) => _values.GetValueOrDefault(name);

    public string Require(string name)
    {
        string? value = Get(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("--" + name + " is required.");
        }

        return value;
    }

    public long? OptionalLong(string name) => Get(name) is { } value
        ? long.Parse(value, CultureInfo.InvariantCulture)
        : null;

    public decimal? OptionalDecimal(string name) => Get(name) is { } value
        ? decimal.Parse(value, CultureInfo.InvariantCulture)
        : null;

    public decimal Decimal(string name, decimal defaultValue) => OptionalDecimal(name) ?? defaultValue;

    public int Integer(string name, int defaultValue) => Get(name) is { } value
        ? int.Parse(value, CultureInfo.InvariantCulture)
        : defaultValue;

    public double Double(string name, double defaultValue) => Get(name) is { } value
        ? double.Parse(value, CultureInfo.InvariantCulture)
        : defaultValue;

    public bool Boolean(string name) => Get(name) is { } value && bool.Parse(value);
}
