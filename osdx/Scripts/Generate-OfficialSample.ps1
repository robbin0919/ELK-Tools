$endpoint = "https://localhost:9200"
$creds = "admin:OsdxTest2026!"
$headers = @{ "Content-Type" = "application/json" }

# 安全協議與憑證忽略
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
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
        "request"   = ("GET /index.html", "POST /api/v1/data", "GET /login")[(Get-Random -Maximum 3)]
        "response"  = (200, 404, 500)[(Get-Random -Maximum 3)]
        "agent"     = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"
        "geo"       = @{ "src" = "TW"; "dest" = "US" }
    }
}

Send-Bulk "test-logs" $officialLogs
Write-Host "成功！已建立官方格式索引: test-logs" -ForegroundColor Green
