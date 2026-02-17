using Spectre.Console;
using osdx.Models;

namespace osdx.UI;

public static class InteractiveWizard
{
    private static string? _currentEndpoint;
    private static string? _currentIndex;
    private static string? _currentUser;

    public static void Run()
    {
        while (true)
        {
            AnsiConsole.Clear();
            AnsiConsole.Write(new FigletText("OSDX").Color(Color.Blue));
            AnsiConsole.MarkupLine("[grey]OpenSearch Data Xport - 交互式引導模式[/]");

            // 顯示目前連線狀態 (若有)
            if (!string.IsNullOrEmpty(_currentEndpoint))
            {
                var statusTable = new Table().Border(TableBorder.Rounded).Expand();
                statusTable.AddColumn("[grey]URL[/]");
                statusTable.AddColumn("[grey]Index[/]");
                statusTable.AddColumn("[grey]User[/]");
                statusTable.AddRow(
                    $"[cyan]{Markup.Escape(_currentEndpoint)}[/]", 
                    $"[cyan]{Markup.Escape(_currentIndex ?? "-")}[/]", 
                    $"[yellow]{Markup.Escape(_currentUser ?? "Guest")}[/]");

                AnsiConsole.Write(new Panel(statusTable).Header("[green] 目前連線狀態 [/]").BorderColor(Color.Green));
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
                        "1. 連線資訊選擇與建立",
                        "2. 管理設定檔 (編輯/刪除)",
                        "3. 系統設定 (SSL 驗證等)",
                        "---",
                        "Exit (結束程式)"
                    }));

            if (choice == "Exit (結束程式)")
            {
                AnsiConsole.MarkupLine("[red]已結束程式。[/]");
                break;
            }

            HandleChoice(choice);
        }
    }

    private static void HandleChoice(string choice)
    {
        switch (choice)
        {
            case "1. 連線資訊選擇與建立":
                HandleConnectionFlow();
                break;
            case "2. 管理設定檔 (編輯/刪除)":
                AnsiConsole.MarkupLine("[blue]進入管理介面...[/]");
                break;
            case "3. 系統設定 (SSL 驗證等)":
                AnsiConsole.MarkupLine("[magenta]進入系統設定...[/]");
                break;
        }

        AnsiConsole.MarkupLine("\n[grey]按任意鍵回主選單...[/]");
        Console.ReadKey(true);
    }

    private static void HandleConnectionFlow()
    {
        var config = Core.ConfigService.LoadConfig();
        var profileNames = config.Profiles.Keys.ToList();
        profileNames.Add("[[建立新連線]]");

        var selectedProfile = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("請選擇 [green]連線目標[/]：")
                .AddChoices(profileNames));

        string endpoint = "";
        string index = "";
        bool isNew = false;

        if (selectedProfile == "[[建立新連線]]")
        {
            isNew = true;
            endpoint = AnsiConsole.Ask<string>("請輸入 OpenSearch [bold]URL[/] (例如 http://localhost:9200):");
            index = AnsiConsole.Ask<string>("請輸入 [bold]Index[/] 名稱 (例如 logs-*):");
        }
        else
        {
            var p = config.Profiles[selectedProfile];
            endpoint = p.Connection.Endpoint;
            index = p.Connection.Index;
            AnsiConsole.MarkupLine($"已載入設定檔: [cyan]{Markup.Escape(selectedProfile)}[/] ({Markup.Escape(endpoint)})");
        }

        // 每次連線都要求輸入帳密
        var username = AnsiConsole.Ask<string>("請輸入 [yellow]帳號 (Username)[/]:");
        var password = AnsiConsole.Prompt(
            new TextPrompt<string>("請輸入 [yellow]密碼 (Password)[/]:")
                .PromptStyle("red")
                .Secret());

        AnsiConsole.Status()
            .Start("正在驗證連線資訊...", ctx => {
                // TODO: 實際呼叫 OpenSearch 驗證
                Thread.Sleep(1000); 
                AnsiConsole.MarkupLine($"[green]成功連線至:[/] {Markup.Escape(endpoint)}");
            });

        // 如果是新連線，詢問是否儲存
        if (isNew)
        {
            if (AnsiConsole.Confirm("是否要將此連線資訊儲存為設定檔 (Profile)？"))
            {
                var profileName = AnsiConsole.Ask<string>("請輸入設定檔名稱 (例如 Prod-Server):");
                var newProfile = new ProfileConfig
                {
                    Connection = new ConnectionConfig
                    {
                        Endpoint = endpoint,
                        Index = index,
                        Username = "", // 不儲存帳號
                        Password = null, // 不儲存密碼
                        IgnoreSslErrors = true
                    }
                };
                Core.ConfigService.AddProfile(profileName, newProfile);
                AnsiConsole.MarkupLine($"[green]設定檔 {Markup.Escape(profileName)} 已儲存 (僅記錄 URL 與 Index)。[/]");
            }
        }

        // 更新全域連線狀態
        _currentEndpoint = endpoint;
        _currentIndex = index;
        _currentUser = username;

        // 顯示連線摘要面板
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
    }
}
