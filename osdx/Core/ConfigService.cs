using System.Text.Json;
using System.Text.Json.Nodes;
using osdx.Models;

namespace osdx.Core;

public static class ConfigService
{
    private static readonly string AppSettingsPath =
        Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public static string GetLogLevel()
    {
        if (!File.Exists(AppSettingsPath)) return "Information";
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(AppSettingsPath));
            return root?["Serilog"]?["MinimumLevel"]?.GetValue<string>() ?? "Information";
        }
        catch { return "Information"; }
    }

    public static void SetLogLevel(string level)
    {
        JsonNode root;
        if (File.Exists(AppSettingsPath))
        {
            try { root = JsonNode.Parse(File.ReadAllText(AppSettingsPath)) ?? new JsonObject(); }
            catch { root = new JsonObject(); }
        }
        else
        {
            root = new JsonObject();
        }

        root["Serilog"] ??= new JsonObject();
        root["Serilog"]!["MinimumLevel"] = level;

        File.WriteAllText(AppSettingsPath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

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
