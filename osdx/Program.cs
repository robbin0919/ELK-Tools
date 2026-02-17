using System.CommandLine;
using Spectre.Console;
using Serilog;
using osdx.Models;
using osdx.UI;

// 初始化日誌
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Verbose() 
    .WriteTo.File("logs/osdx-.log", 
        rollingInterval: RollingInterval.Day, 
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
        flushToDiskInterval: TimeSpan.FromSeconds(1)) // 每秒強制刷新到磁碟
    .CreateLogger();

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
        InteractiveWizard.Run();
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
