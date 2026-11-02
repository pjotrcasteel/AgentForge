using System.Text.Json;
using System.Text.Json.Serialization;

namespace TokenBudget.Cli.Configuration;

/// <summary>Persists non-secret AgentForge preferences in the user's application-data directory.</summary>
internal sealed class AgentForgeSettingsStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string FilePath { get; } = Path.GetFullPath(path);

    public static AgentForgeSettingsStore ForCurrentUser()
    {
        string? custom = Environment.GetEnvironmentVariable("AGENTFORGE_CONFIG_PATH");
        if (!string.IsNullOrWhiteSpace(custom))
        {
            return new AgentForgeSettingsStore(custom);
        }

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            throw new InvalidOperationException("Unable to resolve user application data; set AGENTFORGE_CONFIG_PATH.");
        }

        return new AgentForgeSettingsStore(Path.Combine(appData, "AgentForge", "settings.json"));
    }

    public async Task<AgentForgeSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
        {
            return AgentForgeSettings.Default;
        }

        if (new FileInfo(FilePath).Length > 64 * 1024)
        {
            throw new InvalidDataException("AgentForge settings exceed the 64 KiB safety limit.");
        }

        string content = await File.ReadAllTextAsync(FilePath, cancellationToken);
        AgentForgeSettings value = JsonSerializer.Deserialize<AgentForgeSettings>(content, JsonOptions) ??
            throw new InvalidDataException("AgentForge settings are empty or invalid.");
        value.Validate();
        return value;
    }

    public async Task SaveAsync(AgentForgeSettings value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        await File.WriteAllTextAsync(FilePath, JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine,
            cancellationToken);
    }
}
