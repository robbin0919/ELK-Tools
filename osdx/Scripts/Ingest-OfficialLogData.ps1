$endpoint = "https://localhost:9200"
$creds = "admin:OsdxTest2026!"

# 安全協議設定
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

$auth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($creds))
$headers = @{ "Authorization"="Basic $auth"; "Content-Type"="application/x-ndjson" }

Write-Host "正在注入官方格式 Logstash 範例日誌 (內嵌 1000 筆)..." -ForegroundColor Cyan

# 生成 1000 筆符合官方格式的 Logstash 資料
$sb = New-Object System.Text.StringBuilder
for ($i = 1; $i -le 1000; $i++) {
    $ip = "192.168.1.$((Get-Random -Minimum 1 -Maximum 254))"
    $response = (200, 404, 500, 301)[(Get-Random -Maximum 4)]
    $agent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"
    $timestamp = (Get-Date).AddMinutes(-$i).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    
    $item = @{
        "@timestamp" = $timestamp
        "clientip"   = $ip
        "request"    = "/api/v1/products/$((Get-Random -Minimum 100 -Maximum 999))"
        "response"   = $response
        "bytes"      = Get-Random -Minimum 500 -Maximum 15000
        "agent"      = $agent
        "verb"       = "GET"
    }
    [void]$sb.AppendLine("{""index"":{""_index"":""logstash-logs""}}")
    [void]$sb.AppendLine((ConvertTo-Json $item -Compress))
}

try {
    $content = $sb.ToString()
    if (-not $content.EndsWith("`n")) { $content += "`n" }
    
    Invoke-RestMethod -Method Post -Uri "$endpoint/_bulk" -Headers $headers -Body $content
    Write-Host "成功！已建立索引: logstash-logs (1000 筆)" -ForegroundColor Green
    Write-Host "完成！請在 OSDX 中指定 Index 為 'logstash-logs' 進行匯出測試。" -ForegroundColor Green
} catch {
    Write-Host "注入失敗: $($_.Exception.Message)" -ForegroundColor Red
}
