<#
backup-preserve-structure.ps1
用途：在修改檔案前備份指定檔案，備份位置為 <project>/backups/<timestamp>/<原始相對路徑>

範例：
  powershell -NoProfile -ExecutionPolicy Bypass -File .\osdx\Scripts\backup-preserve-structure.ps1 -Files 'Program.cs','UI\InteractiveWizard.cs','Core\QueryHelper.cs'

假定腳本放在專案的 `osdx\Scripts` 下，預設會以 `osdx` 作為 ProjectRoot（即把備份放到 osdx\backups）。
#>

param(
    [Parameter(Position=0, Mandatory=$false)]
    [string[]]$Files,

    [Parameter(Position=1, Mandatory=$false)]
    [string]$ProjectRoot
)

# 解析腳本路徑與預設專案根目錄（Scripts 的父目錄）
$ScriptPath = $MyInvocation.MyCommand.Definition
$ScriptDir = Split-Path -Parent $ScriptPath

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Split-Path -Parent $ScriptDir
}

try {
    $ProjectRoot = (Resolve-Path $ProjectRoot).ProviderPath
} catch {
    Write-Error "無法解析 ProjectRoot: $ProjectRoot"
    exit 2
}

$ts = Get-Date -Format 'yyyyMMdd_HHmmss'
$BackupsRoot = Join-Path -Path $ProjectRoot -ChildPath "backups\$ts"

if (-not $Files -or $Files.Count -eq 0) {
    Write-Host "Please specify files to backup (relative to project root or absolute)."
    Write-Host "Example: -Files 'Program.cs' 'UI\\InteractiveWizard.cs' 'Core\\QueryHelper.cs'"
    exit 1
}

$created = @()

foreach ($f in $Files) {
    # 嘗試解析來源路徑：先試直接路徑，再試以 ProjectRoot 組合
    $source = $null
    if (Test-Path $f) {
        $source = (Resolve-Path $f).ProviderPath
    } else {
        $cand = Join-Path $ProjectRoot $f
        if (Test-Path $cand) {
            $source = (Resolve-Path $cand).ProviderPath
        } else {
            Write-Warning "File not found: $f (skipping)"
            continue
        }
    }

    # 計算相對路徑（若來源在 ProjectRoot 底下），否則放到 external 下並 sanitize
    $normalizedProjectRoot = $ProjectRoot.TrimEnd('\','/') + '\'
    if ($source.StartsWith($normalizedProjectRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        $rel = $source.Substring($normalizedProjectRoot.Length)
    } else {
        $rel = Join-Path 'external' ($source -replace '[:\\\/]','_')
    }

    $dest = Join-Path $BackupsRoot $rel
    $destDir = Split-Path -Parent $dest
    if (-not (Test-Path $destDir)) {
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    }

    Copy-Item -Path $source -Destination $dest -Force
    $created += $dest
    Write-Host "Backed up: $source -> $dest"
}
Write-Host "`nBackup complete. Backups directory: $BackupsRoot"
Write-Output $created
