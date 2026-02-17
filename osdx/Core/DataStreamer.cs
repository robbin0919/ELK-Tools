using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OpenSearch.Client;
using osdx.Models;
using Spectre.Console;
using Serilog;

namespace osdx.Core;

public static class DataStreamer
{
    public static async Task ExportAsync(ConnectionConfig connection, ExportConfig export, object query, string? password)
    {
        Log.Information("啟動資料導出任務: Index={Index}, Format={Format}", connection.Index, export.Format);

        var client = ConnectionManager.GetClient(connection, password);
        var stopwatch = Stopwatch.StartNew();
        
        // 確保輸出目錄存在
        if (!Directory.Exists(export.OutputPath))
        {
            Directory.CreateDirectory(export.OutputPath);
            Log.Information("建立輸出目錄: {Path}", export.OutputPath);
        }

        string safeIndexName = SanitizeFileName(connection.Index);
        string fileName = $"export_{safeIndexName}_{DateTime.Now:yyyyMMdd_HHmmss}.{export.Format.ToLower()}";
        string filePath = Path.Combine(export.OutputPath, fileName);
        
        long totalExported = 0;
        bool isFirstBatch = true;

        try
        {
            await AnsiConsole.Progress()
                .Columns(new ProgressColumn[] 
                {
                    new TaskDescriptionColumn(),    // 任務描述
                    new ProgressBarColumn(),        // 進度條
                    new PercentageColumn(),         // 百分比
                    new RemainingTimeColumn(),      // 預估剩餘時間
                    new SpinnerColumn(),            // 旋轉動畫
                })
                .StartAsync(async ctx =>
                {
                    var exportTask = ctx.AddTask($"[green]正在從 {connection.Index} 匯出資料...[/]");
                    
                    using var streamWriter = new StreamWriter(filePath, false, Encoding.UTF8);

                    // 1. Initial Search (建立 Scroll 快照)
                    var queryJson = JsonSerializer.Serialize(query);
                    var searchResponse = await client.SearchAsync<Dictionary<string, object>>(s => s
                        .Index(connection.Index)
                        .From(0)
                        .Size(export.BatchSize)
                        .Scroll(export.ScrollTimeout)
                        .Query(q => q.Raw(queryJson))
                        .Source(src => export.Fields.Length > 0 ? src.Includes(f => f.Fields(export.Fields)) : src.IncludeAll())
                    );

                    if (!searchResponse.IsValid)
                    {
                        Log.Error("初始查詢失敗: {DebugInformation}", searchResponse.DebugInformation);
                        var msg = searchResponse.OriginalException?.Message ?? searchResponse.ServerError?.Error?.Reason ?? "伺服器回應錯誤";
                        throw new Exception(msg);
                    }

                    var scrollId = searchResponse.ScrollId;
                    var hits = searchResponse.Hits;

                    // 更新總筆數預估 (若 OpenSearch 有回傳總數)
                    exportTask.MaxValue = searchResponse.Total > 0 ? (double)searchResponse.Total : 1000000; 

                    while (hits.Any())
                    {
                        // 2. 寫入目前批次資料
                        await WriteBatchAsync(streamWriter, hits, export, isFirstBatch);
                        
                        totalExported += hits.Count;
                        exportTask.Increment(hits.Count);
                        isFirstBatch = false;

                        // 3. 繼續抓取下一批
                        var scrollResponse = await client.ScrollAsync<Dictionary<string, object>>(export.ScrollTimeout, scrollId);
                        
                        if (!scrollResponse.IsValid)
                        {
                            Log.Error("Scroll 批次抓取失敗: {Error}", scrollResponse.DebugInformation);
                            var msg = scrollResponse.OriginalException?.Message ?? scrollResponse.ServerError?.Error?.Reason ?? "批次抓取失敗";
                            throw new Exception(msg);
                        }

                        scrollId = scrollResponse.ScrollId;
                        hits = scrollResponse.Hits;
                    }

                    // 4. 清理 Scroll 資源
                    await client.ClearScrollAsync(c => c.ScrollId(scrollId));
                    exportTask.Value = exportTask.MaxValue; // 完成進度條
                });

            stopwatch.Stop();
            Log.Information("導出完成: 總計 {Total} 筆, 耗時 {Duration}", totalExported, stopwatch.Elapsed);

            // 顯示最終報告
            var summary = new Table().Border(TableBorder.Rounded);
            summary.AddColumn("項目");
            summary.AddColumn("結果");
            summary.AddRow("導出檔案", $"[cyan]{filePath}[/]");
            summary.AddRow("總筆數", $"[yellow]{totalExported:N0}[/]");
            summary.AddRow("總耗時", $"[yellow]{stopwatch.Elapsed:mm\\:ss}[/]");
            summary.AddRow("檔案大小", $"[yellow]{new FileInfo(filePath).Length / 1024.0 / 1024.0:F2} MB[/]");

            AnsiConsole.Write(new Panel(summary).Header("[bold green] 導出成功 (Export Completed) [/]").BorderColor(Color.Green));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "導出過程中發生錯誤");
            AnsiConsole.MarkupLine($"[bold red]❌ 導出失敗：{Markup.Escape(ex.Message)}[/]");
            if (File.Exists(filePath))
            {
                AnsiConsole.MarkupLine($"[grey]提示: 部分已匯出的資料保存在 {filePath}[/]");
            }
        }
    }

    private static async Task WriteBatchAsync(StreamWriter writer, IReadOnlyCollection<IHit<Dictionary<string, object>>> hits, ExportConfig export, bool isFirstBatch)
    {
        if (export.Format.ToLower() == "csv")
        {
            await WriteCsvBatchAsync(writer, hits, export.Fields, isFirstBatch);
        }
        else
        {
            await WriteJsonBatchAsync(writer, hits, isFirstBatch);
        }
    }

    private static async Task WriteCsvBatchAsync(StreamWriter writer, IReadOnlyCollection<IHit<Dictionary<string, object>>> hits, string[] requestedFields, bool isFirstBatch)
    {
        // 若未指定 Fields，則從第一筆資料動態偵測 Headers
        var headers = requestedFields.Length > 0 ? requestedFields : hits.First().Source.Keys.ToArray();

        if (isFirstBatch)
        {
            await writer.WriteLineAsync(string.Join(",", headers));
        }

        foreach (var hit in hits)
        {
            var values = headers.Select(h => 
            {
                if (hit.Source.TryGetValue(h, out var val) && val != null)
                {
                    var str = val.ToString() ?? "";
                    // 處理 CSV 轉義: 若包含逗號、雙引號或換行，則用雙引號包起來並轉義雙引號
                    if (str.Contains(",") || str.Contains("\"") || str.Contains("\n") || str.Contains("\r"))
                    {
                        return $"\"{str.Replace("\"", "\"\"")}\"";
                    }
                    return str;
                }
                return "";
            });
            await writer.WriteLineAsync(string.Join(",", values));
        }
    }

    private static async Task WriteJsonBatchAsync(StreamWriter writer, IReadOnlyCollection<IHit<Dictionary<string, object>>> hits, bool isFirstBatch)
    {
        foreach (var hit in hits)
        {
            var json = JsonSerializer.Serialize(hit.Source);
            await writer.WriteLineAsync(json);
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new StringBuilder();
        foreach (var c in fileName)
        {
            sanitized.Append(invalidChars.Contains(c) ? '_' : c);
        }
        return sanitized.ToString();
    }
}
