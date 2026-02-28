/*
 * 檔案名稱: CustomProgressBarColumn.cs
 * 專案: OSDX (OpenSearch Data Xport)
 * 
 * 修改歷程:
 * ────────────────────────────────────────────────────────────────
 * 日期         版本    修改人員        修改說明
 * ────────────────────────────────────────────────────────────────
 * 2026-02-28   v1.0    Robbin Lee      1. 建立自訂進度條欄位類別
 *                                       2. 使用粗體 ASCII 字符（█/·）提升視覺效果
 *                                       3. 採用安全編碼字元避免終端顯示問題
 *                                       4. 支援可配置的寬度與字元樣式
 * ────────────────────────────────────────────────────────────────
 */

using Spectre.Console;
using Spectre.Console.Rendering;

namespace osdx.Core;

/// <summary>
/// 自訂進度條欄位，使用粗體 ASCII 字符提升視覺效果
/// </summary>
public class CustomProgressBarColumn : ProgressColumn
{
    public int Width { get; set; } = 50;
    public char CompletedChar { get; set; } = '█';  // 實心方塊
    public char RemainingChar { get; set; } = '·';  // 中間點（更安全的編碼）
    
    public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
    {
        var percentage = task.Percentage / 100.0;
        var completed = (int)(Width * percentage);
        var remaining = Width - completed;
        
        var completedBar = new string(CompletedChar, completed);
        var remainingBar = new string(RemainingChar, remaining);
        
        return new Markup($"[green1]{completedBar}[/][grey35]{remainingBar}[/]");
    }
}
