/*
 * 檔案名稱: InteractiveWizard.cs
 * 專案: OSDX (OpenSearch Data Xport)
 * 
 * 修改歷程:
 * ────────────────────────────────────────────────────────────────
 * 日期         版本    修改人員        修改說明
 * ────────────────────────────────────────────────────────────────
 * 2026-03-01   v1.4.3  Robbin Lee      1. 新增日期輸入驗證與重試機制
 *                                       2. 格式錯誤時要求重新輸入而非直接使用預設值
 *                                       3. 新增結束日期不得早於起始日期的驗證
 *                                       4. 改善使用者體驗，提供明確的錯誤提示
 *                                       5. 修正 CS0103 編譯錯誤（使用 usedEscape 變數）
 *                                       6. 修正 CS0165 編譯錯誤（變數初始化）
 * 2026-03-01   v1.4.2  Robbin Lee      1. 修正按 Esc 或選擇 No 時的邏輯錯誤
 *                                       2. 現在無論選擇是否均使用 24 小時預設值
 *                                       3. Yes 可自訂日期，No/Esc 直接使用24小時
 * 2026-03-01   v1.4.1  Robbin Lee      1. 修改日期範圍預設值從 15 天改為 24 小時
 *                                       2. 更新所有相關提示文字和說明
 * 2026-02-28   v1.4    Robbin Lee      1. 新增日期範圍自訂功能
 *                                       2. 執行導出前可動態設定起始/結束日期
 *                                       3. 自動偵測查詢中的 @timestamp range
 *                                       4. 支援預設值（最近 15 天）
 * 2026-02-28   v1.2    Robbin Lee      1. 修正長查詢顯示問題
 *                                       2. 限制查詢預覽最多顯示 15 行
 *                                       3. 超過時顯示省略提示，避免選項被推出螢幕外
 * 2026-02-28   v1.1    Robbin Lee      1. 增強帳號輸入驗證邏輯
 *                                       2. 自動偵測 Windows 域名格式（domain\user）
 *                                       3. 提供互動式域名前綴移除功能
 *                                       4. 新增使用者友善的格式提示訊息
 *                                       5. 加強帳號必填校驗
 * ────────────────────────────────────────────────────────────────
 */

using Spectre.Console;
using Serilog;
using osdx.Models;
using System.Text.Json;
using Spectre.Console.Rendering;

namespace osdx.UI;

public static class InteractiveWizard
{
    private static string? _currentEndpoint;
    private static string? _currentIndex;
    private static string? _currentUser;
    private static string? _currentPassword;

    public static async Task RunAsync()
    {
        Log.Information(">>> [TUI] 進入引導模式主迴圈 <<<");
        
        while (true)
        {
            RefreshScreen();

            var choice = TrySelect("[yellow]請選擇要執行的功能：[/]", new List<string> {
                        "1. 連線資訊選擇 (切換目標)",
                        "2. 開始執行資料導出",
                        "3. 管理設定檔 (編輯/建立/刪除)",
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

            await HandleChoiceAsync(choice);
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
            AnsiConsole.MarkupLine($"[green]●[/] [grey]URL:[/] [cyan]{Markup.Escape(_currentEndpoint ?? "")}[/] [grey]|[/] [grey]Index:[/] [cyan]{Markup.Escape(_currentIndex ?? "-")}[/] [grey]|[/] [grey]User:[/] [yellow]{Markup.Escape(_currentUser ?? "Guest")}[/]");
            AnsiConsole.Write(new Rule().RuleStyle("grey"));
            AnsiConsole.WriteLine();
        }
        else
        {
            AnsiConsole.MarkupLine("[red]⚠ 目前尚未連線，請先選擇連線資訊。[/]\n");
        }
    }

    private static async Task HandleChoiceAsync(string choice)
    {
        bool skipWait = false;
        switch (choice)
        {
            case "1. 連線資訊選擇 (切換目標)":
                skipWait = await HandleConnectionFlowAsync();
                break;
            case "2. 開始執行資料導出":
                skipWait = await HandleExportFlowAsync();
                break;
            case "3. 管理設定檔 (編輯/建立/刪除)":
                skipWait = HandleManagementFlow();
                break;
            case "4. 系統設定 (SSL 驗證等)":
                skipWait = HandleSettingsFlow();
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

    private static bool HandleSettingsFlow()
    {
        while (true)
        {
            RefreshScreen();
            var config = Core.ConfigService.LoadConfig();
            
            var settingsTable = new Table().Border(TableBorder.Rounded);
            settingsTable.AddColumn("項目");
            settingsTable.AddColumn("目前值");
            settingsTable.AddRow("1. Global Ignore SSL Errors", config.Settings.GlobalIgnoreSslErrors ? "[green]True[/]" : "[red]False[/]");
            settingsTable.AddRow("2. Log Level", $"[yellow]{config.Settings.LogLevel}[/]");

            var choice = TrySelect("系統設定中心：", new List<string> {
                "1. 切換全局 SSL 忽略狀態",
                "2. 修改日誌等級 (Log Level)",
                "返回主選單"
            }, 10, settingsTable);

            if (choice == null || choice == "返回主選單") return true;

            switch (choice)
            {
                case "1. 切換全局 SSL 忽略狀態":
                    config.Settings.GlobalIgnoreSslErrors = !config.Settings.GlobalIgnoreSslErrors;
                    break;
                case "2. 修改日誌等級 (Log Level)":
                    var levels = new List<string> { "Verbose", "Debug", "Information", "Warning", "Error", "Fatal", "返回" };
                    var selectedLevel = TrySelect("請選擇日誌等級 (重啟程式後生效)：", levels);
                    if (selectedLevel != null && selectedLevel != "返回")
                    {
                        config.Settings.LogLevel = selectedLevel;
                    }
                    break;
            }

            Core.ConfigService.SaveConfig(config);
            AnsiConsole.MarkupLine("[green]✅ 系統設定已更新。[/]");
            Thread.Sleep(500);
        }
    }

    private static bool HandleManagementFlow()
    {
        while (true)
        {
            RefreshScreen();
            var config = Core.ConfigService.LoadConfig();
            
            // 建立顯示名稱與原始 Key 的映射
            var profileMap = config.Profiles.ToDictionary(
                p => $"{p.Key} ({p.Value.Connection.Endpoint} | {p.Value.Connection.Index})",
                p => p.Key
            );

            var displayChoices = profileMap.Keys.ToList();
            displayChoices.Add("[[建立新設定檔]]");
            displayChoices.Add("[[返回主選單]]");

            var selectedDisplay = TrySelect("請選擇要 [blue]管理[/] 的設定檔：", displayChoices);

            if (selectedDisplay == null || selectedDisplay == "[[返回主選單]]") return true;

            if (selectedDisplay == "[[建立新設定檔]]")
            {
                CreateNewProfile();
                continue;
            }

            var selectedProfile = profileMap[selectedDisplay];
            var profile = config.Profiles[selectedProfile];

            while (true)
            {
                RefreshScreen();
                
                // 準備底部的目前連線資訊摘要
                var profileSummary = new Table().Border(TableBorder.Rounded);
                profileSummary.AddColumn("項目");
                profileSummary.AddColumn("詳細資訊");
                profileSummary.AddRow("URL", profile.Connection.Endpoint);
                profileSummary.AddRow("Index", profile.Connection.Index);
                profileSummary.AddRow("User", profile.Connection.Username ?? "");

                var action = TrySelect($"設定檔 [cyan]{selectedProfile}[/] 的操作：", new List<string> {
                            "1. 管理查詢語句清單 (Queries)",
                            "2. 修改連線資訊",
                            "3. 修改導出設定",
                            "4. 刪除此設定檔",
                            "返回上層"
                        }, 10, profileSummary);

                if (action == null || action == "返回上層") break;
                
                if (action == "1. 管理查詢語句清單 (Queries)")
                {
                    ManageQueries(selectedProfile, profile);
                }
                else if (action == "2. 修改連線資訊")
                {
                    EditConnection(selectedProfile, profile);
                }
                else if (action == "3. 修改導出設定")
                {
                    EditExportSettings(selectedProfile, profile);
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

    private static void EditConnection(string profileName, ProfileConfig profile)
    {
        while (true)
        {
            RefreshScreen();
            AnsiConsole.Write(new Rule($"修改 [cyan]{profileName}[/] 的連線資訊").LeftJustified());
            
            var infoTable = new Table().Border(TableBorder.Rounded);
            infoTable.AddColumn("項目");
            infoTable.AddColumn("目前值");
            infoTable.AddRow("1. OpenSearch URL", $"[cyan]{profile.Connection.Endpoint}[/]");
            infoTable.AddRow("2. Target Index", $"[cyan]{profile.Connection.Index}[/]");
            infoTable.AddRow("3. Username", $"[yellow]{(string.IsNullOrEmpty(profile.Connection.Username) ? "(未設定)" : profile.Connection.Username)}[/]");
            infoTable.AddRow("4. Ignore SSL Errors", profile.Connection.IgnoreSslErrors ? "[green]True[/]" : "[red]False[/]");

            var choice = TrySelect("請選擇要修改的項目：", new List<string> {
                "1. 修改 OpenSearch URL",
                "2. 修改 Target Index",
                "3. 修改 Username",
                "4. 切換 SSL 忽略狀態",
                "返回"
            }, 10, infoTable);

            if (choice == null || choice == "返回") break;

            switch (choice)
            {
                case "1. 修改 OpenSearch URL":
                    var newUrl = TryAsk($"請輸入新 URL (目前: {profile.Connection.Endpoint}):");
                    if (!string.IsNullOrWhiteSpace(newUrl)) profile.Connection.Endpoint = newUrl;
                    break;
                case "2. 修改 Target Index":
                    var newIndex = TryAsk($"請輸入新 Index (目前: {profile.Connection.Index}):");
                    if (!string.IsNullOrWhiteSpace(newIndex)) profile.Connection.Index = newIndex;
                    break;
                case "3. 修改 Username":
                    var newUser = TryAsk($"請輸入新帳號 (目前: {profile.Connection.Username}):");
                    if (newUser != null) profile.Connection.Username = newUser;
                    break;
                case "4. 切換 SSL 忽略狀態":
                    profile.Connection.IgnoreSslErrors = !profile.Connection.IgnoreSslErrors;
                    break;
            }

            SaveProfile(profileName, profile);
            AnsiConsole.MarkupLine("[green]✅ 連線資訊已更新。[/]");
            Thread.Sleep(500);
        }
    }

    private static void EditExportSettings(string profileName, ProfileConfig profile)
    {
        while (true)
        {
            RefreshScreen();
            AnsiConsole.Write(new Rule($"修改 [cyan]{profileName}[/] 的導出設定").LeftJustified());

            var infoTable = new Table().Border(TableBorder.Rounded);
            infoTable.AddColumn("項目");
            infoTable.AddColumn("目前值");
            infoTable.AddRow("1. 導出格式 (Format)", $"[yellow]{profile.Export.Format}[/]");
            infoTable.AddRow("2. 欄位清單 (Fields)", profile.Export.Fields.Length == 0 ? "[grey](全部欄位)[/]" : string.Join(", ", profile.Export.Fields));
            infoTable.AddRow("3. 批次大小 (BatchSize)", $"[cyan]{profile.Export.BatchSize}[/]");
            infoTable.AddRow("4. Scroll 逾時 (Timeout)", $"[cyan]{profile.Export.ScrollTimeout}[/]");
            infoTable.AddRow("5. 輸出路徑 (OutputPath)", $"[cyan]{profile.Export.OutputPath}[/]");

            var choice = TrySelect("請選擇要修改的項目：", new List<string> {
                "1. 修改 導出格式 (CSV/JSON)",
                "2. 修改 欄位清單 (Fields)",
                "3. 修改 批次大小 (BatchSize)",
                "4. 修改 Scroll 逾時 (Timeout)",
                "5. 修改 輸出路徑 (OutputPath)",
                "返回"
            }, 10, infoTable);

            if (choice == null || choice == "返回") break;

            switch (choice)
            {
                case "1. 修改 導出格式 (CSV/JSON)":
                    var format = TrySelect("請選擇格式：", new List<string> { "csv", "json", "返回" });
                    if (format != null && format != "返回") profile.Export.Format = format;
                    break;
                case "2. 修改 欄位清單 (Fields)":
                    var fieldsInput = TryAsk($"請輸入欄位名稱，以逗號分隔 (目前: {string.Join(",", profile.Export.Fields)}):");
                    if (fieldsInput != null)
                    {
                        profile.Export.Fields = fieldsInput.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    }
                    break;
                case "3. 修改 批次大小 (BatchSize)":
                    var sizeStr = TryAsk($"請輸入批次抓取數量 (預設 5000):");
                    if (int.TryParse(sizeStr, out int size)) profile.Export.BatchSize = size;
                    break;
                case "4. 修改 Scroll 逾時 (Timeout)":
                    var timeout = TryAsk($"請輸入 Scroll 逾時時間 (例如 1m, 5m):");
                    if (!string.IsNullOrWhiteSpace(timeout)) profile.Export.ScrollTimeout = timeout;
                    break;
                case "5. 修改 輸出路徑 (OutputPath)":
                    var path = TryAsk($"請輸入匯出檔案存放路徑 (目前: {profile.Export.OutputPath}):");
                    if (!string.IsNullOrWhiteSpace(path)) profile.Export.OutputPath = path;
                    break;
            }

            SaveProfile(profileName, profile);
            AnsiConsole.MarkupLine("[green]✅ 導出設定已更新。[/]");
            Thread.Sleep(500);
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
                RefreshScreen();
                // 準備該查詢的內容作為底部顯示（限制顯示行數避免選項被推出螢幕外）
                var queryJson = JsonSerializer.Serialize(profile.Queries[selectedQuery], new JsonSerializerOptions { WriteIndented = true });
                var queryLines = queryJson.Split('\n');
                
                // 限制最多顯示 15 行，避免長查詢導致選項看不到
                const int maxDisplayLines = 15;
                string displayJson;
                bool isTruncated = false;
                
                if (queryLines.Length > maxDisplayLines)
                {
                    displayJson = string.Join('\n', queryLines.Take(maxDisplayLines));
                    isTruncated = true;
                }
                else
                {
                    displayJson = queryJson;
                }
                
                var bottomContent = new List<IRenderable>
                {
                    new Text(""),
                    new Markup($"[grey]查詢語句 [yellow]{selectedQuery}[/] 的內容預覽：[/]"),
                    new Text(displayJson)
                };
                
                if (isTruncated)
                {
                    bottomContent.Add(new Markup($"[dim]...\n(省略 {queryLines.Length - maxDisplayLines} 行，完整內容請選擇「編輯內容」查看)[/]"));
                }
                
                var bottomJson = new Rows(bottomContent.ToArray());

                var action = TrySelect($"查詢語句 [yellow]{selectedQuery}[/] 的操作：", new List<string> {
                    "1. 編輯內容 (Edit)",
                    "2. 重新命名 (Rename)",
                    "3. 刪除此查詢 (Delete)",
                    "返回"
                }, 10, bottomJson);

                if (action == null || action == "返回") continue;

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
        
        // 限制顯示行數，避免長查詢導致選項看不到
        var queryLines = currentQueryJson.Split('\n');
        const int maxDisplayLines = 15;
        string displayJson;
        bool isTruncated = false;
        
        if (queryLines.Length > maxDisplayLines)
        {
            displayJson = string.Join('\n', queryLines.Take(maxDisplayLines));
            isTruncated = true;
        }
        else
        {
            displayJson = currentQueryJson;
        }
        
        // 建立底部的 JSON 顯示區塊
        var bottomContentList = new List<IRenderable>
        {
            new Text(""),
            new Markup("[grey]目前查詢語句（預覽）：[/]"),
            new Text(displayJson)
        };
        
        if (isTruncated)
        {
            bottomContentList.Add(new Markup($"[dim]...\n(省略 {queryLines.Length - maxDisplayLines} 行)[/]"));
        }
        
        var bottomContent = new Rows(bottomContentList.ToArray());

        var choice = TrySelect("請選擇編輯方式：", new List<string> {
                    "使用快速模板 (Match All)",
                    "直接輸入 JSON 字串",
                    "使用外部編輯器 (Vim/Notepad)",
                    "放棄修改"
                }, 10, bottomContent);

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
                string? user = _currentUser;
                string? pwd = _currentPassword;

                // 若測試目標不同於目前啟動之連線，則詢問驗證資訊
                if (profile.Connection.Endpoint != _currentEndpoint || profile.Connection.Index != _currentIndex)
                {
                    AnsiConsole.MarkupLine("[yellow]測試目標與目前使用連線不同，請提供驗證資訊：[/]");
                    var userPrompt = $"請輸入帳號 (Username) [[預設: {Markup.Escape(profile.Connection.Username ?? "")}]]:";
                    user = TryAsk(userPrompt);
                    if (string.IsNullOrEmpty(user)) user = profile.Connection.Username;
                    
                    pwd = TryAsk("請輸入密碼 (Password):", isSecret: true);
                }

                Log.Information("使用者啟動語法測試流程，準備驗證目標: {Endpoint}", profile.Connection.Endpoint);

                AnsiConsole.Status().Start("正在測試查詢語法...", ctx => {
                    // 使用臨時 config 進行測試，不影響原始 profile 儲存
                    var tempConn = new ConnectionConfig {
                        Endpoint = profile.Connection.Endpoint,
                        Index = profile.Connection.Index,
                        Username = user ?? "",
                        IgnoreSslErrors = profile.Connection.IgnoreSslErrors
                    };
                    var result = Core.ConnectionManager.TestQuery(tempConn, pwd, profile.Queries[queryName]);
                    if (result.Success)
                    {
                        Log.Information("語法測試成功");
                        AnsiConsole.MarkupLine($"[green]✔ {result.Message}[/]");
                    }
                    else
                    {
                        Log.Warning("語法測試失敗: {Message}", result.Message);
                        AnsiConsole.MarkupLine($"[red]✘ 測試失敗：{Markup.Escape(result.Message ?? "")}[/]");
                    }
                });
                AnsiConsole.WriteLine("按任意鍵繼續...");
                Console.ReadKey(true);
            }
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "JSON 格式錯誤：{ProfileName} - {QueryName}", profileName, queryName);
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

    private static async Task<bool> HandleExportFlowAsync()
    {
        RefreshScreen();
        if (string.IsNullOrEmpty(_currentEndpoint))
        {
            Log.Warning("導出失敗: 尚未連線就嘗試執行導出");
            AnsiConsole.MarkupLine("[red]❌ 錯誤：尚未建立連線。請先執行「連線資訊選擇 (切換目標)」。[/]");
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
        
        // 取得原始查詢
        var originalQuery = currentProfile.Queries[selectedQueryName];
        var queryToUse = originalQuery;
        
        // 檢查並處理日期範圍參數化
        queryToUse = HandleDateRangeCustomization(originalQuery);
        if (queryToUse == null)
        {
            AnsiConsole.MarkupLine("[yellow]已取消導出操作。[/]");
            return true;
        }
        
        // 執行導出
        await Core.DataStreamer.ExportAsync(currentProfile.Connection, currentProfile.Export, queryToUse, _currentPassword);

        AnsiConsole.MarkupLine("\n[grey]匯出作業結束。按任意鍵回主選單...[/]");
        Console.ReadKey(true);
        return true; 
    }

    private static async Task<bool> HandleConnectionFlowAsync()
    {
        while (true)
        {
            RefreshScreen();
            Log.Information("進入連線流程");
            var config = Core.ConfigService.LoadConfig();
            
            if (config.Profiles.Count == 0)
            {
                AnsiConsole.MarkupLine("[red]⚠ 目前沒有任何設定檔。請先前往「管理設定檔」建立。[/]");
                return false;
            }

            var profileMap = config.Profiles.ToDictionary(
                p => $"{p.Key} ({p.Value.Connection.Endpoint} | {p.Value.Connection.Index})",
                p => p.Key
            );

            var displayChoices = profileMap.Keys.ToList();
            displayChoices.Add("[[返回主選單]]");

            var selectedDisplay = TrySelect("請選擇 [green]連線目標[/]：", displayChoices);

            Log.Information("使用者選擇連線目標: {Target}", selectedDisplay);

            if (selectedDisplay == null || selectedDisplay == "[[返回主選單]]")
            {
                return true; 
            }

            var selectedProfile = profileMap[selectedDisplay];
            var p = config.Profiles[selectedProfile];
            var endpoint = p.Connection.Endpoint;
            var index = p.Connection.Index;
            AnsiConsole.MarkupLine($"已載入設定檔: [cyan]{Markup.Escape(selectedProfile ?? "")}[/] ({Markup.Escape(endpoint ?? "")})");

            // 帳密輸入 (必填校驗)
            string? username = null;
            while (true)
            {
                AnsiConsole.MarkupLine("[grey]💡 提示：OpenSearch 不接受域名格式 (例如 'domain\\user')，請只輸入使用者名稱[/]");
                var userPrompt = $"請輸入帳號 (Username) [[預設: {Markup.Escape(p.Connection.Username ?? "")}]]:";
                username = TryAsk(userPrompt);
                if (username == null) break; // Esc pressed
                if (string.IsNullOrEmpty(username)) username = p.Connection.Username;
                
                // 檢查是否包含反斜線（域名格式）
                if (!string.IsNullOrEmpty(username) && username.Contains("\\"))
                {
                    AnsiConsole.MarkupLine("[yellow]⚠ 警告：偵測到域名格式 (含 '\\')，OpenSearch 可能不接受此格式。[/]");
                    var removePrefix = TryConfirm("是否自動移除域名前綴？(例如 'pcs\\robbinlee' → 'robbinlee')");
                    if (removePrefix == true)
                    {
                        username = username.Split('\\').Last();
                        AnsiConsole.MarkupLine($"[green]已調整為: {Markup.Escape(username)}[/]");
                    }
                    else if (removePrefix == null)
                    {
                        continue; // 使用者按 Esc，重新輸入
                    }
                }
                
                if (!string.IsNullOrEmpty(username)) break;
                AnsiConsole.MarkupLine("[red]❌ 錯誤：帳號不可為空值，請重新輸入。[/]");
            }
            if (username == null) continue;

            string? password = null;
            while (true)
            {
                password = TryAsk("請輸入 [yellow]密碼 (Password)[/]:", isSecret: true);
                if (password == null) break; // Esc pressed
                if (!string.IsNullOrEmpty(password)) break;
                AnsiConsole.MarkupLine("[red]❌ 錯誤：密碼不可為空值，請重新輸入。[/]");
            }
            if (password == null) continue;

            bool isValidated = false;
            await AnsiConsole.Status()
                .StartAsync("正在驗證連線資訊...", async ctx => {
                    var result = await Core.ConnectionManager.ValidateConnectionAsync(p.Connection, password);
                    if (result.Success)
                    {
                        Log.Information("連線驗證成功: Endpoint={Endpoint}, Index={Index}, User={User}", endpoint, index, username);
                        AnsiConsole.MarkupLine($"[green]成功連線至:[/] {Markup.Escape(endpoint ?? "")}");
                        isValidated = true;
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[red]❌ 連線失敗：{Markup.Escape(result.Message)}[/]");
                    }
                });

            if (!isValidated)
            {
                AnsiConsole.MarkupLine("[yellow]驗證未通過，請檢查帳號密碼或伺服器網址後重試。[/]");
                AnsiConsole.MarkupLine("[grey]按任意鍵重新選擇連線目標或按 Esc 返回主選單...[/]");
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Escape) return true;
                continue;
            }

            _currentEndpoint = endpoint;
            _currentIndex = index;
            _currentUser = username;
            _currentPassword = password;

            RefreshScreen();
            var summary = new Table().Border(TableBorder.Rounded);
            summary.AddColumn("[grey]項目[/]");
            summary.AddColumn("[grey]詳細資訊[/]");
            summary.AddRow("OpenSearch URL", $"[cyan]{Markup.Escape(endpoint ?? "")}[/]");
            summary.AddRow("Target Index", $"[cyan]{Markup.Escape(index ?? "")}[/]");
            summary.AddRow("User", $"[yellow]{Markup.Escape(username ?? "")}[/]");

            AnsiConsole.Write(
                new Panel(summary)
                    .Header("[bold green] 連線就緒 (Connection Ready) [/]")
                    .BorderColor(Color.Green)
                    .Padding(1, 1, 1, 1));

            AnsiConsole.MarkupLine("\n[bold]您現在可以開始進行導出作業。[/]");
            AnsiConsole.MarkupLine("[grey]按任意鍵回主選單...[/]");
            Console.ReadKey(true);
            return false; 
        }
    }

    private static void CreateNewProfile()
    {
        AnsiConsole.MarkupLine("[grey](提示: 隨時按 Esc 可取消)[/]");
        var endpoint = TryAsk("請輸入 OpenSearch [bold]URL[/] (例如 http://localhost:9200):");
        if (endpoint == null) return;

        var index = TryAsk("請輸入 [bold]Index[/] 名稱 (例如 logs-*):");
        if (index == null) return;

        var username = TryAsk("請輸入預設 [yellow]帳號 (Username)[/]:");
        if (username == null) return;

        var profileName = AnsiConsole.Ask<string>("請輸入設定檔名稱 (例如 Prod-Server):");
        if (string.IsNullOrEmpty(profileName)) profileName = "New-Profile-" + DateTime.Now.ToString("yyyyMMdd-HHmm");

        var newProfile = new ProfileConfig
        {
            Connection = new ConnectionConfig
            {
                Endpoint = endpoint,
                Index = index,
                Username = username,
                Password = null,
                IgnoreSslErrors = true
            }
        };
        Core.ConfigService.AddProfile(profileName, newProfile);
        Log.Information("儲存新設定檔: {ProfileName}", profileName);
        AnsiConsole.MarkupLine($"[green]設定檔 {Markup.Escape(profileName)} 已儲存。[/]");
        Thread.Sleep(1000);
    }

    private static string? TrySelect(string title, List<string> choices, int pageSize = 10, IRenderable? bottomContent = null)
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
                
                // 使用簡單的分隔符號代替 Rule，避免撐開寬度
                table.AddEmptyRow();
                table.AddRow("[grey]-------------------------------------------[/]");
                table.AddRow($"[grey](↑/↓ 選擇, Enter 確認, Esc 返回)  {selectedIndex + 1}/{choices.Count}[/]");

                var panel = new Panel(table)
                {
                    Header = new PanelHeader(title)
                };
                panel.BorderColor(Color.Blue);

                if (bottomContent != null)
                {
                    ctx.UpdateTarget(new Rows(panel, bottomContent));
                }
                else
                {
                    ctx.UpdateTarget(panel);
                }

                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.UpArrow) selectedIndex = (selectedIndex - 1 + choices.Count) % choices.Count;
                else if (key.Key == ConsoleKey.DownArrow) selectedIndex = (selectedIndex + 1) % choices.Count;
                else if (key.Key == ConsoleKey.Enter) return choices[selectedIndex];
                else if (key.Key == ConsoleKey.Escape) return null;
            }
        });
    }

    private static bool? TryConfirm(string message)
    {
        AnsiConsole.Markup($"{message} [grey](y/n/Esc)[/] ");
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Y)
            {
                AnsiConsole.MarkupLine("[green]Yes[/]");
                Log.Information("使用者確認: {Message} -> Yes", message);
                return true;
            }
            if (key.Key == ConsoleKey.N)
            {
                AnsiConsole.MarkupLine("[red]No[/]");
                Log.Information("使用者確認: {Message} -> No", message);
                return false;
            }
            if (key.Key == ConsoleKey.Escape)
            {
                AnsiConsole.WriteLine();
                Log.Information("使用者確認: {Message} -> Cancel (Esc)", message);
                return null;
            }
        }
    }

    private static object? HandleDateRangeCustomization(object query)
    {
        try
        {
            var queryJson = JsonSerializer.Serialize(query);
            
            if (!queryJson.Contains("@timestamp") || !queryJson.Contains("\"range\""))
            {
                return query;
            }
            
            AnsiConsole.MarkupLine("[yellow]偵測到查詢包含日期範圍條件。[/]");
            var customize = TryConfirm("是否要自訂日期範圍？(否則使用系統時間起算24小時)");
            
            // 初始化為預設值（24小時），避免編譯器警告
            DateTime fromDate = DateTime.UtcNow.AddDays(-1);
            DateTime toDate = DateTime.UtcNow;
            bool usedEscape = false; // 追蹤是否按了 Esc
            
            if (customize == true)
            {
                // 使用者選擇自訂：讓使用者輸入日期
                AnsiConsole.MarkupLine("\n[cyan]請輸入日期範圍：[/]");
                AnsiConsole.MarkupLine("[grey]格式範例: 2026-02-20 或 2026-02-20T10:30:00[/]");
                AnsiConsole.MarkupLine("[grey]提示: 直接按 Enter 使用預設值 (系統時間起算24小時)，按 Esc 取消[/]\n");
                
                // 輸入起始日期（循環直到成功或取消）
                while (true)
                {
                    var fromDateStr = TryAsk("起始日期 (gte) [[預設: 24小時前]]:");
                    
                    if (fromDateStr == null)
                    {
                        // 按 Esc 取消
                        AnsiConsole.MarkupLine("[yellow]已取消日期輸入，使用系統時間起算24小時。[/]");
                        fromDate = DateTime.UtcNow.AddDays(-1);
                        toDate = DateTime.UtcNow;
                        usedEscape = true;
                        break;
                    }
                    
                    if (string.IsNullOrWhiteSpace(fromDateStr))
                    {
                        // 使用預設值
                        fromDate = DateTime.UtcNow.AddDays(-1);
                        AnsiConsole.MarkupLine($"[grey]使用預設起始日期: {fromDate:yyyy-MM-dd HH:mm:ss}[/]");
                        break;
                    }
                    
                    if (DateTime.TryParse(fromDateStr, out fromDate))
                    {
                        fromDate = DateTime.SpecifyKind(fromDate, DateTimeKind.Utc);
                        AnsiConsole.MarkupLine($"[green]✓ 起始日期: {fromDate:yyyy-MM-dd HH:mm:ss}[/]");
                        break;
                    }
                    
                    // 格式錯誤，顯示錯誤訊息並重新輸入
                    AnsiConsole.MarkupLine("[red]✗ 日期格式錯誤！請重新輸入或按 Esc 取消。[/]");
                }
                
                // 如果起始日期沒有按 Esc，繼續輸入結束日期
                if (!usedEscape)
                {
                    while (true)
                    {
                        var toDateStr = TryAsk("結束日期 (lte) [[預設: 現在]]:");
                        
                        if (toDateStr == null)
                        {
                            // 按 Esc 取消
                            AnsiConsole.MarkupLine("[yellow]已取消日期輸入，使用系統時間起算24小時。[/]");
                            fromDate = DateTime.UtcNow.AddDays(-1);
                            toDate = DateTime.UtcNow;
                            break;
                        }
                        
                        if (string.IsNullOrWhiteSpace(toDateStr))
                        {
                            // 使用預設值
                            toDate = DateTime.UtcNow;
                            AnsiConsole.MarkupLine($"[grey]使用預設結束日期: {toDate:yyyy-MM-dd HH:mm:ss}[/]");
                            break;
                        }
                        
                        if (DateTime.TryParse(toDateStr, out toDate))
                        {
                            toDate = DateTime.SpecifyKind(toDate, DateTimeKind.Utc);
                            
                            // 驗證結束日期不能早於起始日期
                            if (toDate < fromDate)
                            {
                                AnsiConsole.MarkupLine("[red]✗ 結束日期不能早於起始日期！請重新輸入。[/]");
                                continue;
                            }
                            
                            AnsiConsole.MarkupLine($"[green]✓ 結束日期: {toDate:yyyy-MM-dd HH:mm:ss}[/]");
                            break;
                        }
                        
                        // 格式錯誤，顯示錯誤訊息並重新輸入
                        AnsiConsole.MarkupLine("[red]✗ 日期格式錯誤！請重新輸入或按 Esc 取消。[/]");
                    }
                }
            }
            else
            {
                // 使用者選擇 No 或按 Esc：直接使用 24 小時預設值
                fromDate = DateTime.UtcNow.AddDays(-1);
                toDate = DateTime.UtcNow;
                AnsiConsole.MarkupLine($"[grey]使用系統時間起算24小時: {fromDate:yyyy-MM-dd HH:mm:ss} 至 {toDate:yyyy-MM-dd HH:mm:ss}[/]");
            }
            
            var gteValue = fromDate.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var lteValue = toDate.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            
            AnsiConsole.MarkupLine($"\n[green]日期範圍已設定:[/]");
            AnsiConsole.MarkupLine($"  從: [cyan]{gteValue}[/]");
            AnsiConsole.MarkupLine($"  到: [cyan]{lteValue}[/]\n");
            
            var modifiedJson = ReplaceTimestampRange(queryJson, gteValue, lteValue);
            var modifiedQuery = JsonSerializer.Deserialize<JsonElement>(modifiedJson);
            return modifiedQuery;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "處理日期範圍自訂時發生錯誤");
            AnsiConsole.MarkupLine($"[red]處理日期時發生錯誤: {Markup.Escape(ex.Message)}[/]");
            return query;
        }
    }
    
    private static string ReplaceTimestampRange(string queryJson, string gteValue, string lteValue)
    {
        try
        {
            var queryObj = JsonSerializer.Deserialize<JsonElement>(queryJson);
            var modifiedObj = ReplaceTimestampInElement(queryObj, gteValue, lteValue);
            return JsonSerializer.Serialize(modifiedObj);
        }
        catch
        {
            return queryJson;
        }
    }
    
    private static JsonElement ReplaceTimestampInElement(JsonElement element, string gteValue, string lteValue)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var dict = new Dictionary<string, object>();
            
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name == "range" && property.Value.ValueKind == JsonValueKind.Object)
                {
                    var rangeObj = property.Value;
                    if (rangeObj.TryGetProperty("@timestamp", out var timestampObj))
                    {
                        var newTimestamp = new Dictionary<string, object>();
                        
                        foreach (var tsProp in timestampObj.EnumerateObject())
                        {
                            if (tsProp.Name == "gte")
                                newTimestamp["gte"] = gteValue;
                            else if (tsProp.Name == "lte")
                                newTimestamp["lte"] = lteValue;
                            else
                                newTimestamp[tsProp.Name] = JsonSerializer.Deserialize<object>(tsProp.Value.GetRawText())!;
                        }
                        
                        dict["range"] = new Dictionary<string, object> { { "@timestamp", newTimestamp } };
                        continue;
                    }
                }
                
                dict[property.Name] = JsonSerializer.Deserialize<object>(
                    ReplaceTimestampInElement(property.Value, gteValue, lteValue).GetRawText())!;
            }
            
            return JsonSerializer.SerializeToElement(dict);
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var list = new List<object>();
            foreach (var item in element.EnumerateArray())
            {
                list.Add(JsonSerializer.Deserialize<object>(
                    ReplaceTimestampInElement(item, gteValue, lteValue).GetRawText())!);
            }
            return JsonSerializer.SerializeToElement(list);
        }
        
        return element;
    }

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
                var result = input.ToString();
                if (isSecret)
                    Log.Information("使用者輸入: {Prompt} -> [SECRET]", prompt);
                else
                    Log.Information("使用者輸入: {Prompt} -> {Value}", prompt, result);
                return result;
            }
            if (key.Key == ConsoleKey.Escape)
            {
                Console.WriteLine();
                Log.Information("使用者按 Esc 取消輸入: {Prompt}", prompt);
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
