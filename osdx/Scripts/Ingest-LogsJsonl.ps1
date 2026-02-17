$endpoint = "https://localhost:9200"
$creds = "admin:OsdxTest2026!"
$filePath = Join-Path $PSScriptRoot "logs.jsonl"

# 安全設定
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

if (-not (Test-Path $filePath)) {
    Write-Host "錯誤：找不到 $filePath" -ForegroundColor Red
    return
}

$auth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($creds))
$headers = @{ "Authorization"="Basic $auth"; "Content-Type"="application/x-ndjson" }

Write-Host "正在注入大型 logs.jsonl 資料 (約 50MB)..." -ForegroundColor Cyan

# 由於檔案較大，我們分批讀取注入以避免記憶體溢位
$batchSize = 5000
$currentBatch = New-Object System.Text.StringBuilder
$counter = 0

Get-Content $filePath -ReadCount 1000 | ForEach-Object {
    foreach ($line in $_) {
        if (-not [string]::IsNullOrWhiteSpace($line)) {
            # 修正 OpenSearch 2.x 不支援 _type 的問題
            $sanitizedLine = $line -replace ',"_type":"log"','' -replace '"_type":"log",','' -replace '"_type":"log"',''
            [void]$currentBatch.AppendLine($sanitizedLine)
            $counter++
        }
    }
    
    # 每 5000 行執行一次 Bulk 注入
    if ($counter -ge $batchSize) {
        Write-Host "正在注入批次資料 ($counter 行)..." -ForegroundColor Yellow
        try {
            Invoke-RestMethod -Method Post -Uri "$endpoint/_bulk" -Headers $headers -Body $currentBatch.ToString()
        } catch {
            Write-Host "注入失敗：$($_.Exception.Message)" -ForegroundColor Red
        }
        $currentBatch.Clear()
        $counter = 0
    }
}

# 注入剩餘資料
if ($currentBatch.Length -gt 0) {
    Write-Host "正在注入最後一組資料..." -ForegroundColor Yellow
    Invoke-RestMethod -Method Post -Uri "$endpoint/_bulk" -Headers $headers -Body $currentBatch.ToString()
}

Write-Host "`n完成！資料已成功注入。" -ForegroundColor Green
Write-Host "索引名稱請參考 logs.jsonl 內容 (通常為 logstash-2015.05.18 等)。" -ForegroundColor White
