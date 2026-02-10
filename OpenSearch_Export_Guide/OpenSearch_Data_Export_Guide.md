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
