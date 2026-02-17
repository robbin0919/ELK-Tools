# OpenSearch 測試環境建置指南

本文件說明如何快速建立一個相容於 **OSDX** 測試需求的 OpenSearch 環境。

---

## 1. 使用 Docker 快速啟動 (建議)

透過 Docker 可以在幾分鐘內建立一個單節點的 OpenSearch 伺服器，並包含基本的 Basic Auth 驗證。

### 啟動指令 (Bash/Linux)
在終端機執行以下指令：

```bash
docker run -d \
  -p 9200:9200 -p 9600:9600 \
  -e "discovery.type=single-node" \
  -e "OPENSEARCH_INITIAL_ADMIN_PASSWORD=OsdxTest2026!" \
  --name osdx-test \
  opensearchproject/opensearch:latest
```

### 啟動指令 (Windows CMD)
在命令提示字元執行以下指令：

```cmd
docker run -d ^
  -p 9200:9200 -p 9600:9600 ^
  -e "discovery.type=single-node" ^
  -e "OPENSEARCH_INITIAL_ADMIN_PASSWORD=OsdxTest2026!" ^
  --name osdx-test ^
  opensearchproject/opensearch:latest
```

*   **Endpoint**: `https://localhost:9200`
*   **Username**: `admin`
*   **Password**: `OsdxTest2026!`
*   **注意**: 預設使用自簽憑證，請在 OSDX 系統設定中開啟 `Ignore SSL Errors`。

---

## 2. 注入測試資料 (Dummy Data)

為了測試匯出功能，我們需要先在 OpenSearch 中建立索引並寫入一些資料。

### 使用 `curl` 寫入樣例資料 (Windows CMD)
```cmd
curl -XPOST "https://localhost:9200/test-logs/_doc/1" -u "admin:OsdxTest2026!" -k -H "Content-Type: application/json" -d "{\"@timestamp\": \"2026-02-17T20:00:00Z\", \"user\": \"alice\", \"action\": \"login\", \"status\": \"success\"}"

curl -XPOST "https://localhost:9200/test-logs/_doc/2" -u "admin:OsdxTest2026!" -k -H "Content-Type: application/json" -d "{\"@timestamp\": \"2026-02-17T21:00:00Z\", \"user\": \"bob\", \"action\": \"logout\", \"status\": \"error\"}"
```

### 使用 `curl` 寫入樣例資料 (Bash/Linux)
```bash
curl -XPOST "https://localhost:9200/test-logs/_doc/1" \
  -u "admin:OsdxTest2026!" -k \
  -H "Content-Type: application/json" \
  -d'
{
  "@timestamp": "2026-02-17T20:00:00Z",
  "user": "alice",
  "action": "login",
  "status": "success"
}'
```

---

## 3. 在 OSDX 工具中進行驗證流程

請依照以下順序在 OSDX 介面中操作以完成整合測試：

1.  **系統設定 (全局)**：
    *   進入 `4. 系統設定 (SSL 驗證等)`。
    *   選擇 `1. 切換全局 SSL 忽略狀態`，將其設為 `True`。
    *   返回主選單。

2.  **建立設定檔**：
    *   進入 `3. 管理設定檔 (編輯/建立/刪除)`。
    *   選擇 `[[建立新設定檔]]`。
    *   **URL**: `https://localhost:9200`
    *   **Index**: `test-logs`
    *   **Username**: `admin`
    *   **Name**: `Local-Test`
    *   返回主選單。

3.  **連線與匯出**：
    *   進入 `1. 連線資訊選擇 (切換目標)`。
    *   選擇 `Local-Test`。
    *   輸入帳號 `admin` (或按 Enter 沿用預設) 與密碼 `OsdxTest2026!`。
    *   顯示 `連線就緒 (Connection Ready)` 後返回。
    *   進入 `2. 開始執行資料導出`。
    *   挑選查詢語句（預設為 `Default`）。
    *   **觀察進度條**，確認匯出完成。

4.  **檢查結果**：
    *   查看程式目錄下的 `exports/` 資料夾。
    *   確認產生的 CSV/JSON 檔案內容是否正確。

---

## 4. 常用管理指令

*   **查看 OpenSearch 狀態**: `curl -XGET "https://localhost:9200" -u "admin:OsdxTest2026!" -k`
*   **查看索引清單**: `curl -XGET "https://localhost:9200/_cat/indices?v" -u "admin:OsdxTest2026!" -k`
*   **停止測試容器**: `docker stop osdx-test`
*   **刪除測試容器**: `docker rm osdx-test`

---

## 5. 大數據測試範例 (大量注入)

若要測試 Scroll API 的分批匯出與進度條功能，可使用 PowerShell 注入 3000 筆具備不同業務情境的資料。

### 資料注入腳本 (`Generate-TestData.ps1`)
```powershell
$endpoint = "https://localhost:9200"
$creds = "admin:OsdxTest2026!"
$headers = @{ "Content-Type" = "application/json" }
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

function Send-Bulk {
    param($index, $data)
    $bulkData = ""
    foreach ($item in $data) {
        $bulkData += "{ ""index"": { ""_index"": ""$index"" } }`n"
        $bulkData += (ConvertTo-Json $item -Compress) + "`n"
    }
    $auth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($creds))
    $headers["Authorization"] = "Basic $auth"
    Invoke-RestMethod -Method Post -Uri "$endpoint/_bulk" -Headers $headers -Body $bulkData
}

# 1. CICD Logs (1000筆)
$cicd = 1..1000 | % { @{ "@timestamp"=(Get-Date).AddSeconds(-$_).ToString("yyyy-MM-ddTHH:mm:ssZ"); "project"="OSDX"; "step"="Build"; "status"="success" } }
Send-Bulk "cicd-logs" $cicd

# 2. IIS Logs (1000筆)
$iis = 1..1000 | % { @{ "@timestamp"=(Get-Date).AddSeconds(-$_).ToString("yyyy-MM-ddTHH:mm:ssZ"); "method"="GET"; "uri"="/api/v1"; "status"=200 } }
Send-Bulk "iis-logs" $iis

# 3. Web App Logs (1000筆)
$app = 1..1000 | % { @{ "@timestamp"=(Get-Date).AddSeconds(-$_).ToString("yyyy-MM-ddTHH:mm:ssZ"); "level"="INFO"; "msg"="Processing..." } }
Send-Bulk "webapp-logs" $app
```

執行後即可在 OSDX 中指定 `cicd-logs`, `iis-logs` 或 `webapp-logs` 作為目標 Index 進行匯出測試。

---

## 6. 官方格式模擬 (Web Logs)

此範例模擬 OpenSearch 官方最著名的「Sample Web Logs」格式，包含 `clientip`, `request`, `geo` 等複雜欄位與巢狀結構，適合測試欄位篩選功能。

### 官方格式注入腳本 (`Generate-OfficialSample.ps1`)
```powershell
$endpoint = "https://localhost:9200"
$creds = "admin:OsdxTest2026!"
$headers = @{ "Content-Type" = "application/json" }
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

function Send-Bulk {
    param($index, $data)
    $bulkData = ""
    foreach ($item in $data) {
        $bulkData += "{ ""index"": { ""_index"": ""$index"" } }`n"
        $bulkData += (ConvertTo-Json $item -Compress) + "`n"
    }
    $auth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($creds))
    $headers["Authorization"] = "Basic $auth"
    Invoke-RestMethod -Method Post -Uri "$endpoint/_bulk" -Headers $headers -Body $bulkData
}

Write-Host "正在生成官方格式模擬資料 (Web Logs)..." -ForegroundColor Cyan

$officialLogs = 1..1000 | ForEach-Object {
    @{
        "timestamp" = (Get-Date).AddMinutes(-$_).ToString("yyyy-MM-ddTHH:mm:ssZ")
        "clientip"  = "192.168.1.$((Get-Random -Minimum 1 -Maximum 254))"
        "request"   = ("GET /index.html", "POST /api/v1/data", "GET /login")[(Get-Random % 3)]
        "response"  = (200, 404, 500)[(Get-Random % 3)]
        "agent"     = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"
        "geo"       = @{ "src" = "TW"; "dest" = "US" }
    }
}

Send-Bulk "opensearch_dashboards_sample_data_logs" $officialLogs
Write-Host "成功！已建立官方格式索引: opensearch_dashboards_sample_data_logs" -ForegroundColor Green
```

---

## 7. 官方真實資料匯入 (Bank Accounts)

若您需要 100% 官方標準的測試資料（常用於官方 Getting Started 教學），可以使用此腳本匯入 1000 筆真實的銀行帳戶資料。

### 官方銀行資料注入腳本 (`Ingest-OfficialBankData.ps1`)
```powershell
$endpoint = "https://localhost:9200"
$creds = "admin:OsdxTest2026!"
$sourceUrl = "https://raw.githubusercontent.com/elastic/elasticsearch/master/docs/src/test/resources/accounts.json"
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

Write-Host "下載官方資料中..."
$rawData = Invoke-WebRequest -Uri $sourceUrl -UseBasicParsing
$auth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($creds))
Invoke-RestMethod -Method Post -Uri "$endpoint/bank/_bulk" -Headers @{"Authorization"="Basic $auth"; "Content-Type"="application/x-ndjson"} -Body $rawData.Content
Write-Host "完成！請在 OSDX 中指定 Index 為 'bank' 進行匯出測試。"
```
