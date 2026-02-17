using OpenSearch.Client;
using OpenSearch.Net;
using osdx.Models;
using System.Net;

namespace osdx.Core;

public static class ConnectionManager
{
    public static IOpenSearchClient GetClient(ConnectionConfig config, string? password = null)
    {
        var nodes = new[] { new Uri(config.Endpoint) };
        var connectionPool = new StaticConnectionPool(nodes);
        var settings = new ConnectionSettings(connectionPool)
            .DefaultIndex(config.Index);

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

    public static (bool Success, string Message) TestQuery(ConnectionConfig config, string? password, object query)
    {
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
                return (false, $"伺服器回傳錯誤: {response.ApiCall.DebugInformation}");
            }
        }
        catch (Exception ex)
        {
            return (false, $"連線或執行出錯: {ex.Message}");
        }
    }
}
