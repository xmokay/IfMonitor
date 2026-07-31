using System.Text.Json;
using System.Text.Json.Serialization;

namespace IfMonitor;

public sealed class MonitoredAdapter
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class AppConfig
{
    public List<MonitoredAdapter> Adapters { get; set; } = [];
    public bool IsMonitoring { get; set; }
    public bool NotifyOnRecover { get; set; } = true;
    public bool RunAtStartup { get; set; }

    /// <summary>Legacy single-adapter fields; migrated on load.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? AdapterId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? AdapterName { get; set; }

    public void Normalize()
    {
        if (Adapters.Count == 0
            && !string.IsNullOrWhiteSpace(AdapterId))
        {
            Adapters.Add(new MonitoredAdapter
            {
                Id = AdapterId,
                Name = string.IsNullOrWhiteSpace(AdapterName) ? AdapterId : AdapterName!,
            });
        }

        AdapterId = null;
        AdapterName = null;

        Adapters = Adapters
            .Where(a => !string.IsNullOrWhiteSpace(a.Id))
            .GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        foreach (MonitoredAdapter a in Adapters)
        {
            if (string.IsNullOrWhiteSpace(a.Name))
            {
                a.Name = a.Id;
            }
        }
    }

    public bool HasAdapters => Adapters.Count > 0;
}

public static class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IfMonitor");

    public static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return new AppConfig();
            }

            string json = File.ReadAllText(ConfigPath);
            AppConfig config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
            config.Normalize();
            return config;
        }
        catch
        {
            return new AppConfig();
        }
    }

    public static void Save(AppConfig config)
    {
        config.Normalize();
        Directory.CreateDirectory(ConfigDirectory);
        string json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }
}
