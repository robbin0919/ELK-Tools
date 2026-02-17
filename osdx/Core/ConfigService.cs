using System.Text.Json;
using osdx.Models;

namespace osdx.Core;

public static class ConfigService
{
    private static readonly string ConfigPath = "config.json";

    public static AppConfig LoadConfig()
    {
        if (!File.Exists(ConfigPath))
        {
            return new AppConfig();
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public static void SaveConfig(AppConfig config)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(config, options);
        File.WriteAllText(ConfigPath, json);
    }

    public static void AddProfile(string name, ProfileConfig profile)
    {
        var config = LoadConfig();
        config.Profiles[name] = profile;
        SaveConfig(config);
    }
}
