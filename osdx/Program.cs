using System.CommandLine;
using Spectre.Console;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.File;
using System.Linq;
using osdx.Models;
using osdx.UI;
using osdx.Core;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

// 初始化日誌 (從 appsettings.json 讀取 Serilog 設定)
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .Build();

// 若為互動式 (TUI)，不要把日誌輸出到 Console（會破壞 Spectre.Console 的畫面）
bool isInteractive = args.Length == 0;
var serilogSection = configuration.GetSection("Serilog");
var minLevelStr = serilogSection["MinimumLevel"] ?? "Information";
LogEventLevel ParseLevel(string s) => Enum.TryParse<LogEventLevel>(s, true, out var lv) ? lv : LogEventLevel.Information;
var level = ParseLevel(minLevelStr);

var loggerCfg = new LoggerConfiguration()
    .MinimumLevel.Is(level)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "osdx");

// 讀取 File Sink 的設定（若存在）
var fileSection = serilogSection.GetSection("WriteTo").GetChildren().FirstOrDefault(c => string.Equals(c["Name"], "File", StringComparison.OrdinalIgnoreCase));
if (isInteractive)
{
    if (fileSection.Exists())
    {
        var argsSec = fileSection.GetSection("Args");
        var path = argsSec["path"] ?? "logs/osdx-.log";
        var template = argsSec["outputTemplate"] ?? "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";
        var rolling = argsSec["rollingInterval"] ?? "Day";
        Enum.TryParse<RollingInterval>(rolling, true, out var rollingInterval);
        var flushStr = argsSec["flushToDiskInterval"] ?? "00:00:01";
        TimeSpan.TryParse(flushStr, out var flushInterval);
        loggerCfg.WriteTo.File(path, rollingInterval: rollingInterval, outputTemplate: template, flushToDiskInterval: flushInterval == default ? TimeSpan.FromSeconds(1) : flushInterval);
    }
    else
    {
        loggerCfg.WriteTo.File("logs/osdx-.log", rollingInterval: RollingInterval.Day);
    }
}
else
{
    // CLI 模式：保留 Console 與 File，但限制 Console 僅輸出 Warning 以上，以免 Info 干擾進度 UI
    loggerCfg.WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Warning);
    if (fileSection.Exists())
    {
        var argsSec = fileSection.GetSection("Args");
        var path = argsSec["path"] ?? "logs/osdx-.log";
        var template = argsSec["outputTemplate"] ?? "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";
        var rolling = argsSec["rollingInterval"] ?? "Day";
        Enum.TryParse<RollingInterval>(rolling, true, out var rollingInterval);
        var flushStr = argsSec["flushToDiskInterval"] ?? "00:00:01";
        TimeSpan.TryParse(flushStr, out var flushInterval);
        loggerCfg.WriteTo.File(path, rollingInterval: rollingInterval, outputTemplate: template, flushToDiskInterval: flushInterval == default ? TimeSpan.FromSeconds(1) : flushInterval);
    }
    else
    {
        loggerCfg.WriteTo.File("logs/osdx-.log", rollingInterval: RollingInterval.Day);
    }
}

Log.Logger = loggerCfg.CreateLogger();

// 定義命令列參數
var rootCommand = new RootCommand("OSDX (OpenSearch Data Xport) - 自動化資料匯出工具");

var exportCommand = new Command("export", "執行資料匯出任務 (自動化模式)");

var profileOption = new Option<string>(new[] { "--profile", "-p" }, "指定已存在的 Profile 名稱") { IsRequired = true };
var queryOption = new Option<string>(new[] { "--query", "-q" }, () => "Default", "指定 Profile 下的查詢語句名稱");
var usernameOption = new Option<string>(new[] { "--username", "-u" }, "覆蓋連線帳號");
var passwordOption = new Option<string>(new[] { "--password", "-pass" }, "連線密碼");
var outputOption = new Option<string>(new[] { "--output", "-o" }, "覆蓋輸出路徑");
var batchSizeOption = new Option<int?>(new[] { "--batch-size", "-b" }, "覆蓋批次抓取數量");
var fromOption = new Option<string>(new[] { "--from" }, "指定起始日期 (gte)，格式: yyyy-MM-dd 或 ISO 8601");
var toOption = new Option<string>(new[] { "--to" }, "指定結束日期 (lte)，格式: yyyy-MM-dd 或 ISO 8601");

exportCommand.AddOption(profileOption);
exportCommand.AddOption(queryOption);
exportCommand.AddOption(usernameOption);
exportCommand.AddOption(passwordOption);
exportCommand.AddOption(outputOption);
exportCommand.AddOption(batchSizeOption);
exportCommand.AddOption(fromOption);
exportCommand.AddOption(toOption);

exportCommand.SetHandler(async (string profileName, string queryName, string? username, string? password, string? output, int? batchSize, string? from, string? to) =>
{
    try 
    {
        Log.Information("啟動自動化匯出模式: Profile={Profile}, Query={Query}", profileName, queryName);
        AnsiConsole.Write(new FigletText("OSDX").Color(Color.Blue));
        AnsiConsole.MarkupLine($"[yellow]🚀 啟動自動化執行：Profile=[white]{profileName}[/], Query=[white]{queryName}[/][/]");

        // 1. 載入設定檔
        var config = ConfigService.LoadConfig();
        if (!config.Profiles.TryGetValue(profileName, out var profile))
        {
            var msg = $"找不到名為 '{profileName}' 的 Profile。請先使用 TUI 模式 (直接執行 osdx) 建立設定。";
            Log.Error(msg);
            AnsiConsole.MarkupLine($"[bold red]❌ 錯誤：{msg}[/]");
            Environment.Exit(1);
            return;
        }

        // 2. 尋找查詢語句
        if (!profile.Queries.TryGetValue(queryName, out var queryObj))
        {
            var msg = $"在 Profile '{profileName}' 中找不到查詢語句 '{queryName}'。";
            Log.Error(msg);
            AnsiConsole.MarkupLine($"[bold red]❌ 錯誤：{msg}[/]");
            Environment.Exit(1);
            return;
        }

        // 3. 參數覆蓋 (僅存在於記憶體，不寫回檔案)
        if (!string.IsNullOrEmpty(username)) profile.Connection.Username = username;
        if (!string.IsNullOrEmpty(output)) profile.Export.OutputPath = output;
        if (batchSize.HasValue) profile.Export.BatchSize = batchSize.Value;

        // 4. 動態日期範圍注入 (如果參數有給)
        object queryToUse = queryObj;
        if (!string.IsNullOrEmpty(from) || !string.IsNullOrEmpty(to))
        {
            AnsiConsole.MarkupLine("[cyan]ℹ 偵測到日期參數，正在準備動態注入 (含智慧補全時分秒)...[/]");
            
            string gteValue;
            try 
            {
                gteValue = string.IsNullOrEmpty(from) 
                    ? DateTimeOffset.UtcNow.AddDays(-1).ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-ddTHH:mm:ss.fffzzz")
                    : QueryHelper.ParseSmartDate(from, false);
            }
            catch (FormatException ex)
            {
                AnsiConsole.MarkupLine($"[bold red]❌ 錯誤：起始日期格式無效 '{from}' ({ex.Message})[/]");
                Environment.Exit(1);
                return;
            }

            string lteValue;
            try
            {
                lteValue = string.IsNullOrEmpty(to)
                    ? DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-ddTHH:mm:ss.fffzzz")
                    : QueryHelper.ParseSmartDate(to, true);
            }
            catch (FormatException ex)
            {
                AnsiConsole.MarkupLine($"[bold red]❌ 錯誤：結束日期格式無效 '{to}' ({ex.Message})[/]");
                Environment.Exit(1);
                return;
            }

            AnsiConsole.MarkupLine($"  起始: [white]{gteValue}[/]");
            AnsiConsole.MarkupLine($"  結束: [white]{lteValue}[/]");

            var queryJson = JsonSerializer.Serialize(queryObj);
            var modifiedJson = QueryHelper.ReplaceTimestampRange(queryJson, gteValue, lteValue);
            queryToUse = JsonSerializer.Deserialize<JsonElement>(modifiedJson);
            Log.Information("已完成動態日期注入 (智慧解析): From={From}, To={To}", gteValue, lteValue);
        }

        // 5. 密碼處理 (優先使用指令參數)
        string? finalPassword = password;
        if (string.IsNullOrEmpty(finalPassword))
        {
            // 如果指令沒給，嘗試環境變數
            finalPassword = Environment.GetEnvironmentVariable("OSDX_PASSWORD");
        }

        if (string.IsNullOrEmpty(finalPassword) && !string.IsNullOrEmpty(profile.Connection.Username))
        {
            // 如果還是沒有，但在自動化模式下必須有密碼
            Log.Warning("自動化模式未提供密碼，嘗試提示輸入...");
            finalPassword = AnsiConsole.Prompt(new TextPrompt<string>("請輸入 OpenSearch 密碼：").PromptStyle("red").Secret());
        }

        // 6. 執行導出
        await DataStreamer.ExportAsync(profile.Connection, profile.Export, queryToUse, finalPassword);
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "自動化執行過程中發生嚴重錯誤");
        AnsiConsole.WriteException(ex);
        Environment.Exit(3);
    }
}, profileOption, queryOption, usernameOption, passwordOption, outputOption, batchSizeOption, fromOption, toOption);

rootCommand.AddCommand(exportCommand);

// 判斷是否進入 TUI 模式
if (args.Length == 0)
{
    try 
    {
        Log.Information("OSDX 啟動 (引導模式)");
        await InteractiveWizard.RunAsync();
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "引導模式因未預期的錯誤而終止");
        AnsiConsole.WriteException(ex);
    }
    finally
    {
        Log.Information("OSDX 程式結束");
        Log.CloseAndFlush();
    }
}
else
{
    // 執行命令列解析 (自動化模式)
    return await rootCommand.InvokeAsync(args);
}

return 0;
