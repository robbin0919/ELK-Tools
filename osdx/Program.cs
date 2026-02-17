using System.CommandLine;
using Spectre.Console;
using osdx.Models;

// 這裡僅展示進入點邏輯，實際實作會分佈在 Core 與 UI 資料夾中
AnsiConsole.Write(new FigletText("OSDX").Color(Color.Blue));

if (args.Length > 0)
{
    // 自動化模式 (Automation Mode)
    AnsiConsole.MarkupLine("[yellow]偵測到命令列參數，啟動自動化模式...[/]");
    // TODO: 解析參數並呼叫 DataStreamer
}
else
{
    // 引導模式 (Interactive Mode)
    AnsiConsole.MarkupLine("[green]啟動交互式引導模式...[/]");
    // TODO: 呼叫 InteractiveWizard
}
