/*
 * 檔案名稱: DataStreamer.cs
 * 專案: OSDX (OpenSearch Data Xport)
 * 
 * 修改歷程:
 * ────────────────────────────────────────────────────────────────
 * 日期         版本    修改人員        修改說明
 * ────────────────────────────────────────────────────────────────
 * 2026-02-28   v1.3.4  Robbin Lee      1. 進度條時間顯示改為已執行時間（ElapsedTimeColumn）
 *                                       2. 移除剩餘時間預估，顯示實際執行時長
 * 2026-02-28   v1.3.3  Robbin Lee      1. 改用粗體 ASCII 字符（█/·）取代細線進度條
 *                                       2. 建立 CustomProgressBarColumn 自訂欄位
 *                                       3. 大幅提升進度條視覺辨識度
 * 2026-02-28   v1.3.2  Robbin Lee      1. 進度條加寬至 50 字元並添加彩色樣式
 *                                       2. 新增傳輸速度顯示
 *                                       3. 優化進度條視覺效果（綠色/灰色配色）
 * 2026-02-28   v1.3.1  Robbin Lee      1. 優化進度條顯示（加寬至40字元，新增已下載筆數）
 *                                       2. 修正 LowLevel API 的 Scroll 參數傳遞方式
 *                                       3. 使用 QueryString 參數正確處理 TimeSpan 轉換
 * 2026-02-28   v1.3    Robbin Lee      1. 實現智能查詢包裝機制
 *                                       2. 完整 DSL 使用 LowLevel API 直接發送
 *                                       3. 簡單查詢條件使用 High-Level API 包裝
 *                                       4. 修正資料導出時的查詢雙重包裝問題
 * ────────────────────────────────────────────────────────────────
 */

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OpenSearch.Client;
using OpenSearch.Net;
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
                .AutoRefresh(true)
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(new ProgressColumn[] 
                {
                    new TaskDescriptionColumn(),                                    // 任務描述
                    new CustomProgressBarColumn()                                   // 自訂粗體進度條
                    { 
                        Width = 50,
                        CompletedChar = '█',                                        // 使用實心方塊
                        RemainingChar = '·'                                         // 使用中間點（編碼更安全）
                    },
                    new PercentageColumn() { Style = new Style(Color.Cyan1) },     // 百分比
                    new DownloadedColumn(),                                         // 已下載筆數
                    new TransferSpeedColumn(),                                      // 速度
                    new ElapsedTimeColumn() { Style = new Style(Color.Blue) },     // 已執行時間
                })
                .StartAsync(async ctx =>
                {
                    var exportTask = ctx.AddTask($"[bold cyan]正在從 {connection.Index} 匯出資料[/]");
                    
                    using var streamWriter = new StreamWriter(filePath, false, Encoding.UTF8);

                    // 1. Initial Search (建立 Scroll 快照)
                    var queryJson = JsonSerializer.Serialize(query);
                    
                    // 智能查詢包裝：判斷是完整 DSL 還是簡單查詢條件
                    ISearchResponse<Dictionary<string, object>> searchResponse;
                    
                    try
                    {
                        var queryObj = JsonSerializer.Deserialize<Dictionary<string, object>>(queryJson);
                        
                        // 檢查是否為完整 DSL（包含 "query" 頂層字段）
                        if (queryObj != null && queryObj.ContainsKey("query"))
                        {
                            // 完整 DSL：使用 LowLevel API 直接發送
                            Log.Debug("偵測到完整 DSL 查詢，使用 LowLevel API 直接發送");
                            
                            // 構建完整請求體，加入 scroll、size、_source 等參數
                            var fullRequest = new Dictionary<string, object>(queryObj);
                            if (!fullRequest.ContainsKey("size"))
                                fullRequest["size"] = export.BatchSize;
                            
                            if (export.Fields.Length > 0)
                            {
                                fullRequest["_source"] = export.Fields;
                            }
                            
                            var fullRequestJson = JsonSerializer.Serialize(fullRequest);
                            
                            // 使用 LowLevel API，將 scroll 作為查詢字符串參數傳遞
                            var lowLevelResponse = client.LowLevel.Search<SearchResponse<Dictionary<string, object>>>(
                                connection.Index,
                                PostData.String(fullRequestJson),
                                new SearchRequestParameters { QueryString = new Dictionary<string, object> { { "scroll", export.ScrollTimeout } } }
                            );
                            
                            searchResponse = lowLevelResponse;
                        }
                        else
                        {
                            // 簡單查詢條件：使用 High-Level API 自動包裝
                            Log.Debug("偵測到簡單查詢條件，使用 High-Level API 包裝");
                            
                            searchResponse = await client.SearchAsync<Dictionary<string, object>>(s => s
                                .Index(connection.Index)
                                .From(0)
                                .Size(export.BatchSize)
                                .Scroll(export.ScrollTimeout)
                                .Query(q => q.Raw(queryJson))
                                .Source(src => export.Fields.Length > 0 ? src.Includes(f => f.Fields(export.Fields)) : src.IncludeAll())
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "查詢結構解析失敗，使用預設 High-Level API 包裝");
                        
                        // 解析失敗時回退到原始方法
                        searchResponse = await client.SearchAsync<Dictionary<string, object>>(s => s
                            .Index(connection.Index)
                            .From(0)
                            .Size(export.BatchSize)
                            .Scroll(export.ScrollTimeout)
                            .Query(q => q.Raw(queryJson))
                            .Source(src => export.Fields.Length > 0 ? src.Includes(f => f.Fields(export.Fields)) : src.IncludeAll())
                        );
                    }

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
        // 標頭處理
        string[] headers;
        if (requestedFields.Length > 0)
        {
            headers = requestedFields;
        }
        else
        {
            // 進階標頭偵測：掃描本批次所有資料，收集所有出現過的 Key (確保異質資料欄位不遺漏)
            headers = hits.SelectMany(h => h.Source.Keys).Distinct().ToArray();
        }

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
                    // 更強韌的轉字串邏輯
                    string str;
                    if (val is string s) 
                    {
                        str = s;
                    }
                    else if (val is DateTime dt)
                    {
                        str = dt.ToString("yyyy-MM-ddTHH:mm:ssZ");
                    }
                    else if (val is JsonElement je)
                    {
                        str = je.ValueKind == JsonValueKind.String ? je.GetString() ?? "" : je.GetRawText();
                    }
                    else 
                    {
                        // 處理數值或其它物件
                        str = val.ToString() ?? "";
                    }

                    // 處理 CSV 轉義
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
