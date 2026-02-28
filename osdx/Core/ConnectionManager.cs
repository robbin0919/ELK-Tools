/*
 * 檔案名稱: ConnectionManager.cs
 * 專案: OSDX (OpenSearch Data Xport)
 * 
 * 修改歷程:
 * ────────────────────────────────────────────────────────────────
 * 日期         版本    修改人員        修改說明
 * ────────────────────────────────────────────────────────────────
 * 2026-02-28   v1.3    Robbin Lee      1. 改進方法 4：使用實際查詢取代 Indices.Exists
 *                                       2. 解決只有查詢權限但無 admin 權限的驗證問題
 *                                       3. 執行 match_all 查詢以驗證 index 存取權限
 * 2026-02-28   v1.2    Robbin Lee      1. 修正 TestQuery 方法的查詢包裝問題
 *                                       2. 智能判斷完整查詢 vs 查詢條件
 *                                       3. 避免對完整查詢 DSL 進行二次包裝
 * 2026-02-28   v1.1    Robbin Lee      1. 新增多重連線驗證機制（4種方法）
 *                                       2. 新增 Index 層級驗證（方法4）
 *                                       3. 智慧型 403 錯誤診斷（區分認證失敗 vs 權限不足）
 *                                       4. 自動偵測 security_exception
 *                                       5. 修正 HttpMethod 命名空間衝突
 *                                       6. 增強錯誤訊息與日誌記錄
 * ────────────────────────────────────────────────────────────────
 */

using OpenSearch.Client;
using OpenSearch.Net;
using osdx.Models;
using System.Net;
using Serilog;
using System.Text.Json;

namespace osdx.Core;

public static class ConnectionManager
{
    public static IOpenSearchClient GetClient(ConnectionConfig config, string? password = null)
    {
        var nodes = new[] { new Uri(config.Endpoint) };
        var connectionPool = new StaticConnectionPool(nodes);
        var settings = new ConnectionSettings(connectionPool)
            .DefaultIndex(config.Index)
            .DisablePing() // 禁用首次連線的 Ping，避免因超時導致失敗
            .DisableDirectStreaming() // 開啟後可在 DebugInformation 看到完整的 Request/Response
            .RequestTimeout(TimeSpan.FromSeconds(30)); // 設定較長的超時時間

        if (!string.IsNullOrEmpty(config.Username) && !string.IsNullOrEmpty(password))
        {
            settings.BasicAuthentication(config.Username, password);
        }

        if (config.IgnoreSslErrors)
        {
            settings.ServerCertificateValidationCallback((o, certificate, chain, errors) => true);
        }

        return new OpenSearchClient(settings);
    }

    public static async Task<(bool Success, string Message)> ValidateConnectionAsync(ConnectionConfig config, string? password)
    {
        Log.Information("開始連線驗證: Endpoint={Endpoint}, User={User}, IgnoreSslErrors={IgnoreSsl}", 
            config.Endpoint, config.Username, config.IgnoreSslErrors);
        
        try
        {
            var client = GetClient(config, password);
            
            // 方法 1: 嘗試使用 Ping (HEAD /)
            Log.Debug("嘗試方法 1: Ping (HEAD /)");
            var pingResponse = await client.PingAsync();
            
            if (pingResponse.IsValid)
            {
                Log.Information("連線驗證成功 (使用 Ping)");
                return (true, "連線驗證成功 (Ping)");
            }
            
            Log.Debug("Ping 失敗 (HTTP {StatusCode}), 嘗試方法 2: 叢集健康檢查", pingResponse.ApiCall?.HttpStatusCode);
            
            // 方法 2: 嘗試 Cluster Health (GET /_cluster/health)
            var healthResponse = await client.Cluster.HealthAsync();
            
            if (healthResponse.IsValid)
            {
                Log.Information("連線驗證成功 (使用 Cluster Health): ClusterName={ClusterName}, Status={Status}",
                    healthResponse.ClusterName, healthResponse.Status);
                return (true, $"連線驗證成功\n叢集名稱: {healthResponse.ClusterName}\n狀態: {healthResponse.Status}");
            }
            
            Log.Debug("Cluster Health 失敗 (HTTP {StatusCode}), 嘗試方法 3: 根路徑", healthResponse.ApiCall?.HttpStatusCode);
            
            // 方法 3: 嘗試根路徑 (GET /)
            var rootResponse = client.LowLevel.DoRequest<DynamicResponse>(OpenSearch.Net.HttpMethod.GET, "/");
            
            if (rootResponse.Success)
            {
                Log.Information("連線驗證成功 (使用根路徑)");
                return (true, "連線驗證成功 (Root)");
            }
            
            Log.Debug("Root 失敗 (HTTP {StatusCode}), 嘗試方法 4: Index 查詢測試", rootResponse.HttpStatusCode);
            
            // 方法 4: 嘗試實際查詢目標 Index (針對無 cluster 權限但有 index 讀取權限的使用者)
            // 使用實際的 search 查詢，而不是 Exists，因為 Exists 需要 admin 權限
            if (!string.IsNullOrEmpty(config.Index))
            {
                try
                {
                    // 執行一個簡單的 match_all 查詢，size=0 只取總數
                    var searchResponse = client.LowLevel.Search<DynamicResponse>(
                        config.Index,
                        PostData.String("{\"query\":{\"match_all\":{}},\"size\":0}")
                    );
                    
                    if (searchResponse.Success)
                    {
                        Log.Information("連線驗證成功 (使用 Index 查詢): Index={Index}", config.Index);
                        return (true, $"連線驗證成功 (Index 查詢)\nIndex: {config.Index}\n您有查詢權限，可以正常使用導出功能");
                    }
                    
                    Log.Debug("Index 查詢失敗 (HTTP {StatusCode})", searchResponse.HttpStatusCode);
                }
                catch (Exception ex)
                {
                    Log.Debug("Index 查詢測試發生異常: {Message}", ex.Message);
                }
            }
            
            // 所有方法都失敗，收集詳細錯誤資訊
            Log.Warning("所有驗證方法均失敗，開始診斷...");
            
            // 使用 healthResponse 作為主要錯誤來源（通常最詳細）
            var response = healthResponse.ApiCall?.HttpStatusCode != null ? healthResponse : (IResponse)pingResponse;
            var statusCode = response.ApiCall?.HttpStatusCode;
            var exceptionMsg = response.OriginalException?.Message;
            var serverError = response.ServerError?.Error?.Reason;
            var debugInfo = response.DebugInformation;
            
            // 建構詳細錯誤訊息
            var errorDetails = new System.Text.StringBuilder();
            errorDetails.AppendLine($"HTTP 狀態碼: {statusCode}");
            
            if (statusCode == 403)
            {
                // 檢查是否為權限不足（而非認證失敗）
                bool isPermissionIssue = serverError?.Contains("no permissions", StringComparison.OrdinalIgnoreCase) == true ||
                                        serverError?.Contains("security_exception", StringComparison.OrdinalIgnoreCase) == true;
                
                if (isPermissionIssue)
                {
                    errorDetails.AppendLine("➜ 403 Forbidden - 權限不足（認證已成功）:");
                    errorDetails.AppendLine("  ✓ 帳號密碼正確，已通過認證");
                    errorDetails.AppendLine("  ✗ 使用者缺少必要的存取權限");
                    errorDetails.AppendLine("");
                    errorDetails.AppendLine("  解決方案：");
                    errorDetails.AppendLine("  1. 聯繫 OpenSearch 管理員為您的帳號授予以下權限：");
                    errorDetails.AppendLine("     - cluster:monitor/health (叢集監控)");
                    errorDetails.AppendLine("     - indices:data/read/* (索引讀取)");
                    errorDetails.AppendLine($"     - 特定 Index 的存取權限: {config.Index}");
                    errorDetails.AppendLine("  2. 或使用具有足夠權限的帳號（如 admin）");
                    errorDetails.AppendLine("  3. 檢查 OpenSearch Security Plugin 的角色映射配置");
                }
                else
                {
                    errorDetails.AppendLine("➜ 403 Forbidden - 認證或配置問題:");
                    errorDetails.AppendLine("  1. 驗證密碼是否正確（OpenSearch 密碼區分大小寫）");
                    errorDetails.AppendLine("  2. 確認帳號有基本存取權限");
                    errorDetails.AppendLine("  3. 檢查 OpenSearch 是否啟用了 Basic Authentication");
                    errorDetails.AppendLine("  4. 確認伺服器端安全性外掛程式（如 Security Plugin）配置");
                    errorDetails.AppendLine("  5. 嘗試使用管理員帳號測試（如 admin）");
                }
            }
            else if (statusCode == 401)
            {
                errorDetails.AppendLine("➜ 401 Unauthorized - 認證失敗");
                errorDetails.AppendLine("  1. 帳號或密碼錯誤");
                errorDetails.AppendLine("  2. 帳號可能已被鎖定或停用");
            }
            
            if (!string.IsNullOrEmpty(exceptionMsg))
                errorDetails.AppendLine($"\n例外訊息: {exceptionMsg}");
                
            if (!string.IsNullOrEmpty(serverError))
                errorDetails.AppendLine($"伺服器錯誤: {serverError}");
            
            // 檢查是否是 SSL 問題
            if (exceptionMsg?.Contains("SSL", StringComparison.OrdinalIgnoreCase) == true ||
                exceptionMsg?.Contains("certificate", StringComparison.OrdinalIgnoreCase) == true)
            {
                errorDetails.AppendLine("\n⚠ 偵測到 SSL 憑證問題，但 IgnoreSslErrors 已啟用");
                errorDetails.AppendLine("  可能需要檢查 .NET 的 SSL/TLS 設定");
            }

            var finalMessage = errorDetails.ToString();
            Log.Warning("連線驗證失敗:\n{Message}", finalMessage);
            
            // 記錄完整 Debug 資訊供技術人員分析
            if (!string.IsNullOrEmpty(debugInfo))
            {
                Log.Debug("Ping Response Debug:\n{DebugInfo}", pingResponse.DebugInformation);
                Log.Debug("Health Response Debug:\n{DebugInfo}", healthResponse.DebugInformation);
            }
            
            return (false, finalMessage);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "連線驗證時發生異常");
            return (false, $"連線異常: {ex.Message}\n{ex.StackTrace}");
        }
    }

    public static (bool Success, string Message) TestQuery(ConnectionConfig config, string? password, object query)
    {
        Log.Information("開始 OpenSearch 查詢測試: Endpoint={Endpoint}, Index={Index}, User={User}", 
            config.Endpoint, config.Index, config.Username);

        try
        {
            var client = GetClient(config, password);
            
            // 序列化查詢物件
            var queryJson = JsonSerializer.Serialize(query);
            
            // 判斷 query 是否已經是完整的查詢請求體（包含 query, size, sort 等頂層字段）
            // 還是只是查詢條件（需要包裝成 {"query": ...}）
            string requestBody;
            
            try
            {
                var queryObj = JsonSerializer.Deserialize<Dictionary<string, object>>(queryJson);
                
                // 如果包含 "query" 頂層字段，表示這已經是完整的查詢請求體
                if (queryObj != null && queryObj.ContainsKey("query"))
                {
                    // 直接使用原始查詢
                    requestBody = queryJson;
                    Log.Debug("偵測到完整的查詢請求體，直接使用");
                }
                else
                {
                    // 只是查詢條件，需要包裝
                    requestBody = $"{{\"query\": {queryJson}, \"size\": 0}}";
                    Log.Debug("偵測到查詢條件，包裝為完整請求體");
                }
            }
            catch
            {
                // 解析失敗，使用原始包裝方式（向後兼容）
                requestBody = $"{{\"query\": {queryJson}, \"size\": 0}}";
                Log.Debug("無法解析查詢結構，使用預設包裝方式");
            }

            var response = client.LowLevel.Search<SearchResponse<object>>(
                config.Index, 
                PostData.String(requestBody)
            );

            if (response.ApiCall.Success)
            {
                return (true, "查詢語法正確且伺服器接受。");
            }
            else
            {
                // 保持日誌中有詳細的 DebugInformation
                Log.Warning("OpenSearch 查詢語法測試失敗：{DebugInformation}", response.ApiCall.DebugInformation);
                
                // 從結果中提取對使用者友善的簡短錯誤訊息
                var friendlyMessage = response.ApiCall.OriginalException?.Message 
                                     ?? response.ServerError?.Error?.Reason 
                                     ?? $"伺服器回應錯誤 (HTTP {response.ApiCall.HttpStatusCode})";

                return (false, friendlyMessage);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "執行查詢測試時發生未預期的錯誤");
            return (false, ex.Message);
        }
    }
}
