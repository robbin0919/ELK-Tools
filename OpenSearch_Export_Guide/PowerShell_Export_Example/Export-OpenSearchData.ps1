# Export-OpenSearchData.ps1
# 使用 OpenSearch Scroll API 匯出大量資料

$baseUrl = "http://localhost:9200"
$indexName = "your_index"
$outputFile = "export_data.json"
$scrollTimeout = "2m"
$batchSize = 5000
$queryFile = "query.json"

# 如果有啟用安全性驗證，請取消以下註解並設定帳密
# $user = "admin"
# $pass = "your_password"
# $pair = "$($user):$($pass)"
# $encoded = [System.Convert]::ToBase64String([System.Text.Encoding]::ASCII.GetBytes($pair))
# $headers = @{ Authorization = "Basic $encoded" }

# 1. 準備查詢條件 (優先讀取檔案，否則預設 match_all)
if (Test-Path $queryFile) {
    $query = Get-Content -Raw $queryFile
    Write-Host "使用來自 $queryFile 的查詢條件。" -ForegroundColor Gray
} else {
    $query = @{
        size = $batchSize
        query = @{
            match_all = @{}
        }
    } | ConvertTo-Json
    Write-Host "未發現 $queryFile，將匯出所有資料。" -ForegroundColor Gray
}

Write-Host "開始從 $indexName 匯出資料至 $outputFile..." -ForegroundColor Cyan

# 2. 初始化 Scroll 查詢
$searchUri = "$baseUrl/$indexName/_search?scroll=$scrollTimeout"

# 如果有 $headers 變數，請在 Invoke-RestMethod 加入 -Headers $headers
$response = Invoke-RestMethod -Uri $searchUri -Method Post -Body $query -ContentType "application/json" # -Headers $headers
$scrollId = $response._scroll_id
$hits = $response.hits.hits

$totalExported = 0
$streamWriter = [System.IO.StreamWriter]::new((Get-Item .).FullName + "/" + $outputFile, $false, [System.Text.Encoding]::UTF8)
$streamWriter.WriteLine("[")

try {
    while ($hits.Count -gt 0) {
        foreach ($hit in $hits) {
            $jsonItem = $hit._source | ConvertTo-Json -Depth 10 -Compress
            if ($totalExported -gt 0) {
                $streamWriter.WriteLine(",")
            }
            $streamWriter.Write($jsonItem)
            $totalExported++
        }

        Write-Host "已匯出 $totalExported 筆..."
        
        # 2. 獲取下一批次
        $scrollUri = "$baseUrl/_search/scroll"
        $scrollQuery = @{
            scroll = $scrollTimeout
            scroll_id = $scrollId
        } | ConvertTo-Json

        $response = Invoke-RestMethod -Uri $scrollUri -Method Post -Body $scrollQuery -ContentType "application/json"
        $scrollId = $response._scroll_id
        $hits = $response.hits.hits
    }
}
finally {
    $streamWriter.WriteLine()
    $streamWriter.WriteLine("]")
    $streamWriter.Close()
}

# 3. 清除 Scroll 快照
Invoke-RestMethod -Uri "$baseUrl/_search/scroll" -Method Delete -Body (@{ scroll_id = @($scrollId) } | ConvertTo-Json) -ContentType "application/json" | Out-Null

Write-Host "匯出完成！總計匯出: $totalExported 筆。" -ForegroundColor Green
