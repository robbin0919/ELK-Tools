# OpenSearch 自動化資料匯出工具 (OSDX) 系統設計文件

本文件定義了「OpenSearch 自動化資料匯出工具 (OSDX)」的系統架構與開發規格。本工具旨在解決海量資料匯出時的穩定性與自動化需求。

---

## 1. 專案概述
本專案名為 **OSDX** (全名為 **O**pen**S**earch **D**ata **X**port)，是一個輕量級、跨平台的命令列工具 (CLI)，用於自動化從 OpenSearch 叢集匯出海量資料（50 萬筆以上）至本地檔案（CSV/JSON）。該工具需支援手動執行與自動化排程設定，並能運行於 Windows、Linux 及 Docker 環境。

*   **OS**: OpenSearch
*   **D**: Data (資料)
*   **X**: Xport (匯出 / 擷取)

## 2. 設計目標
*   **下載即用**：使用者無需安裝額外 Runtime（如 .NET SDK 或 Python 環境）。
*   **跨平台支援**：單一程式碼庫需支援 Windows 11 與 Linux。
*   **海量資料處理**：採用 Scroll API 機制，確保穩定匯出 50 萬筆以上資料而不觸發 `max_result_window` 限制。
*   **自動化友好**：支援設定檔與命令列參數，便於整合進 Docker 與排程工具。

## 3. 技術選型
*   **開發語言**：.NET 8 (C#)
*   **TUI 函式庫**：`Spectre.Console` (用於打造美觀的終端機介面、進度條與互動式選單)
*   **發佈模式**：自我包含 (Self-contained) 單一執行檔
*   **通訊協議**：OpenSearch REST API (使用 OpenSearch.Client SDK)
*   **介面類型**：CLI / TUI 混合模式

## 4. 系統架構
系統分為五個核心模組：
1.  **Config & Argument Parser**：解析命令列參數與設定檔。
2.  **Interactive Wizard (TUI Manager)**：當無參數啟動時，負責引導使用者進行互動式設定。
3.  **Connection Manager**：建立與 OpenSearch 的安全連線（支援 Basic Auth 與 SSL 忽略）。
4.  **Data Streamer**：核心邏輯，執行 Scroll API 分批抓取資料。
5.  **File Writer**：將抓取的資料流即時寫入本地磁碟（串流寫入以節省記憶體）。

## 5. 詳細設計

### 5.1 設定檔設計 (`config.json`)
為了支援多組設定，設定檔將改為「Profiles」結構，且每個 Profile 可包含多個具名查詢語句：
```json
{
  "Profiles": {
    "Prod-Web-Logs": {
      "Connection": {
        "Endpoint": "https://prod-os:9200",
        "Index": "web-logs-*",
        "Username": "admin",
        "IgnoreSslErrors": true
      },
      "Export": {
        "Format": "csv",
        "Fields": ["@timestamp", "ip", "method", "status"],
        "BatchSize": 5000,
        "OutputPath": "./exports/prod/"
      },
      "Queries": {
        "All-Data": { "match_all": {} },
        "Error-Only": { "term": { "status": 500 } }
      }
    }
  },
  "DefaultProfile": "Prod-Web-Logs"
}
```

### 5.2 核心工作流程
1.  **模式與 Profile 判定**：
    *   **指定 Profile (CLI)**：若啟動時帶有 `--profile [Name]`，程式將直接載入該 Profile 的設定。若該 Profile 帶有多個 Query，且未指定 `--query-name`，則預設使用第一個或名為 `Default` 的查詢。
    *   **引導模式 (TUI)**：
        1. 首先顯示**已儲存設定檔選單**供使用者挑選連線目標。
        2. 選定後，若該 Profile 有多個 Query，系統將引導使用者挑選要使用的查詢語句。
2.  **執行匯出 (核心邏輯)**：
    *   **建立快照 (Initial Search)**：發送帶有 `scroll=[逾時時間]` 參數的 Search 請求，這會在伺服器端建立一個資料快照，並回傳第一個 `scroll_id`。
    *   **分批迴圈 (Scroll Loop)**：
        *   使用 `scroll_id` 向 `/_search/scroll` 終端點發送請求。
        *   每次抓取 `BatchSize` 指定的筆數。
        *   **實時進度更新**：透過 TUI 介面（如 Spectre.Console Progress Bar）更新已匯出總筆數。
        *   **串流寫入 (Stream Writing)**：將抓取到的資料立即轉換為指定格式 (CSV/JSON) 並附加 (Append) 到檔案中，避免大量資料佔用記憶體。
        *   **迴圈終止條件**：當回傳的 `hits` 陣列長度為 0 時，表示資料已全數抓取完畢。
3.  **清理與完成**：
    *   **釋放資源**：發送 `DELETE /_search/scroll` 請求以釋放 OpenSearch 伺服器端的快照資源。
    *   **最終報告**：顯示總耗時、最終檔案路徑與大小。程式根據執行模式決定是否自動關閉。

### 5.3 命令列指令範例
*   **指定設定檔執行**：`./osdx --profile Prod-Web-Logs`
*   **指定設定檔並覆蓋索引**：`./osdx --profile Debug-Errors --index temp-logs`
*   **指定外部設定檔**：`./osdx --config /path/to/my-config.json --profile MyTask`

### 5.4 交互式體驗設計 (TUI Experience)
1.  **設定檔挑選**：
    *   啟動後顯示清單：
        *   `> Prod-Web-Logs (https://prod-os:9200)`
        *   `  Debug-Errors (https://dev-os:9200)`
        *   `  [建立新設定檔]`
2.  **連線與驗證**：選定後，程式會列出該 Profile 的預設值，使用者可直接按 Enter 確認或修改。
3.  **導出設定與執行**：
    *   **格式選擇**：使用上下鍵選擇 CSV 或 JSON。
    *   **確認執行**：顯示設定摘要，詢問「是否開始導出？[Y/n]」。
    *   **視覺化進度**：執行期間顯示彩色進度條，實時顯示「已匯出筆數」與「預估剩餘時間」。
    *   **完成提示**：匯出成功後，顯示檔案路徑並提供「開啟資料夾」或「結束程式」的選項。

### 5.5 查詢語句維護 (Query Maintenance)
為了彈性過濾匯出資料，系統於「管理設定檔」中提供 Query 編輯功能：
1.  **維護方式**：
    *   **多 Query 管理**：支援在 Profile 下新增、重新命名、刪除多組具名 Query。
    *   **快速模板**：提供 `match_all`、`range` (時間區間) 等常用 JSON 範本供快速套用。
    *   **文字編輯**：支援在終端機直接輸入 JSON 字串。
    *   **外部編輯器 (進階)**：提供「使用預設編輯器編輯」選項，將 Query 寫入暫存檔並調用系統 `notepad` 或 `vim` 進行編輯，存檔後自動回填。
2.  **校驗邏輯**：
    *   **格式檢查**：儲存前必須通過本地端 `System.Text.Json` 的解析測試。
    *   **語法測試 (選用)**：提供「測試查詢」功能，發送小型 Search 請求 (size=0) 至 OpenSearch 以確認語法是否被伺服器接受。

## 6. 部署與排程設計

### 6.1 Docker 部署
*   **Dockerfile 策略**：使用最小化的 Linux Image (如 Alpine)，將編譯好的獨立執行檔編入。
*   **執行方式**：
    ```bash
    docker run -v $(pwd)/exports:/app/exports osdx-tool --config /app/config.json
    ```

### 6.2 排程設定
*   **Linux (Cron)**：
    `0 4 * * * /path/to/osdx --config /path/to/config.json >> /var/log/osdx.log 2>&1`
*   **Windows (工作排程器)**：
    建立基本工作，執行程式指向 `osdx.exe`，並將設定檔路徑填入參數欄。

## 7. 安全與錯誤處理
*   **驗證管理**：密碼支援從環境變數讀取，避免寫死在設定檔中。
*   **重試機制**：針對網路短暫連線中斷實作指數退避 (Exponential Backoff) 重試。
*   **日誌記錄**：整合 `Serilog` 輸出執行日誌，包含開始時間、匯出筆數、錯誤訊息與完成時間。

## 8. 測試計畫
*   **單元測試**：測試 Config 解析與 Query 建構邏輯。
*   **整合測試**：針對實體 OpenSearch 節點進行 10 萬、50 萬、100 萬筆資料的匯出壓力測試。
*   **相容性測試**：分別在 Windows 11 與 Ubuntu 22.04 執行編譯後的執行檔。

## 9. 測試環境建置
為了方便開發與驗證，請參考 [OpenSearch 測試環境建置指南](./OpenSearch_Test_Environment_Setup.md) 以快速啟動單節點 Docker 容器並注入樣例資料。
