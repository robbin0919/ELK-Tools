namespace osdx.Models;

public class AppConfig
{
    public Dictionary<string, ProfileConfig> Profiles { get; set; } = new();
    public string DefaultProfile { get; set; } = string.Empty;
    public SettingsConfig Settings { get; set; } = new();
}

public class SettingsConfig
{
    public bool GlobalIgnoreSslErrors { get; set; } = false;
    public string LogLevel { get; set; } = "Information";
}

public class ProfileConfig
{
    public ConnectionConfig Connection { get; set; } = new();
    public ExportConfig Export { get; set; } = new();
    public Dictionary<string, object> Queries { get; set; } = new() { { "Default", new { match_all = new { } } } };
}

public class ConnectionConfig
{
    public string Endpoint { get; set; } = string.Empty;
    public string Index { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? Password { get; set; }
    public bool IgnoreSslErrors { get; set; }
}

public class ExportConfig
{
    public string Format { get; set; } = "csv";
    public string[] Fields { get; set; } = Array.Empty<string>();
    public int BatchSize { get; set; } = 5000;
    public string ScrollTimeout { get; set; } = "2m";
    public string OutputPath { get; set; } = "./exports/";
}
