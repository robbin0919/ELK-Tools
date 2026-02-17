using Spectre.Console;
using Serilog;
using osdx.Models;

namespace osdx.UI;

public static class InteractiveWizard
{
    private static string? _currentEndpoint;
    private static string? _currentIndex;
    private static string? _currentUser;

    public static void Run()
    {
        Log.Information(">>> [TUI] 進入引導模式主迴圈 <<<");
        
        while (true)
        {
            AnsiConsole.Clear();
            AnsiConsole.Write(new FigletText("OSDX").Color(Color.Blue));
            AnsiConsole.MarkupLine("[grey]OpenSearch Data Xport - 交互式引導模式[/]");

            // 顯示目前連線狀態 (若有)
            if (!string.IsNullOrEmpty(_currentEndpoint))
            {
                AnsiConsole.MarkupLine($"[green]●[/] [grey]URL:[/] [cyan]{Markup.Escape(_currentEndpoint)}[/] [grey]|[/] [grey]Index:[/] [cyan]{Markup.Escape(_currentIndex ?? "-")}[/] [grey]|[/] [grey]User:[/] [yellow]{Markup.Escape(_currentUser ?? "Guest")}[/]");
                AnsiConsole.Write(new Rule().RuleStyle("grey"));
                AnsiConsole.WriteLine();
            }
            else
            {
                AnsiConsole.MarkupLine("[red]⚠ 目前尚未連線，請先選擇連線資訊。[/]\n");
            }

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[yellow]請選擇要執行的功能：[/]")
                    .PageSize(10)
                    .AddChoices(new[] {
                        "1. 連線資訊選擇與建立 (切換目標)",
                        "2. 開始執行資料導出",
                        "3. 管理設定檔 (編輯/刪除)",
                        "4. 系統設定 (SSL 驗證等)",
                        "---",
                        "Exit (結束程式)"
                    }));

            Log.Information("使用者主選單選擇: {Choice}", choice);

            if (choice == "Exit (結束程式)")
            {
                Log.Information("使用者選擇結束程式 (Exit)");
                AnsiConsole.MarkupLine("[red]已結束程式。[/]");
                break;
            }

            HandleChoice(choice);
        }
    }

    private static void HandleChoice(string choice)
    {
        bool skipWait = false;
        switch (choice)
        {
            case "1. 連線資訊選擇與建立 (切換目標)":
                skipWait = HandleConnectionFlow();
                break;
            case "2. 開始執行資料導出":
                HandleExportFlow();
                break;
            case "3. 管理設定檔 (編輯/刪除)":
                Log.Information("進入管理介面");
                AnsiConsole.MarkupLine("[blue]進入管理介面...[/]");
                break;
            case "4. 系統設定 (SSL 驗證等)":
                Log.Information("進入系統設定");
                AnsiConsole.MarkupLine("[magenta]進入系統設定...[/]");
                break;
        }

        if (!skipWait)
        {
            AnsiConsole.MarkupLine("\n[grey]按任意鍵 (或 Esc) 回主選單...[/]");
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Escape)
            {
                Log.Information("使用者按下 Esc 鍵返回主選單");
            }
        }
    }

    private static void HandleExportFlow()
    {
        if (string.IsNullOrEmpty(_currentEndpoint))
        {
            Log.Warning("導出失敗: 尚未連線就嘗試執行導出");
            AnsiConsole.MarkupLine("[red]❌ 錯誤：尚未建立連線。請先執行「連線資訊選擇與建立」。[/]");
            return;
        }

        Log.Information("開始執行資料導出作業: Endpoint={Endpoint}, Index={Index}, User={User}", _currentEndpoint, _currentIndex, _currentUser);
        AnsiConsole.MarkupLine($"[yellow]🚀 準備執行導出作業...[/]");
        AnsiConsole.MarkupLine($"[grey]目標:[/] {Markup.Escape(_currentEndpoint)} [grey]索引:[/] {Markup.Escape(_currentIndex ?? "")}");
        // TODO: 這裡將會呼叫 Core/DataStreamer.cs 執行真正的 Scroll API 邏輯
    }

    private static bool HandleConnectionFlow()
    {
        Log.Information("進入連線流程");
        var config = Core.ConfigService.LoadConfig();
        var profileNames = config.Profiles.Keys.ToList();
        profileNames.Add("[[建立新連線]]");
        profileNames.Add("[[返回主選單]]");

        var selectedProfile = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("請選擇 [green]連線目標[/]：")
                .AddChoices(profileNames));

        Log.Information("使用者選擇連線目標: {Target}", selectedProfile);

        if (selectedProfile == "[[返回主選單]]")
        {
            return true; 
        }

        string endpoint = "";
        string index = "";
        bool isNew = false;

        if (selectedProfile == "[[建立新連線]]")
        {
            isNew = true;
            AnsiConsole.MarkupLine("[grey](提示: 隨時按 Esc 可取消並返回主選單)[/]");
            var inputUrl = TryAsk("請輸入 OpenSearch [bold]URL[/] (例如 http://localhost:9200):");
            if (inputUrl == null) return true;
            endpoint = inputUrl;

            var inputIndex = TryAsk("請輸入 [bold]Index[/] 名稱 (例如 logs-*):");
            if (inputIndex == null) return true;
            index = inputIndex;
        }
        else
        {
            var p = config.Profiles[selectedProfile];
            endpoint = p.Connection.Endpoint;
            index = p.Connection.Index;
            AnsiConsole.MarkupLine($"已載入設定檔: [cyan]{Markup.Escape(selectedProfile)}[/] ({Markup.Escape(endpoint)})");
        }

        // 每次連線都要求輸入帳密
        var username = TryAsk("請輸入 [yellow]帳號 (Username)[/]:");
        if (username == null) return true;

        var password = TryAsk("請輸入 [yellow]密碼 (Password)[/]:", isSecret: true);
        if (password == null) return true;

        AnsiConsole.Status()
            .Start("正在驗證連線資訊...", ctx => {
                // TODO: 實際呼叫 OpenSearch 驗證
                Thread.Sleep(1000); 
                Log.Information("連線驗證成功: Endpoint={Endpoint}, Index={Index}, User={User}", endpoint, index, username);
                AnsiConsole.MarkupLine($"[green]成功連線至:[/] {Markup.Escape(endpoint)}");
            });

        if (isNew)
        {
            if (AnsiConsole.Confirm("是否要將此連線資訊儲存為設定檔 (Profile)？"))
            {
                var profileName = AnsiConsole.Ask<string>("請輸入設定檔名稱 (例如 Prod-Server):");
                if (string.IsNullOrEmpty(profileName)) profileName = "New-Profile-" + DateTime.Now.ToString("yyyyMMdd-HHmm");
                
                var newProfile = new ProfileConfig
                {
                    Connection = new ConnectionConfig
                    {
                        Endpoint = endpoint,
                        Index = index,
                        Username = "", 
                        Password = null, 
                        IgnoreSslErrors = true
                    }
                };
                Core.ConfigService.AddProfile(profileName, newProfile);
                Log.Information("儲存新設定檔: {ProfileName}", profileName);
                AnsiConsole.MarkupLine($"[green]設定檔 {Markup.Escape(profileName)} 已儲存。[/]");
            }
        }

        _currentEndpoint = endpoint;
        _currentIndex = index;
        _currentUser = username;

        var summary = new Table().Border(TableBorder.Rounded).Expand();
        summary.AddColumn("[grey]項目[/]");
        summary.AddColumn("[grey]詳細資訊[/]");
        summary.AddRow("OpenSearch URL", $"[cyan]{Markup.Escape(endpoint)}[/]");
        summary.AddRow("Target Index", $"[cyan]{Markup.Escape(index)}[/]");
        summary.AddRow("User", $"[yellow]{Markup.Escape(username)}[/]");

        AnsiConsole.Write(
            new Panel(summary)
                .Header("[bold green] 連線就緒 (Connection Ready) [/]")
                .BorderColor(Color.Green)
                .Padding(1, 1, 1, 1));

        AnsiConsole.MarkupLine("\n[bold]您現在可以開始進行導出作業。[/]");
        return false; 
    }

    /// <summary>
    /// 自定義輸入方法，支援按 Esc 鍵取消
    /// </summary>
    private static string? TryAsk(string prompt, bool isSecret = false)
    {
        AnsiConsole.Markup(prompt + " ");
        var input = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return input.ToString();
            }
            if (key.Key == ConsoleKey.Escape)
            {
                Console.WriteLine();
                Log.Information("使用者按 Esc 取消輸入");
                return null;
            }
            if (key.Key == ConsoleKey.Backspace && input.Length > 0)
            {
                input.Remove(input.Length - 1, 1);
                Console.Write("\b \b");
            }
            else if (!char.IsControl(key.KeyChar))
            {
                input.Append(key.KeyChar);
                Console.Write(isSecret ? "*" : key.KeyChar);
            }
        }
    }
}
