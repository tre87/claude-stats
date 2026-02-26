using System.Text.Json;

namespace ClaudeStats.Console.Configuration;

public static class ConfigManager
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude-stats");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task<AppConfig> LoadOrCreateAsync()
    {
        if (!File.Exists(ConfigPath))
            return new AppConfig();

        var json = await File.ReadAllTextAsync(ConfigPath);
        return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
    }

    public static async Task SaveAsync(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(ConfigPath, json);
    }

    public static void Clear()
    {
        if (File.Exists(ConfigPath))
            File.Delete(ConfigPath);
    }
}
