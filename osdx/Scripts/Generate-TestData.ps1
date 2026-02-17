$endpoint = "https://localhost:9200"
$creds = "admin:OsdxTest2026!"
$headers = @{ "Content-Type" = "application/json" }

# 強制指定 TLS 1.2 以相容現代 HTTPS 連線
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
# 忽略 SSL 憑證錯誤 (針對自簽憑證)
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
Write-Host "正在生成 CICD 資料..." -ForegroundColor Cyan
$cicd = 1..1000 | % { @{ "@timestamp"=(Get-Date).AddSeconds(-$_).ToString("yyyy-MM-ddTHH:mm:ssZ"); "project"="OSDX-Core"; "step"="Build"; "status"="success" } }
Send-Bulk "test-logs" $cicd

# 2. IIS Logs (1000筆)
Write-Host "正在生成 IIS 資料..." -ForegroundColor Yellow
$iis = 1..1000 | % { @{ "@timestamp"=(Get-Date).AddSeconds(-$_).ToString("yyyy-MM-ddTHH:mm:ssZ"); "method"="GET"; "uri"="/api/v1/status"; "status"=200 } }
Send-Bulk "test-logs" $iis

# 3. Web App Logs (1000筆)
Write-Host "正在生成 Web App 資料..." -ForegroundColor Green
$app = 1..1000 | % { @{ "@timestamp"=(Get-Date).AddSeconds(-$_).ToString("yyyy-MM-ddTHH:mm:ssZ"); "level"="INFO"; "msg"="Request processed successfully" } }
Send-Bulk "test-logs" $app

Write-Host "完成！已成功注入 3000 筆資料。" -ForegroundColor Green
