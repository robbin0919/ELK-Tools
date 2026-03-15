# OSDX 發佈作業流程指南

本文件說明每次版本發佈時，從變更記錄更新到最終打包的完整標準作業流程（SOP）。

---

## 概觀

```
更新 CHANGELOG (Unreleased) → 備份 → 發佈版本 → dotnet build 驗證 → dotnet publish → zip + SHA256
```

---

## 步驟 1：補充 Unreleased 區段

在 `osdx/CHANGELOG.md` 頂部的 `## [Unreleased]` 區段，記錄本次所做的所有變更。

### 條目分類

| 類型 | 使用時機 |
|------|---------|
| `### 新增` | 全新功能 |
| `### 修正` | Bug 修補 |
| `### 改進` | 效能優化、可維護性改善 |
| `### 重構` | 不影響外部行為的內部重構 |
| `### 移除` | 移除功能或設定 |

### 格式範例

```markdown
## [Unreleased]
### 新增
- **功能名稱**：說明此功能完成的事情及其影響（相關程式：`FileName.cs`）。

### 修正
- **重要修正：問題簡述**：原本的行為 → 修正後的行為，影響的函式或模組。

### 改進
- 說明改進的具體內容與效益（相關程式：`FileName.cs`）。
```

---

## 步驟 2：備份現有檔案

> **規則：修改任何檔案前，必須先建立備份。**

備份結構依原始目錄保留，存於 `osdx/backups/<timestamp>/`。

### 備份指令（PowerShell）

```powershell
$ts = Get-Date -Format "yyyyMMdd_HHmmss"
$base = ".\osdx\backups\$ts\osdx"
New-Item -ItemType Directory -Path $base -Force | Out-Null
Copy-Item ".\osdx\CHANGELOG.md" "$base\CHANGELOG.md" -Force
Copy-Item ".\osdx\osdx.csproj"  "$base\osdx.csproj"  -Force
Write-Output "BACKUPS_DONE:$ts"
```

備份後，在 `### 備註` 中記錄備份路徑：

```markdown
### 備註
- 已備份原始檔案於 osdx/backups/20260315_005142/osdx/CHANGELOG.md、osdx/backups/20260315_005142/osdx/osdx.csproj。
```

---

## 步驟 3：發佈 Unreleased 為新版本

1. 確認語義化版本號（目前最新版 + 1 patch / minor / major）。
2. 將 `## [Unreleased]` 區段的 **實際內容** 移入新版本節，並在頂部保留空的 Unreleased 佔位。
3. 同步更新 `osdx/osdx.csproj` 的版本號。

### CHANGELOG.md 結構

```markdown
## [Unreleased]
- 待記錄的變更（尚未釋出）。

## [X.Y.Z] - YYYY-MM-DD
### 改進
- （本次改動）

### 備註
- 已備份原始檔案於 osdx/backups/<timestamp>/...
```

### osdx.csproj 版本更新

```xml
<Version>X.Y.Z</Version>
<AssemblyVersion>X.Y.Z.0</AssemblyVersion>
<FileVersion>X.Y.Z.0</FileVersion>
```

---

## 步驟 4：dotnet build 驗證

> 每次修改後**必須**執行 build，確認無編譯錯誤後再進行 publish。

```powershell
dotnet build osdx\osdx.csproj -c Release --nologo
```

**預期輸出**（成功範例）：

```
osdx 成功 (X.X 秒) → osdx\bin\Release\net8.0\win-x64\osdx.dll
在 XX 秒內建置 成功
```

**常見警告處理**：

| 警告代碼 | 原因 | 處理方式 |
|----------|------|---------|
| `IL3000` | `Assembly.Location` 在 single-file 模式下回傳空字串 | 改用 `Process.GetCurrentProcess().MainModule?.FileName ?? AppContext.BaseDirectory` |
| `CS8600` | 可能為 null 的值指派給 non-nullable 變數 | 加入 null 檢查或 `?? default` |
| `MSB3026` | 建置時目標 `.exe` 被執行中的程序鎖定 | 關閉執行中的 `osdx.exe` 再重新建置 |

---

## 步驟 5：dotnet publish（single-file 打包）

```powershell
dotnet publish .\osdx\osdx.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    /p:PublishSingleFile=true `
    -o .\osdx\publish\<VERSION>\win-x64
```

**輸出清單**：

```
osdx\publish\<VERSION>\win-x64\
  osdx.exe           ← 主要可執行檔（self-contained single-file）
  osdx.pdb           ← 偵錯符號（可選，發佈時可移除）
  appsettings.json   ← Serilog 設定（必須隨執行檔一起發佈）
```

> **重要**：`appsettings.json` 必須與 `osdx.exe` 放在同一目錄，Serilog 才能正確載入 sink 設定。  
> 若遇到 `No Serilog:Using configuration section is defined` 錯誤，請確認 `appsettings.json` 中有 `"Using": ["Serilog.Sinks.Console", "Serilog.Sinks.File"]`。

---

## 步驟 6：建立 ZIP 壓縮包與 SHA256 校驗碼

```powershell
$version = "X.Y.Z"
$zip = ".\osdx\publish\$version\osdx-$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path ".\osdx\publish\$version\win-x64\*" -DestinationPath $zip -Force
(Get-FileHash -Algorithm SHA256 $zip).Hash | Out-File ($zip + '.sha256')
Write-Output "DONE: $zip"
```

**最終產物**：

```
osdx\publish\<VERSION>\
  win-x64\                          ← 未壓縮執行目錄
  osdx-<VERSION>-win-x64.zip        ← 發佈壓縮包
  osdx-<VERSION>-win-x64.zip.sha256 ← SHA256 校驗碼
```

---

## 完整一鍵發佈指令（PowerShell）

以下指令將步驟 2 + 4 + 5 + 6 合併為單一腳本，執行前請已完成 CHANGELOG.md 更新：

```powershell
# ---- 設定版本號 ----
$VERSION = "1.5.7"
$ROOT    = ".\osdx"

# 步驟 2：備份
$ts   = Get-Date -Format "yyyyMMdd_HHmmss"
$bak  = "$ROOT\backups\$ts\osdx"
New-Item -ItemType Directory -Path $bak -Force | Out-Null
Copy-Item "$ROOT\CHANGELOG.md" "$bak\CHANGELOG.md" -Force
Copy-Item "$ROOT\osdx.csproj"  "$bak\osdx.csproj"  -Force
Write-Output "Backup → $bak"

# 步驟 4：build 驗證
dotnet build "$ROOT\osdx.csproj" -c Release --nologo
if ($LASTEXITCODE -ne 0) { Write-Error "Build Failed"; exit 1 }

# 步驟 5：publish
$pubDir = "$ROOT\publish\$VERSION\win-x64"
dotnet publish "$ROOT\osdx.csproj" -c Release -r win-x64 `
    --self-contained true /p:PublishSingleFile=true `
    -o $pubDir
if ($LASTEXITCODE -ne 0) { Write-Error "Publish Failed"; exit 1 }

# 步驟 6：zip + SHA256
$zip = "$ROOT\publish\$VERSION\osdx-$VERSION-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$pubDir\*" -DestinationPath $zip -Force
(Get-FileHash -Algorithm SHA256 $zip).Hash | Out-File ($zip + '.sha256')
Write-Output "Release Package → $zip"
Write-Output "SHA256 → $zip.sha256"
```

---

## 快速檢查清單

- [ ] `## [Unreleased]` 已補充本次所有變更內容
- [ ] 修改前已建立備份（`osdx/backups/<timestamp>/`）
- [ ] `CHANGELOG.md` 已移動 Unreleased → `[X.Y.Z] - YYYY-MM-DD`
- [ ] `osdx.csproj` `Version` / `AssemblyVersion` / `FileVersion` 已更新
- [ ] `dotnet build` 回傳 exit code 0（無錯誤）
- [ ] `dotnet publish` 輸出目錄含 `osdx.exe`、`appsettings.json`
- [ ] `osdx-X.Y.Z-win-x64.zip` 與 `.sha256` 已產生

---

## 版本命名規則（SemVer）

```
MAJOR.MINOR.PATCH
  │      │     └── Bug 修正、不影響 API 的小改動（1.5.5 → 1.5.6）
  │      └──────── 向下相容的新功能（1.5.x → 1.6.0）
  └─────────────── 重大不相容變更（1.x.x → 2.0.0）
```

---

維護者：Robbin Lee  
最後更新：2026-03-15
