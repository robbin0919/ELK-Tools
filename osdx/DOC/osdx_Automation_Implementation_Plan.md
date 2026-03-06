# OSDX 自動化模式 (Automation Mode) 實作計畫書

本文件說明如何實作 **OSDX** 的自動化模式。

## 核心設計原則
> [!IMPORTANT]
> **職責分離原則**：
> 1. **指令模式 (CLI Mode)**：僅供「自動化執行」已存在的任務，不提供建立、編輯或刪除設定的功能。
> 2. **交互式模式 (TUI Mode)**：所有 Profile、連線資訊及查詢語句 (Queries) 的管理與設定，必須透過 TUI 介面進行。

---

## 1. 實作目標
*   **唯讀執行**：指令模式僅讀取 `config.json` 中的現有 Profile 並執行。
*   **參數覆蓋**：僅允許在執行時臨時覆蓋「執行期參數」（如帳號、密碼、輸出路徑）。
*   **無人值守**：支援排程工具啟動，且在出錯時能正確回傳 Exit Code。

---

## 2. 命令列指令結構設計 (Proposed CLI)

自動化模式僅包含 `export` 指令。

```bash
# 基本用法：指定已在 TUI 中設定好的 Profile 與 Query
./osdx export --profile "Prod-Logs" --query "Errors" --username "admin" --password "MySecret123"

# 縮寫形式
./osdx export -p "Prod-Logs" -q "Default" -u "robbin" -pass "xxx"
```

### 支援的執行參數：
| 參數 | 縮寫 | 說明 | 必填 |
| :--- | :--- | :--- | :--- |
| `--profile` | `-p` | 必須是已在 TUI 中建立過的 Profile 名稱 | 是 |
| `--query` | `-q` | 該 Profile 下已定義的查詢語句 | 否 (預設 "Default") |
| `--username` | `-u` | 執行時使用的帳號（覆蓋 Profile 預設值） | 否 |
| `--password` | `-pass` | 執行時使用的密碼（不建議儲存在設定檔中） | 否 |
| `--output` | `-o` | 執行時臨時指定輸出路徑 | 否 |

---

## 3. 技術實作步驟

### 步驟 1：整合 `System.CommandLine`
在 `Program.cs` 中建立指令解析器，定義上述參數選項。

### 步驟 2：解析邏輯與 Profile 載入
1.  **讀取現有設定**：載入 `config.json`。
2.  **查找 Profile**：根據 `--profile` 尋找。若該 Profile 未在 TUI 中設定過，則終止並提示「請先使用 TUI 模式建立設定檔」。
3.  **參數覆蓋**：僅針對執行所需的連線資訊進行記憶體中的暫時覆蓋，**不寫回 `config.json`**。

### 步驟 3：呼叫核心引擎
將最終確定的配置傳遞給 `DataStreamer.ExportAsync`。

---

## 4. 自動化模式特有邏輯

### 4.1 禁止配置操作
*   指令模式下不提供 `--create-profile` 或 `--add-query` 等參數。
*   若使用者輸入未定義的 Profile，程式應引導使用者：「找不到該 Profile，請執行 `./osdx` 進入引導模式進行設定」。

### 4.2 錯誤處理
*   若連線失敗，直接回傳 Exit Code 並記錄日誌。
*   不嘗試修復設定或彈出互動視窗。

---

## 5. 修改位置預估

*   **`Program.cs`**: 實作 `RootCommand`，僅包含執行邏輯。
*   **`UI/InteractiveWizard.cs`**: 保持不變，作為唯一的配置入口點。

---

## 6. 使用範例

**情境：透過 Linux Cron 執行已設定好的備份任務**
```bash
# 預先在 TUI 模式中已建立名為 "Security-Daily" 的 Profile
./osdx export --profile "Security-Daily" --password "${OS_PASS}"
```
