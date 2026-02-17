using System.CommandLine;
using Spectre.Console;
using Serilog;
using osdx.Models;
using osdx.UI;
using System.Text.Json;

// 預讀取設定以取得日誌等級
string logLevel = "Information";
try
{
    if (File.Exists("config.json"))
    {
        var json = File.ReadAllText("config.json");
        var config = JsonSerializer.Deserialize<AppConfig>(json);
        if (config?.Settings?.LogLevel != null) logLevel = config.Settings.LogLevel;
    }
}
catch { /* 忽略讀取錯誤，使用預設值 */ }

// 初始化日誌
var logConfig = new LoggerConfiguration()
    .MinimumLevel.Is(Enum.Parse<Serilog.Events.LogEventLevel>(logLevel)) 
    .WriteTo.File("logs/osdx-.log", 
        rollingInterval: RollingInterval.Day, 
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
        flushToDiskInterval: TimeSpan.FromSeconds(1));

Log.Logger = logConfig.CreateLogger();

try 
{
    Log.Information("OSDX 程式啟動");

    if (args.Length > 0)
    {
        AnsiConsole.Write(new FigletText("OSDX").Color(Color.Blue));
        // 自動化模式 (Automation Mode)
        AnsiConsole.MarkupLine("[yellow]偵測到命令列參數，啟動自動化模式...[/]");
        // TODO: 解析參數並呼叫 DataStreamer
    }
    else
    {
        // 引導模式 (Interactive Mode)
        await InteractiveWizard.RunAsync();
    }
}
catch (Exception ex)
{
    Log.Fatal(ex, "程式因未預期的錯誤而終止");
    AnsiConsole.WriteException(ex);
}
finally
{
    Log.Information("OSDX 程式結束");
    Log.CloseAndFlush();
}
