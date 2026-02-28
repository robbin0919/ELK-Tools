################################################################################
# 檔案名稱: Test-OpenSearchConnection.ps1
# 專案: OSDX (OpenSearch Data Xport)
# 用途: 直接使用 PowerShell 測試 OpenSearch 連線，排除應用程式邏輯問題
#
# 修改歷程:
# ──────────────────────────────────────────────────────────────────────────
# 日期         版本    修改人員        修改說明
# ──────────────────────────────────────────────────────────────────────────
# 2026-02-28   v1.0    Robbin Lee      1. 初始版本建立
#                                       2. 支援三種連線測試（HEAD /, GET /_cluster/health, GET /）
#                                       3. 實作 SSL 憑證驗證忽略功能
#                                       4. 提供詳細的錯誤診斷資訊
#                                       5. 顯示 OpenSearch 叢集基本資訊
# ──────────────────────────────────────────────────────────────────────────
################################################################################

param(
    [Parameter(Mandatory=$true)]
    [string]$Endpoint = "https://172.17.34.184:9270",
    
    [Parameter(Mandatory=$true)]
    [string]$Username,
    
    [Parameter(Mandatory=$true)]
    [string]$Password
)

# 忽略 SSL 憑證驗證（僅用於測試環境）
if (-not ([System.Management.Automation.PSTypeName]'ServerCertificateValidationCallback').Type) {
    $certCallback = @"
    using System;
    using System.Net;
    using System.Net.Security;
    using System.Security.Cryptography.X509Certificates;
    public class ServerCertificateValidationCallback
    {
        public static void Ignore()
        {
            if(ServicePointManager.ServerCertificateValidationCallback == null)
            {
                ServicePointManager.ServerCertificateValidationCallback += 
                    delegate
                    (
                        Object obj, 
                        X509Certificate certificate, 
                        X509Chain chain, 
                        SslPolicyErrors errors
                    )
                    {
                        return true;
                    };
            }
        }
    }
"@
    Add-Type $certCallback
}
[ServerCertificateValidationCallback]::Ignore()

# 建立 Basic Auth Header
$base64AuthInfo = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(("{0}:{1}" -f $Username, $Password)))
$headers = @{
    "Authorization" = "Basic $base64AuthInfo"
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "OpenSearch 連線測試" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Endpoint: $Endpoint" -ForegroundColor Yellow
Write-Host "Username: $Username" -ForegroundColor Yellow
Write-Host "Password: $(if ($Password.Length -gt 0) { '*' * $Password.Length } else { '(空)' })" -ForegroundColor Yellow
Write-Host ""

# 測試 1: HEAD /
Write-Host "[測試 1] HEAD / (Ping)" -ForegroundColor Green
try {
    $response1 = Invoke-WebRequest -Uri "$Endpoint/" -Method HEAD -Headers $headers -UseBasicParsing -ErrorAction Stop
    Write-Host "  ✓ 成功 - HTTP $($response1.StatusCode)" -ForegroundColor Green
} catch {
    Write-Host "  ✗ 失敗 - $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.Response) {
        Write-Host "    HTTP 狀態碼: $([int]$_.Exception.Response.StatusCode)" -ForegroundColor Red
    }
}

Write-Host ""

# 測試 2: GET /_cluster/health
Write-Host "[測試 2] GET /_cluster/health" -ForegroundColor Green
try {
    $response2 = Invoke-WebRequest -Uri "$Endpoint/_cluster/health" -Method GET -Headers $headers -UseBasicParsing -ErrorAction Stop
    $health = $response2.Content | ConvertFrom-Json
    Write-Host "  ✓ 成功 - HTTP $($response2.StatusCode)" -ForegroundColor Green
    Write-Host "    Cluster Name: $($health.cluster_name)" -ForegroundColor Cyan
    Write-Host "    Status: $($health.status)" -ForegroundColor Cyan
    Write-Host "    Nodes: $($health.number_of_nodes)" -ForegroundColor Cyan
} catch {
    Write-Host "  ✗ 失敗 - $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.Response) {
        Write-Host "    HTTP 狀態碼: $([int]$_.Exception.Response.StatusCode)" -ForegroundColor Red
    }
}

Write-Host ""

# 測試 3: GET /
Write-Host "[測試 3] GET / (Root)" -ForegroundColor Green
try {
    $response3 = Invoke-WebRequest -Uri "$Endpoint/" -Method GET -Headers $headers -UseBasicParsing -ErrorAction Stop
    $root = $response3.Content | ConvertFrom-Json
    Write-Host "  ✓ 成功 - HTTP $($response3.StatusCode)" -ForegroundColor Green
    Write-Host "    Name: $($root.name)" -ForegroundColor Cyan
    Write-Host "    Version: $($root.version.number)" -ForegroundColor Cyan
    Write-Host "    Distribution: $($root.version.distribution)" -ForegroundColor Cyan
} catch {
    Write-Host "  ✗ 失敗 - $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.Response) {
        Write-Host "    HTTP 狀態碼: $([int]$_.Exception.Response.StatusCode)" -ForegroundColor Red
        
        # 嘗試讀取錯誤回應內容
        try {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $responseBody = $reader.ReadToEnd()
            Write-Host "    Response Body: $responseBody" -ForegroundColor Red
        } catch {}
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "測試完成" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
