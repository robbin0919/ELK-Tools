using System.Text.Json;
using OpenSearch.Client;

namespace OpenSearchExport;

class Program
{
    static async Task Main(string[] args)
    {
        // 1. 設定連線資訊
        var node = new Uri("http://localhost:9200");
        var settings = new ConnectionSettings(node)
            // .BasicAuthentication("admin", "your_password") // 如果有啟用安全性驗證，請取消此行註解
            // .ServerCertificateValidationCallback(CertificateHandlers.DangerousAcceptAnyServerCertificate) // 如果使用 HTTPS 且是自簽憑證
            .DefaultIndex("your_index");
        var client = new OpenSearchClient(settings);

        string scrollTimeout = "2m"; // 快照存留時間
        int batchSize = 5000;       // 每批次抓取筆數
        string outputFilePath = "export_data.json";
        string queryFilePath = "query.json"; // 查詢條件檔案

        // 2. 準備查詢條件
        string queryJson = "{\"match_all\": {}}";
        if (File.Exists(queryFilePath))
        {
            queryJson = File.ReadAllText(queryFilePath);
            Console.WriteLine($"使用來自 {queryFilePath} 的查詢條件。");
        }
        else
        {
            Console.WriteLine("未發現 query.json，將匯出所有資料 (match_all)。");
        }

        // 3. 初始化 Scroll 查詢
        var searchResponse = await client.SearchAsync<dynamic>(s => s
            .Index("your_index")
            .Size(batchSize)
            .Scroll(scrollTimeout)
            .Query(q => q.Raw(queryJson)) // 使用 Raw JSON 讓使用者可控
        );

        if (!searchResponse.IsValid)
        {
            Console.WriteLine($"查詢失敗: {searchResponse.DebugInformation}");
            return;
        }

        int totalExported = 0;
        using (var outputStream = new FileStream(outputFilePath, FileMode.Create))
        using (var writer = new Utf8JsonWriter(outputStream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartArray();

            string? scrollId = searchResponse.ScrollId;

            while (searchResponse.Documents.Any())
            {
                foreach (var document in searchResponse.Documents)
                {
                    JsonSerializer.Serialize(writer, document);
                    totalExported++;
                }

                Console.WriteLine($"已匯出 {totalExported} 筆...");

                // 3. 繼續抓取下一批次
                searchResponse = await client.ScrollAsync<dynamic>(scrollTimeout, scrollId);
                
                if (!searchResponse.IsValid)
                {
                    Console.WriteLine("Scroll 過程中發生錯誤。");
                    break;
                }
                
                scrollId = searchResponse.ScrollId;
            }

            writer.WriteEndArray();
        }

        // 4. 清除 Scroll 快照 (釋放伺服器資源)
        await client.ClearScrollAsync(c => c.ScrollId(searchResponse.ScrollId));

        Console.WriteLine($"匯出完成！總計匯出: {totalExported} 筆。");
    }
}
