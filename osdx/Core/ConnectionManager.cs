using OpenSearch.Client;
using OpenSearch.Net;
using osdx.Models;
using System.Net;
using Serilog;

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
        Log.Information("開始連線驗證: Endpoint={Endpoint}, User={User}", config.Endpoint, config.Username);
        try
        {
            var client = GetClient(config, password);
            var response = await client.PingAsync();

            if (response.IsValid)
            {
                return (true, "連線驗證成功。");
            }
            else
            {
                var msg = response.OriginalException?.Message 
                          ?? response.ServerError?.Error?.Reason 
                          ?? $"連線失敗 (HTTP {response.ApiCall.HttpStatusCode})";
                Log.Warning("連線驗證失敗: {Message}", msg);
                return (false, msg);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "連線驗證時發生異常");
            return (false, ex.Message);
        }
    }

    public static (bool Success, string Message) TestQuery(ConnectionConfig config, string? password, object query)
    {
        Log.Information("開始 OpenSearch 查詢測試: Endpoint={Endpoint}, Index={Index}, User={User}", 
            config.Endpoint, config.Index, config.Username);

        try
        {
            var client = GetClient(config, password);
            
            // 使用 low-level 或 high-level search 測試語法
            // 這裡簡單使用 search 請求，並設定 size=0 僅測試語法
            var response = client.LowLevel.Search<SearchResponse<object>>(
                config.Index, 
                PostData.Serializable(new { query = query, size = 0 })
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
