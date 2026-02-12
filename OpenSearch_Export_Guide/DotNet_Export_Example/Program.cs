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
            .DefaultIndex("your_index");
        var client = new OpenSearchClient(settings);

        string scrollTimeout = "2m"; // 快照存留時間
        int batchSize = 5000;       // 每批次抓取筆數
        string outputFilePath = "export_data.json";

        Console.WriteLine($"開始匯出資料至 {outputFilePath}...");

        // 2. 初始化 Scroll 查詢
        var searchResponse = await client.SearchAsync<dynamic>(s => s
            .From(0)
            .Size(batchSize)
            .Query(q => q.MatchAll())
            .Scroll(scrollTimeout) // 開啟 Scroll
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
