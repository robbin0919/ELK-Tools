# OpenSearch 資料匯出指南

本文件彙整了將 OpenSearch 查詢結果匯出成檔案（CSV, JSON 等）的幾種常見方法。

---

## 1. 使用 OpenSearch Dashboards (UI)
適合少量資料且偏好圖形化介面的使用者。

### 操作步驟：
*   **Discover 頁面**：
    1. 在 **Discover** 設定時間範圍與過濾條件。
    2. 點擊右上角的 **Share**。
    3. 選擇 **CSV reports** 進行下載。
*   **Dashboard 或 Visualization**：
    1. 點擊圖表右上角的 **Inspect** 圖示。
    2. 點擊 **Download CSV**。

---

## 2. 使用 API 與 `curl` (CLI)
適合自動化流程或開發者。

### 直接轉存 JSON：
```bash
curl -X GET "localhost:9200/your_index/_search?pretty&size=1000" -H 'Content-Type: application/json' -d'
{
  "query": { "match_all": {} }
}' > output.json
```

### 配合 `jq` 轉成 CSV：
```bash
curl -s -X GET "localhost:9200/your_index/_search" -d '{"query":{"match_all":{}}, "size":100}' | jq -r '.hits.hits[]._source | [ .field1, .field2 ] | @csv' > output.csv
```

---

## 3. 使用 Logstash
適合海量資料匯出（萬級以上）。

### 設定範例 (`export.conf`)：
```ruby
input {
  elasticsearch {
    hosts => ["localhost:9200"]
    index => "your_index"
    query => '{ "query": { "match_all": {} } }'
  }
}
output {
  csv {
    fields => ["field1", "field2", "timestamp"]
    path => "/path/to/export.csv"
  }
}
```

---

## 4. 使用第三方工具 `elasticdump`
強大的命令列工具，支援多種格式，是處理大數據量（如 50 萬筆以上）的首選。

### 為什麼推薦使用 `elasticdump` 處理大量資料？
*   **不觸及 `max_result_window` 限制**：OpenSearch 預設的一般查詢 (`from` + `size`) 限制為 10,000 筆。`elasticdump` 預設採用 **Scroll API** 機制，會在伺服器端建立快照分批抓取，因此可以完整匯出數百萬筆資料而不受此限制影響。
*   **記憶體友善**：分批處理資料，不會一次性載入所有結果，避免客戶端 OOM。

### 安裝與基本用法
*   **安裝**：`npm install -g elasticdump`
*   **匯出 JSON**：
    ```bash
    elasticdump --input=http://localhost:9200/your_index --output=data.json --type=data
    ```

### 50 萬筆以上資料的優化範例
針對大量資料，建議調整 `--limit` (批次量) 與 `--scrollTime` (快照存留時間)：
```bash
elasticdump \
  --input=http://localhost:9200/your_index \
  --output=large_data.json \
  --type=data \
  --limit=5000 \
  --scrollTime=20m
```
*   **`--limit`**：建議設為 2000 ~ 5000，可大幅提升匯出效率。
*   **`--scrollTime`**：若資料量極大，建議延長快照有效期（如 20m），避免匯出中途快照過期。

---

## 5. 使用 Python 腳本
最靈活的方式，可進行資料清洗。

```python
from opensearchpy import OpenSearch
import pandas as pd

client = OpenSearch(hosts=[{'host': 'localhost', 'port': 9200}])
query = {"query": {"match_all": {}}, "size": 1000}
response = client.search(body=query, index="your_index")

# 轉換為 Pandas DataFrame 並匯出
data = [hit['_source'] for hit in response['hits']['hits']]
df = pd.DataFrame(data)
df.to_csv("output.csv", index=False)
```

---

## 6. 使用 .NET 8 (C#) 腳本
適合在企業級環境中使用，利用 `OpenSearch.Client` (NEST 衍生版本) 的 Scroll API 處理巨量資料。

### 核心代碼範例：
```csharp
using OpenSearch.Client;

var settings = new ConnectionSettings(new Uri("http://localhost:9200")).DefaultIndex("your_index");
var client = new OpenSearchClient(settings);

string scrollTimeout = "2m";
var response = await client.SearchAsync<dynamic>(s => s
    .Size(5000)
    .Query(q => q.MatchAll())
    .Scroll(scrollTimeout)
);

while (response.Documents.Any())
{
    // 處理資料 (例如寫入檔案)
    // ...
    
    // 繼續捲動抓取
    response = await client.ScrollAsync<dynamic>(scrollTimeout, response.ScrollId);
}
```
*   **優點**：強型別支援、非同步效能優異，適合整合至現有的 .NET 後端系統。
*   **完整範例**：請參考同目錄下的 `DotNet_Export_Example` 專案。

---

## 7. 使用 PowerShell 腳本
適合 Windows 或 Linux (安裝有 PowerShell Core) 的自動化排程與維運腳本。

### 核心代碼範例：
```powershell
$scrollId = $response._scroll_id
$hits = $response.hits.hits

while ($hits.Count -gt 0) {
    # 處理資料
    foreach ($hit in $hits) { $hit._source | ConvertTo-Json -Compress | Add-Content "data.json" }

    # 繼續捲動
    $response = Invoke-RestMethod -Uri "$baseUrl/_search/scroll" -Method Post -Body (@{
        scroll = "2m"
        scroll_id = $scrollId
    } | ConvertTo-Json) -ContentType "application/json"
    $scrollId = $response._scroll_id
    $hits = $response.hits.hits
}
```
*   **優點**：無需額外 SDK (內建 `Invoke-RestMethod`)，適合快速腳本開發。
*   **完整範例**：請參考同目錄下的 `PowerShell_Export_Example` 專案。

---

## 8. 安全性與驗證 (Security & Authentication)
在正式環境中，OpenSearch 通常會啟用安全性外掛。以下是常見的驗證處理方式：

### Basic Auth 範例
*   **curl**: 使用 `-u admin:password`。
*   **Python**: `OpenSearch(hosts=..., http_auth=('admin', 'pass'))`。
*   **.NET**: `settings.BasicAuthentication("admin", "pass")`。
*   **PowerShell**: 在 Header 中加入 `Authorization: Basic [Base64]`。

### SSL/TLS 憑證
如果 OpenSearch 使用 HTTPS 且憑證是自簽的（常見於內部網路），你可能需要忽略 SSL 驗證：
*   **curl**: 加入 `-k` 或 `--insecure`。
*   **.NET**: 設定 `ServerCertificateValidationCallback`。
*   **PowerShell**: 使用 `[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }`（僅限 Windows PowerShell）或在 PowerShell Core 使用 `-SkipCertificateCheck`。

---

## 9. 自動化匯出排程的關鍵資訊清單
當您需要將上述方法開發成一個穩定、可自動運行的匯出排程時，除了基本的驗證資訊和查詢語法外，還需要一個完整的設定清單，以確保腳本的可靠性與可維護性。以下可作為開發前的準備清單：

### 1. 連線資訊 (Connection Details)
*   **主機位址 (Endpoint URL)**：OpenSearch 服務的完整 URL，例如 `https://opensearch.your-company.com:9200`。
*   **索引名稱 (Index Name)**：要查詢的目標索引，可使用萬用字元，如 `my-logs-*`。
*   **SSL/TLS 憑證處理**：若主機使用 `HTTPS` 且為自簽憑證，腳本需設定為「略過憑證驗證」。

### 2. 驗證資訊 (Authentication Details)
*   **驗證方式**：是使用基本驗證 (帳號密碼)、API Key 還是其他 Token？
*   **憑證資訊**：根據驗證方式，提供對應的帳密或金鑰字串。

### 3. 查詢與資料範圍 (Query and Data Scope)
*   **查詢語法 (Query DSL)**：定義要篩選的資料條件。
*   **要匯出的欄位 (Fields to Export)**：若格式為 `CSV`，需明確指定欄位及順序。

### 4. 輸出設定 (Output Configuration)
*   **匯出格式 (Output Format)**：`JSON` 或 `CSV`。
*   **儲存路徑 (File Path)**：匯出檔案的存放目錄。
*   **檔案命名規則 (File Naming Convention)**：建議包含動態時間戳以避免檔案覆蓋，例如 `daily_report_2026-02-17_0400.csv`。

### 5. 執行與效能參數 (Execution & Performance)
*   **匯出方式 (Export Method)**：對於大量資料，應使用 **Scroll API** 或 **Search After**。
*   **批次大小 (Batch Size / Limit)**：每次批次抓取的資料筆數（例如 `5000`），此為關鍵效能參數。
*   **逾時設定 (Timeout Settings)**：如 Scroll API 的快照存留時間 (`scrollTime`)。

### 6. 排程與監控 (Scheduling & Monitoring)
*   **執行頻率 (Schedule/Frequency)**：定義於作業系統層級（如 Linux `cron` 或 Windows `工作排程器`），例如「每天凌晨 4 點」。
*   **日誌記錄 (Logging)**：腳本應記錄詳細執行過程，以便追蹤問題。
*   **錯誤通知機制 (Error Notification)**：失敗時應主動通知維運人員（如 **Email**、**Slack/Teams**）。

---

## 注意事項

### 1. 突破 `max_result_window` 限制
在 OpenSearch/Elasticsearch 中，有一個關鍵參數 `index.max_result_window`，預設值為 **10,000**。

*   **一般分頁查詢 (`from` + `size`)**：
    當你請求的資料範圍（`from` + `size`）超過 10,000 時，系統會拋出錯誤。這是為了保護叢集效能，避免深度分頁造成過大的記憶體負擔。
*   **如何避開限制？**
    *   **Scroll API (推薦)**：如 `elasticdump` 的運作方式。它會建立一個資料一致性的快照，透過 `scroll_id` 分批迭代取回所有資料。**不受 `max_result_window` 限制**。
    *   **Search After**：另一種高效的分頁方式，適合需要排序且大數據量翻頁的場景，同樣不受 10,000 筆限制。
    *   **修改設定 (不推薦)**：雖然可以透過 API 調大 `max_result_window`，但這會增加叢集崩潰的風險，處理 50 萬筆資料時不建議採用此法。

### 2. 效能與資源建議
*   **執行環境**：處理 50 萬筆資料時，建議在與 OpenSearch 同網段或本機執行匯出，以減少網路延遲。
*   **磁碟空間**：50 萬筆資料轉為 JSON 後體積可能相當大，請預先確認目標目錄的磁碟剩餘空間。
*   **避開尖峰時段**：大量資料匯出會佔用 IO 與 CPU 資源，建議在業務離峰時段進行。

### 3. 關於 `query` 查詢內容
本文件中的許多範例使用了 `{"query": { "match_all": {} }}`，這是一個表示「匹配所有文件」的基礎查詢。在實際應用中，您需要根據想匯出的特定資料，自行準備或修改這段 `query` 內容。

這意味著您需要熟悉 OpenSearch 的 [查詢語法 (Query DSL)](https://opensearch.org/docs/latest/opensearch/query-dsl/index/)。

簡單來說，您會透過以下幾種方式定義查詢：
*   **圖形化介面 (GUI)**：在 OpenSearch Dashboards 中，透過設定時間範圍、篩選條件來動態產生查詢。
*   **命令列/API (CLI/API)**：直接提供符合 Query DSL 語法的 JSON 結構。
*   **腳本與程式碼 (Scripts/Code)**：利用各語言的客戶端函式庫 (Client Library) 提供的方法來建構查詢物件。
*   **工具 (Tools)**：像 `elasticdump` 等工具，通常也支援傳入查詢參數來篩選特定資料。
