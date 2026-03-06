using System.CommandLine;
using Spectre.Console;
using Serilog;
using osdx.Models;
using osdx.UI;
using osdx.Core;
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

// 定義命令列參數
var rootCommand = new RootCommand("OSDX (OpenSearch Data Xport) - 自動化資料匯出工具");

var exportCommand = new Command("export", "執行資料匯出任務 (自動化模式)");

var profileOption = new Option<string>(new[] { "--profile", "-p" }, "指定已存在的 Profile 名稱") { IsRequired = true };
var queryOption = new Option<string>(new[] { "--query", "-q" }, () => "Default", "指定 Profile 下的查詢語句名稱");
var usernameOption = new Option<string>(new[] { "--username", "-u" }, "覆蓋連線帳號");
var passwordOption = new Option<string>(new[] { "--password", "-pass" }, "連線密碼");
var outputOption = new Option<string>(new[] { "--output", "-o" }, "覆蓋輸出路徑");
var batchSizeOption = new Option<int?>(new[] { "--batch-size", "-b" }, "覆蓋批次抓取數量");

exportCommand.AddOption(profileOption);
exportCommand.AddOption(queryOption);
exportCommand.AddOption(usernameOption);
exportCommand.AddOption(passwordOption);
exportCommand.AddOption(outputOption);
exportCommand.AddOption(batchSizeOption);

exportCommand.SetHandler(async (string profileName, string queryName, string? username, string? password, string? output, int? batchSize) =>
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

        // 4. 密碼處理 (優先使用指令參數)
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

        // 5. 執行導出
        await DataStreamer.ExportAsync(profile.Connection, profile.Export, queryObj, finalPassword);
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "自動化執行過程中發生嚴重錯誤");
        AnsiConsole.WriteException(ex);
        Environment.Exit(3);
    }
}, profileOption, queryOption, usernameOption, passwordOption, outputOption, batchSizeOption);

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
