# 變更記錄（CHANGELOG）

遵循 Keep a Changelog 格式，並建議使用 Semantic Versioning（SemVer）。

## [Unreleased]
- 待記錄的變更（尚未釋出）。

## [1.5.7] - 2026-03-15
### 改進
- 調整日誌輸出以避免破壞 TUI 進度條及畫面：在互動式引導模式（TUI）中移除 Console sink（僅寫入檔案），在 CLI 模式下將 Console sink 最低等級限制為 `Warning`，避免 Info/Debug 訊息在進度顯示期間打斷畫面（相關程式：`Program.cs`）。
### 備註
- 已備份原始檔案於 osdx/backups/20260315_005142/osdx/CHANGELOG.md、osdx/backups/20260315_005142/osdx/osdx.csproj。

## [1.5.6] - 2026-03-14
### 修正
- **重要修正：日期替換未生效問題**：`QueryHelper.ReplaceTimestampInElement()` 原先硬編只處理 `range.@timestamp`，導致使用 `range.timestamp`（沒有 `@`）的查詢日期範圍完全未被替換，送出的 Request 仍為原始舊日期。
- 改為動態匹配：搜尋 `range` 網層下第一個含有 `gte` 或 `lte` 屬性的任意欄位（支援 `@timestamp`、`timestamp`、`created_at` 等所有日期欄位名稱），確保使用者輸入的日期範圍正確套用到實際送出的 Request。
### 備註
- 已備份原始檔案於 osdx/backups/20260314_221026/osdx/Core/QueryHelper.cs。

## [1.5.5] - 2026-03-14
### 修正
- **消除 IL3000 警告**：`InteractiveWizard.cs` 的 `RefreshScreen()` 與 `HandleAboutFlow()` 移除 `Assembly.Location` 呼叫（在 single-file 發佈模式下永遠回傳空字串），改用 `Process.GetCurrentProcess().MainModule?.FileName ?? AppContext.BaseDirectory` 取得執行檔路徑，確保建置日期能正確讀取。
- **消除 CS8600 警告**：移除對 `Assembly.Location`（可能為 null）的直接指派，同步解決 nullable 型別警告。
### 備註
- 已備份原始檔案於 osdx/backups/20260314_220047/osdx/CHANGELOG.md、osdx.csproj、InteractiveWizard.cs。

## [1.5.4] - 2026-03-14
### 新增
- **TUI 主畫面版本資訊顯示**：`RefreshScreen()` 左側新增版本號（`v{Version}`）與建置日期（取自執行檔最後修改時間）的即時顯示，讓使用者隨時掌握目前版本。
- **「關於」頁面動態化**：`HandleAboutFlow()` 改為動態讀取版本號、建置日期與 .NET Runtime 版本，取代先前的靜態文字。
### 備註
- 已備份原始檔案於 osdx/backups/20260314_214106/osdx/CHANGELOG.md、osdx/backups/20260314_214106/osdx/osdx.csproj。

## [1.5.3] - 2026-03-14
### 改進
- 引入 `Microsoft.Extensions.Configuration` 與 `Serilog.Settings.Configuration`；Serilog 改由 `appsettings.json` 設定（含 `MinimumLevel`、`WriteTo`）。
- 移除 `Program.cs` 中手動讀取 `config.json` LogLevel 的程式碼；移除 `SettingsConfig.LogLevel` 屬性（重構）。
- 新增 `ConfigService.GetLogLevel()` / `ConfigService.SetLogLevel()`，使用 `JsonNode` 操作 `appsettings.json`。
- TUI 系統設定：修改日誌等級現在直接寫入 `appsettings.json`（不再透過 `config.json`）。
- `DataStreamer` 新增 `[Request]` INFO 日誌，記錄每次送出的初始查詢 JSON（LowLevel DSL / High-Level / Fallback）；Scroll 批次請求維持 Debug 層級以避免日誌爆量。
### 備註
- 已備份原始檔案於 osdx/backups/20260314_211036/CHANGELOG.md、osdx/backups/20260314_211036/osdx.csproj。

## [1.5.2] - 2026-03-14
### 修正
- 統一日期範圍顯示與注入為 UTC+8（InteractiveWizard.cs、Program.cs、Core/QueryHelper.cs）。
### 備註
- 已備份原始檔案於 osdx/backups/Program.cs.backup_20260314_195931、osdx/backups/InteractiveWizard.cs.backup_20260314_195931、osdx/backups/QueryHelper.cs.backup_20260314_195931。

## [1.5.1] - 2026-03-07
### 新增
- **自動化模式日期注入**：`export` 指令新增 `--from` 與 `--to` 參數，支援在 CLI 模式下動態覆蓋查詢中的 `@timestamp` 範圍。
- **日期預設值策略**：若僅指定 `--from` 或 `--to` 其中之一，另一端將自動補足（預設 24 小時範圍）。

### 重構
- **職責分離**：將 JSON 查詢處理邏輯從 `InteractiveWizard` 提取至 `QueryHelper` 核心工具類別，實現 TUI 與 CLI 的代碼共享。

## [1.5.0] - 2026-03-06
### 新增
- **自動化執行模式 (CLI Mode)**：新增 `export` 指令，支援透過命令列參數直接啟動導出任務。
- **職責分離架構**：
  - **指令模式**：僅供執行任務，不提供設定修改功能，確保自動化流程的穩定性。
  - **交互模式 (TUI)**：維持作為唯一的配置管理入口（Profile/Query 管理）。
- **參數覆蓋功能**：在 CLI 執行時，支援臨時覆蓋 `username`、`password`、`output` 與 `batch-size` 等參數。
- **多層次密碼讀取**：支援從 CLI 參數、環境變數 (`OSDX_PASSWORD`) 或互動式隱藏輸入讀取連線密碼。
- **自動化狀態回傳**：實作標準結束碼 (Exit Codes)，便於整合至 Cron 或排程工具。

### 改進
- 整合 `System.CommandLine` 進行強韌的參數解析。
- 最佳化啟動邏輯：若無參數則自動進入 TUI 引導模式，若有參數則切換至 CLI 模式。
- 強化錯誤診斷：在自動化模式下若 Profile 不存在，將提供明確的引導訊息。

## [1.4.4] - 2026-03-01
### 新增
- **「關於」功能頁面**：在主選單新增選項，顯示版本資訊、開發者資料與專案 GitHub 連結。
- 在 `osdx.csproj` 中定義明確的版本資訊與中繼資料。

### 改進
- 統一介面風格，使用兼容性分隔線與彩色提示。

## [1.4.3] - 2026-03-01
### 新增
- **日期輸入驗證與重試機制**：當使用者輸入錯誤的日期格式時，系統不再直接使用預設值，而是要求重新輸入。
- 新增結束日期不得早於起始日期的邏輯驗證。

### 改進
- 改善使用者體驗，提供明確的錯誤提示訊息（✗ 日期格式錯誤！）。
- 成功輸入時顯示確認訊息（✓）。
- 使用者可選擇按 Esc 取消輸入並使用系統預設值（24小時）。
- 重試循環確保資料準確性，避免因輸入錯誤而執行錯誤的查詢範圍。

### 修正
- 修正編譯錯誤 CS0103：使用 `usedEscape` 布林變數追蹤 Esc 按鍵狀態，取代區域變數 `fromDateStr` 的作用域問題。
- 修正編譯錯誤 CS0165：在宣告時初始化 `fromDate` 和 `toDate` 變數為預設值（24小時），確保所有執行路徑都有明確賦值。

## [1.4.2] - 2026-03-01
### 修正
- **重要修正**：修正當使用者選擇 No 或按 Esc 時，系統錯誤地返回原始查詢而不是使用24小時預設值的問題。
- 現在的行為：
  - **Yes**：進入自訂模式，讓使用者輸入日期（或按 Enter 使用24小時）
  - **No/Esc**：直接使用系統時間起算24小時，不再詢問
- 增加提示訊息，明確顯示所使用的日期範圍。

## [1.4.1] - 2026-03-01
### 改進
- 修改日期範圍預設值從「最近 15 天」改為「系統時間起算24小時」。
- 更新提示文字：從「使用查詢語句中的預設值」改為「使用系統時間起算24小時」。
- 日期顯示格式增加時間部分，提供更精確的時間範圍資訊。

## [1.3.4] - 2026-02-28
### 改進
- 進度條時間顯示改為「已執行時間」，使用 `ElapsedTimeColumn` 取代 `RemainingTimeColumn`。
- 移除不準確的剩餘時間預估，改為顯示實際已執行時長，提供更直觀的進度資訊。

## [1.4.0] - 2026-02-28
### 新增
- **日期範圍自訂功能**：執行導出前可動態輸入日期範圍，無需修改查詢語句。
- 自動偵測查詢中的 `@timestamp` range 條件，提示使用者是否自訂。
- 支援多種日期格式輸入：`2026-02-20` 或 `2026-02-20T10:30:00`。
- 提供預設值選項：按 Enter 使用最近 15 天的資料。
- 智能替換：遞迴搜尋並替換 JSON 查詢結構中的日期值。

### 改進
- 使用者可在每次導出時靈活調整時間範圍，提升操作便利性。
- 日期輸入介面友善，提供格式提示與預設值說明。

### 修正
- 修正日期輸入提示中方括號導致 Spectre.Console markup 解析錯誤的問題。

## [1.3.3] - 2026-02-28
### 改進
- 進度條改用粗體 ASCII 字符：已完成使用 `█` (實心方塊)，未完成使用 `░` (淺色陰影)。
- 創建 `CustomProgressBarColumn` 自訂進度條欄位，大幅提升視覺辨識度。
- 解決細線進度條不明顯的問題，色彩對比更清晰。

## [1.3.2] - 2026-02-28
### 改進
- 進度條視覺大幅優化：加寬至 50 字元，使用彩色樣式（綠色已完成 / 灰色未完成）。
- 新增傳輸速度顯示欄位，提供更完整的下載資訊。
- 優化任務描述文字樣式為粗體青色，提升視覺識別度。

## [1.3.1] - 2026-02-28
### 改進
- `DataStreamer.cs` 檔案添加版本歷程註記，同步至 v1.3.0。
- 優化資料導出進度條顯示：加寬進度條至 40 字元，添加已下載筆數顯示，改用更明顯的動畫效果。
- 設定進度條自動刷新與保留完成狀態，提升使用者體驗。

### 技術改進
- 在 `DataStreamer.ExportAsync()` 中使用 `QueryString` 參數正確傳遞 scroll 超時設定，解決 LowLevel API 的 TimeSpan 類型轉換問題。

## [1.3.0] - 2026-02-28
### 改進
- 改進連線驗證方法 4：使用實際查詢（match_all, size=0）取代 `Indices.Exists` 檢查。
- 解決只有 `indices:data/read/search` 查詢權限但無 `indices:admin/get` 管理權限的使用者連線驗證失敗問題。
- 驗證成功時明確提示「您有查詢權限，可以正常使用導出功能」。

### 修正
- `DataStreamer.ExportAsync()` 應用智能查詢包裝機制，修正資料導出時的查詢雙重包裝問題。
- 完整 DSL 查詢使用 LowLevel API 直接發送，簡單查詢條件使用 High-Level API 包裝。
- 解決從 OpenSearch Dashboard 複製的完整 DSL 無法正常導出資料的問題。

## [1.2.1] - 2026-02-28
### 修正
- 修正查詢管理界面長查詢顯示問題：限制查詢預覽最多顯示 15 行，超過時顯示省略提示。
- 同時修正「查詢選擇」和「查詢編輯」兩處界面的顯示問題。
- 避免長查詢（如從 OpenSearch Dashboard 複製的完整 DSL）導致編輯選項被推出螢幕外看不到。

## [1.2.0] - 2026-02-28
### 修正
- `TestQuery()` 修正對完整查詢 DSL 進行二次包裝的問題，避免將 sort、size、aggs 等頂層字段錯誤包裝在 `{"query": ...}` 內。
- 智能偵測查詢結構：檢查頂層是否包含 "query" 字段，若有則直接使用原始查詢，若無則自動包裝為標準請求格式。
- 支援從 OpenSearch Dashboard 複製的完整查詢 DSL（包含 sort、size、aggs、highlight 等複雜結構）。

### 改進
- `TestQuery()` 增加 Debug 日誌記錄查詢處理方式，方便診斷。

## [1.1.0] - 2026-02-28
### 新增
- 新增四種連線驗證方式自動切換機制：
  1. Ping (HEAD /)
  2. Cluster Health (GET /_cluster/health)
  3. Root (GET /)
  4. Index 存在檢查 (HEAD /{index}) - 特別適用於只有 Index 層級權限的使用者
- 新增帳號格式自動偵測與修正：自動偵測 Windows 域名格式（如 `domain\user`），提供互動式選項移除域名前綴。
- 新增 PowerShell 獨立測試腳本 `Scripts/Test-OpenSearchConnection.ps1`，可直接測試 OpenSearch 連線，排除應用程式邏輯問題。

### 改進
- 智慧型 403 錯誤診斷：區分「認證失敗」與「權限不足」兩種情況。
- 自動偵測 `security_exception` 並判斷為權限問題，顯示「✓ 帳號密碼正確，已通過認證」確認訊息。
- 提供精確的解決方案：列出需要的具體權限（如 `cluster:monitor/health`），建議聯繫管理員或使用更高權限帳號。
- 401 錯誤提供認證失敗的可能原因；自動偵測 SSL 相關錯誤並提供額外提示。
- 改善日誌記錄：記錄 `IgnoreSslErrors` 狀態、每種驗證方法的嘗試結果和 HTTP 狀態碼。
- 優化使用者體驗：在帳號輸入界面添加格式提示，連線成功時顯示使用的驗證方法，錯誤訊息更加結構化。

### 修正
- 修正命名空間衝突：明確指定 `OpenSearch.Net.HttpMethod` 避免與 `System.Net.Http.HttpMethod` 衝突。
- 改善連線可靠性：當 HEAD / 請求被拒絕（403）時嘗試其他端點，提高在 OpenSearch Security Plugin 嚴格安全設定下的連線成功率。

### 驗證場景
- OpenSearch Security Plugin 環境
- 使用者具有 backend_roles 但缺少 cluster 權限
- 自動識別 `security_exception` 類型錯誤
- Index 層級權限檢查
- 從 OpenSearch Dashboard 複製的完整查詢 DSL
- 包含 sort、size、aggs、highlight 等複雜查詢結構

---

使用說明：
- 每次主要釋出或合併重大變更時，請於頂端的 Unreleased 區段記錄變更，並在釋出時移入新版本章節。
- 建議條目類型：新增（Added）、修正（Fixed）、改進（Changed）、移除（Removed）、重構（Refactored）、驗證場景（Validated Scenarios）。

範例格式：
- 新增: 新功能描述
- 修正: 修補 bug 描述
- 改進: 效能或可維護性改善描述

維護者：請在每次重要變更時同步更新此檔案。
