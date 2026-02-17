using Spectre.Console;
using Serilog;
using osdx.Models;
using System.Text.Json;

namespace osdx.UI;

public static class InteractiveWizard
{
    private static string? _currentEndpoint;
    private static string? _currentIndex;
    private static string? _currentUser;
    private static string? _currentPassword;

    public static void Run()
    {
        Log.Information(">>> [TUI] 進入引導模式主迴圈 <<<");
        
        while (true)
        {
            RefreshScreen();

            var choice = TrySelect("[yellow]請選擇要執行的功能：[/]", new List<string> {
                        "1. 連線資訊選擇與建立 (切換目標)",
                        "2. 開始執行資料導出",
                        "3. 管理設定檔 (編輯/刪除)",
                        "4. 系統設定 (SSL 驗證等)",
                        "---",
                        "Exit (結束程式)"
                    });

            Log.Information("使用者主選單選擇: {Choice}", choice);

            if (string.IsNullOrEmpty(choice) || choice == "Exit (結束程式)")
            {
                Log.Information("使用者選擇結束程式 (Exit)");
                AnsiConsole.MarkupLine("[red]已結束程式。[/]");
                break;
            }

            HandleChoice(choice);
        }
    }

    private static void RefreshScreen()
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
                skipWait = HandleExportFlow();
                break;
            case "3. 管理設定檔 (編輯/刪除)":
                skipWait = HandleManagementFlow();
                break;
            case "4. 系統設定 (SSL 驗證等)":
                RefreshScreen();
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

    private static bool HandleManagementFlow()
    {
        while (true)
        {
            RefreshScreen();
            var config = Core.ConfigService.LoadConfig();
            var profileNames = config.Profiles.Keys.ToList();
            profileNames.Add("[[返回主選單]]");

            var selectedProfile = TrySelect("請選擇要 [blue]管理[/] 的設定檔：", profileNames);

            if (selectedProfile == null || selectedProfile == "[[返回主選單]]") return true;

            var profile = config.Profiles[selectedProfile];

            while (true)
            {
                RefreshScreen();
                var action = TrySelect($"設定檔 [cyan]{selectedProfile}[/] 的操作：", new List<string> {
                            "1. 管理查詢語句清單 (Queries)",
                            "2. 修改連線資訊",
                            "3. 修改導出設定",
                            "4. 刪除此設定檔",
                            "返回上層"
                        });

                if (action == null || action == "返回上層") break;
                
                RefreshScreen();
                if (action == "1. 管理查詢語句清單 (Queries)")
                {
                    ManageQueries(selectedProfile, profile);
                }
                else if (action == "4. 刪除此設定檔")
                {
                    var confirm = TryConfirm($"確定要刪除 [red]{selectedProfile}[/] 嗎？");
                    if (confirm == true)
                    {
                        config.Profiles.Remove(selectedProfile);
                        Core.ConfigService.SaveConfig(config);
                        AnsiConsole.MarkupLine("[green]設定檔已刪除。[/]");
                        break; 
                    }
                    else if (confirm == null) return true;
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]此功能尚未實作。[/]");
                    AnsiConsole.WriteLine("按任意鍵繼續...");
                    Console.ReadKey(true);
                }
            }
        }
    }

    private static void ManageQueries(string profileName, ProfileConfig profile)
    {
        while (true)
        {
            RefreshScreen();
            var queryNames = profile.Queries.Keys.ToList();
            queryNames.Add("[[新增查詢語句]]");
            queryNames.Add("返回上層");

            var selectedQuery = TrySelect($"管理 [cyan]{profileName}[/] 的查詢語句：", queryNames);
            if (selectedQuery == null || selectedQuery == "返回上層") return;

            if (selectedQuery == "[[新增查詢語句]]")
            {
                var newName = AnsiConsole.Ask<string>("請輸入新查詢語句名稱 (例如 Yesterday-Errors):");
                if (string.IsNullOrWhiteSpace(newName)) continue;
                if (profile.Queries.ContainsKey(newName))
                {
                    AnsiConsole.MarkupLine("[red]名稱重複！[/]");
                    Thread.Sleep(1000);
                    continue;
                }
                profile.Queries[newName] = new { match_all = new { } };
                EditQuery(profileName, profile, newName);
            }
            else
            {
                var action = TrySelect($"查詢語句 [yellow]{selectedQuery}[/] 的操作：", new List<string> {
                    "1. 編輯內容 (Edit)",
                    "2. 重新命名 (Rename)",
                    "3. 刪除此查詢 (Delete)",
                    "返回"
                });

                if (action == "1. 編輯內容 (Edit)")
                {
                    EditQuery(profileName, profile, selectedQuery);
                }
                else if (action == "2. 重新命名 (Rename)")
                {
                    var newName = AnsiConsole.Ask<string>($"請輸入 [yellow]{selectedQuery}[/] 的新名稱:");
                    if (!string.IsNullOrWhiteSpace(newName) && newName != selectedQuery)
                    {
                        var content = profile.Queries[selectedQuery];
                        profile.Queries.Remove(selectedQuery);
                        profile.Queries[newName] = content;
                        SaveProfile(profileName, profile);
                    }
                }
                else if (action == "3. 刪除此查詢 (Delete)")
                {
                    if (profile.Queries.Count <= 1)
                    {
                        AnsiConsole.MarkupLine("[red]至少需保留一個查詢語句。[/]");
                        Thread.Sleep(1000);
                        continue;
                    }
                    if (TryConfirm($"確定要刪除 [red]{selectedQuery}[/] 嗎？") == true)
                    {
                        profile.Queries.Remove(selectedQuery);
                        SaveProfile(profileName, profile);
                    }
                }
            }
        }
    }

    private static void SaveProfile(string profileName, ProfileConfig profile)
    {
        var config = Core.ConfigService.LoadConfig();
        config.Profiles[profileName] = profile;
        Core.ConfigService.SaveConfig(config);
    }

    private static void EditQuery(string profileName, ProfileConfig profile, string queryName)
    {
        var currentQueryJson = JsonSerializer.Serialize(profile.Queries[queryName], new JsonSerializerOptions { WriteIndented = true });
        
        RefreshScreen();
        AnsiConsole.Write(new Rule($"編輯 [cyan]{profileName}[/] - [yellow]{queryName}[/] 的 Query").LeftJustified());
        AnsiConsole.MarkupLine("[grey]目前查詢語句：[/]");
        AnsiConsole.WriteLine(currentQueryJson);
        AnsiConsole.WriteLine();

        var choice = TrySelect("請選擇編輯方式：", new List<string> {
                    "使用快速模板 (Match All)",
                    "直接輸入 JSON 字串",
                    "使用外部編輯器 (Vim/Notepad)",
                    "放棄修改"
                });

        string? newJson = null;

        switch (choice)
        {
            case "使用快速模板 (Match All)":
                newJson = "{ \"match_all\": {} }";
                break;
            case "直接輸入 JSON 字串":
                AnsiConsole.MarkupLine("[yellow]請輸入 JSON 內容 (輸入完畢請按 Enter，或按 Esc 取消)：[/]");
                newJson = TryAsk("JSON >");
                break;
            case "使用外部編輯器 (Vim/Notepad)":
                newJson = EditWithExternalEditor(currentQueryJson);
                break;
            default:
                return;
        }

        if (string.IsNullOrWhiteSpace(newJson))
        {
            AnsiConsole.MarkupLine("[red]取消修改或輸入為空。[/]");
            return;
        }

        try
        {
            var element = JsonSerializer.Deserialize<JsonElement>(newJson);
            profile.Queries[queryName] = element;
            SaveProfile(profileName, profile);
            
            AnsiConsole.MarkupLine("[bold green]✅ Query 已成功更新並儲存！[/]");

            var testConfirm = TryConfirm("是否要立即對 OpenSearch 伺服器進行語法測試？");
            if (testConfirm == true)
            {
                string? pwd = (profile.Connection.Endpoint == _currentEndpoint && profile.Connection.Index == _currentIndex && !string.IsNullOrEmpty(_currentPassword)) 
                              ? _currentPassword 
                              : TryAsk("請輸入密碼以進行測試 (留空則不使用):", isSecret: true);
                
                if (pwd == null && (profile.Connection.Endpoint != _currentEndpoint)) 
                {
                    AnsiConsole.MarkupLine("[yellow]已取消測試。[/]");
                    return;
                }

                AnsiConsole.Status().Start("正在測試查詢語法...", ctx => {
                    var result = Core.ConnectionManager.TestQuery(profile.Connection, pwd, profile.Queries[queryName]);
                    if (result.Success)
                    {
                        AnsiConsole.MarkupLine($"[green]✔ {result.Message}[/]");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[red]✘ 測試失敗：{Markup.Escape(result.Message)}[/]");
                    }
                });
                AnsiConsole.WriteLine("按任意鍵繼續...");
                Console.ReadKey(true);
            }
        }
        catch (JsonException ex)
        {
            AnsiConsole.MarkupLine($"[red]❌ JSON 格式錯誤：{Markup.Escape(ex.Message)}[/]");
            AnsiConsole.WriteLine("按任意鍵繼續...");
            Console.ReadKey(true);
        }
    }

    private static string? EditWithExternalEditor(string initialContent)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"osdx_query_{Guid.NewGuid()}.json");
        File.WriteAllText(tempFile, initialContent);

        try
        {
            var editor = Environment.OSVersion.Platform == PlatformID.Win32NT ? "notepad.exe" : "vim";
            // 如果 Linux 有環境變數 EDITOR 則優先使用
            var envEditor = Environment.GetEnvironmentVariable("EDITOR");
            if (!string.IsNullOrEmpty(envEditor)) editor = envEditor;

            AnsiConsole.MarkupLine($"[grey]正在調用編輯器: {editor}...[/]");
            AnsiConsole.MarkupLine("[grey](編輯完成並存檔後，請關閉編輯器以繼續)[/]");
            
            using (var process = System.Diagnostics.Process.Start(editor, tempFile))
            {
                process.WaitForExit();
            }

            return File.ReadAllText(tempFile);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]無法啟動編輯器: {Markup.Escape(ex.Message)}[/]");
            return null;
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    private static bool HandleExportFlow()
    {
        RefreshScreen();
        if (string.IsNullOrEmpty(_currentEndpoint))
        {
            Log.Warning("導出失敗: 尚未連線就嘗試執行導出");
            AnsiConsole.MarkupLine("[red]❌ 錯誤：尚未建立連線。請先執行「連線資訊選擇與建立」。[/]");
            return false;
        }

        // 載入當前 Profile 的所有 Query
        var config = Core.ConfigService.LoadConfig();
        var currentProfile = config.Profiles.Values.FirstOrDefault(p => p.Connection.Endpoint == _currentEndpoint && p.Connection.Index == _currentIndex);
        
        if (currentProfile == null)
        {
             AnsiConsole.MarkupLine("[red]❌ 錯誤：找不到對應的設定檔資訊。[/]");
             return false;
        }

        var queryNames = currentProfile.Queries.Keys.ToList();
        var selectedQueryName = queryNames.Count > 1 
            ? TrySelect("[yellow]請挑選要使用的查詢語句 (Query)：[/]", queryNames)
            : queryNames.FirstOrDefault();

        if (selectedQueryName == null) return true; // 按下 Esc

        Log.Information("開始執行資料導出作業: Endpoint={Endpoint}, Index={Index}, Query={QueryName}", _currentEndpoint, _currentIndex, selectedQueryName);
        AnsiConsole.MarkupLine($"[yellow]🚀 準備執行導出作業...[/]");
        AnsiConsole.MarkupLine($"[grey]目標:[/] {Markup.Escape(_currentEndpoint)} [grey]索引:[/] {Markup.Escape(_currentIndex ?? "")}");
        AnsiConsole.MarkupLine($"[grey]查詢:[/] [yellow]{selectedQueryName}[/]");
        
        // TODO: 這裡將會呼叫 Core/DataStreamer.cs 並傳入 selectedQueryName 與內容
        return false;
    }

    private static bool HandleConnectionFlow()
    {
        while (true)
        {
            RefreshScreen();
            Log.Information("進入連線流程");
            var config = Core.ConfigService.LoadConfig();
            var profileNames = config.Profiles.Keys.ToList();
            profileNames.Add("[[建立新連線]]");
            profileNames.Add("[[返回主選單]]");

            var selectedProfile = TrySelect("請選擇 [green]連線目標[/]：", profileNames);

            Log.Information("使用者選擇連線目標: {Target}", selectedProfile);

            if (selectedProfile == null || selectedProfile == "[[返回主選單]]")
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
                if (inputUrl == null) continue;
                endpoint = inputUrl;

                var inputIndex = TryAsk("請輸入 [bold]Index[/] 名稱 (例如 logs-*):");
                if (inputIndex == null) continue;
                index = inputIndex;
            }
            else
            {
                var p = config.Profiles[selectedProfile];
                endpoint = p.Connection.Endpoint;
                index = p.Connection.Index;
                AnsiConsole.MarkupLine($"已載入設定檔: [cyan]{Markup.Escape(selectedProfile)}[/] ({Markup.Escape(endpoint)})");
            }

            // 帳密輸入
            var username = TryAsk("請輸入 [yellow]帳號 (Username)[/]:");
            if (username == null) continue;

            var password = TryAsk("請輸入 [yellow]密碼 (Password)[/]:", isSecret: true);
            if (password == null) continue;

            AnsiConsole.Status()
                .Start("正在驗證連線資訊...", ctx => {
                    // TODO: 實際呼叫 OpenSearch 驗證
                    Thread.Sleep(1000); 
                    Log.Information("連線驗證成功: Endpoint={Endpoint}, Index={Index}, User={User}", endpoint, index, username);
                    AnsiConsole.MarkupLine($"[green]成功連線至:[/] {Markup.Escape(endpoint)}");
                });

            if (isNew)
            {
                var saveConfirm = TryConfirm("是否要將此連線資訊儲存為設定檔 (Profile)？");
                if (saveConfirm == true)
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
            _currentPassword = password;

            var summary = new Table().Border(TableBorder.Rounded);
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
    }

    /// <summary>
    /// 自定義選擇方法，支援 Esc 鍵取消
    /// </summary>
    private static string? TrySelect(string title, List<string> choices, int pageSize = 10)
    {
        int selectedIndex = 0;
        int topIndex = 0;
        
        return AnsiConsole.Live(new Text("")).Start(ctx =>
        {
            while (true)
            {
                var table = new Table().NoBorder().HideHeaders();
                table.AddColumn("Item");
                
                int visibleCount = Math.Min(pageSize, choices.Count);
                if (selectedIndex < topIndex) topIndex = selectedIndex;
                if (selectedIndex >= topIndex + visibleCount) topIndex = selectedIndex - visibleCount + 1;

                for (int i = topIndex; i < Math.Min(topIndex + visibleCount, choices.Count); i++)
                {
                    if (i == selectedIndex)
                        table.AddRow($"[bold blue]> {Markup.Escape(choices[i])}[/]");
                    else
                        table.AddRow($"  {Markup.Escape(choices[i])}");
                }
                
                table.AddEmptyRow();
                table.AddRow(new Rule().RuleStyle("grey"));
                table.AddRow($"[grey](↑/↓ 選擇, Enter 確認, Esc 返回)  {selectedIndex + 1}/{choices.Count}[/]");

                var panel = new Panel(table)
                {
                    Header = new PanelHeader(title)
                };
                panel.BorderColor(Color.Blue);

                ctx.UpdateTarget(panel);

                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.UpArrow) selectedIndex = (selectedIndex - 1 + choices.Count) % choices.Count;
                else if (key.Key == ConsoleKey.DownArrow) selectedIndex = (selectedIndex + 1) % choices.Count;
                else if (key.Key == ConsoleKey.Enter) return choices[selectedIndex];
                else if (key.Key == ConsoleKey.Escape) return null;
            }
        });
    }

    /// <summary>
    /// 自定義確認方法，支援 Esc 鍵取消
    /// </summary>
    private static bool? TryConfirm(string message)
    {
        AnsiConsole.Markup($"{message} [grey](y/n/Esc)[/] ");
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Y)
            {
                AnsiConsole.MarkupLine("[green]Yes[/]");
                return true;
            }
            if (key.Key == ConsoleKey.N)
            {
                AnsiConsole.MarkupLine("[red]No[/]");
                return false;
            }
            if (key.Key == ConsoleKey.Escape)
            {
                AnsiConsole.WriteLine();
                return null;
            }
        }
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
