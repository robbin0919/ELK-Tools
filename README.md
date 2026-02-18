# ELK-Tools 🛠️

這是一個專為 OpenSearch (ELK 生態系) 打造的工具集與技術指南，旨在簡化資料匯出、自動化維運及開發流程。

## 🌟 核心組件

### 1. [OSDX (OpenSearch Data Xport)](./osdx/)
**OSDX** (全名為 **O**pen**S**earch **D**ata **X**port) 是本專案的核心工具，一個輕量級、跨平台的 .NET 8 命令列工具 (CLI)，專門用於自動化從 OpenSearch 匯出海量資料。

*   **OS**: **O**pen**S**earch
*   **D**: **D**ata (資料)
*   **X**: **X**port (匯出 / 擷取)

![OSDX Interface](./osdx/DOC/images/osdx-interface.png)

*   **海量匯出**：採用 Scroll API 機制，穩定處理 50 萬筆以上資料，不受 `max_result_window` (10,000 筆) 限制。
*   **互動式體驗 (TUI)**：搭載 `Spectre.Console` 打造美觀的終端機介面，提供引導式設定選單與即時進度條。
*   **設定檔管理**：支援多 Profile 結構，可輕鬆切換不同環境（如 Prod, Dev）與查詢條件。
*   **跨平台支援**：單一執行檔即可運行於 Windows 與 Linux，無需安裝額外 Runtime。
*   **自動化友善**：支援命令列參數調用，便於整合至 Docker 與 CronJob。

### 2. [OpenSearch 資料匯出指南](./OpenSearch_Export_Guide/)
提供多種語言與工具的匯出實務範例，涵蓋從簡單的 UI 操作到複雜的自動化腳本：

*   **實例程式碼**：包含 [.NET 8](./OpenSearch_Export_Guide/DotNet_Export_Example/) 與 [PowerShell](./OpenSearch_Export_Guide/PowerShell_Export_Example/) 的完整實作。
*   **多樣化方案**：整理了使用 `curl`, `jq`, `elasticdump`, `Logstash` 及 `Python` 的匯出教學。
*   **技術文件**：深入解析如何突破 `max_result_window` 限制及優化效能。

---

## 📂 目錄結構

```text
/
├── OpenSearch_Export_Guide/   # 各類匯出技術指南與範例腳本
│   ├── DotNet_Export_Example/ # .NET 實作範例
│   └── PowerShell_Export_Example/ # PowerShell 實作範例
└── osdx/                      # OSDX 工具原始碼
    ├── Core/                  # 核心邏輯 (Streamer, Connection)
    ├── UI/                    # TUI 互動介面
    ├── Models/                # 資料模型
    ├── Scripts/               # 測試資料注入腳本
    └── DOC/                   # OSDX 詳細設計文件
```

---

## 🚀 快速上手

### 執行 OSDX
1. 進入 `osdx` 目錄。
2. 使用 `dotnet run` 啟動互動模式：
   ```bash
   dotnet run --project osdx.csproj
   ```
3. 按照畫面引導輸入 OpenSearch 連線資訊、索引名稱及查詢條件。

### 自動化執行
指定 Profile 進行非互動式匯出：
```bash
./osdx --profile Prod-Web-Logs --config ./config.json
```

---

## 🛠️ 開發環境需求
*   [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
*   OpenSearch 測試環境 (可參考 [測試環境建置指南](./osdx/DOC/OpenSearch_Test_Environment_Setup.md))

---

## 📄 授權與文件
詳細的系統設計請參閱 [OSDX 系統設計文件](./osdx/DOC/OpenSearch_Export_System_Design.md)。
