$endpoint = "https://localhost:9200"
$creds = "admin:OsdxTest2026!"
$sourceUrl = "https://raw.githubusercontent.com/elastic/elasticsearch/v7.10.2/docs/src/test/resources/accounts.json"

# 安全協議設定
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

Write-Host "正在嘗試獲取官方銀行帳戶資料..." -ForegroundColor Cyan

$content = ""
try {
    $webClient = New-Object System.Net.WebClient
    $webClient.Headers.Add("User-Agent", "Mozilla/5.0")
    $content = $webClient.DownloadString($sourceUrl)
    Write-Host "下載成功！" -ForegroundColor Green
} catch {
    Write-Host "下載失敗 ($($_.Exception.Message))。改用本地備用方案生成資料..." -ForegroundColor Yellow
    
    $sb = New-Object System.Text.StringBuilder
    for ($i = 1; $i -le 5000; $i++) {
        $gender = if ((Get-Random -Maximum 2) -eq 0) { "M" } else { "F" }
        $item = @{
            "account_number" = $i
            "balance" = Get-Random -Minimum 1000 -Maximum 50000
            "firstname" = "TestUser"
            "lastname" = "$i"
            "age" = Get-Random -Minimum 18 -Maximum 70
            "gender" = $gender
            "address" = "123 Street No. $i"
            "employer" = "OSDX Corp"
            "email" = "user$i@example.com"
            "city" = "Taipei"
            "state" = "TW"
        }
        [void]$sb.AppendLine("{""index"":{""_index"":""bank""}}")
        [void]$sb.AppendLine((ConvertTo-Json $item -Compress))
    }
    $content = $sb.ToString()
}

$auth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($creds))

Write-Host "正在注入資料至索引: bank ..." -ForegroundColor Yellow
try {
    $headers = @{
        "Authorization"="Basic $auth"; 
        "Content-Type"="application/x-ndjson"
    }
    
    # 確保結尾有換行
    if (-not $content.EndsWith("`n")) { $content += "`n" }
    
    Invoke-RestMethod -Method Post -Uri "$endpoint/bank/_bulk" -Headers $headers -Body $content
    Write-Host "完成！已成功注入資料。請在 OSDX 中指定 Index 為 'bank' 進行匯出測試。" -ForegroundColor Green
} catch {
    Write-Host "注入失敗: $($_.Exception.Message)" -ForegroundColor Red
}
