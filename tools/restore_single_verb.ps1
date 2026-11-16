# 回滚：把 sparse 包与 DLL 恢复到实验前的单 Verb 状态。
# 用法：powershell -ExecutionPolicy Bypass -File tools\restore_single_verb.ps1
$ErrorActionPreference = "Stop"

$root   = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$sdk    = Join-Path $root "tools\sdktools\bin\10.0.22621.0\x64"
$pack   = Join-Path $sdk "makeappx.exe"
$sign   = Join-Path $sdk "signtool.exe"
$backup = Join-Path $root "build\AppxManifest.single-verb.backup.xml"
$pfx    = Join-Path $root "build\FolderMarker.pfx"
$msix   = Join-Path $root "dist\FolderMarkerShell.msix"
$appDir = Join-Path $env:LOCALAPPDATA "FolderMarker"

foreach ($t in @($pack, $sign, $backup, $pfx)) {
    if (-not (Test-Path -LiteralPath $t)) { Write-Output "缺少: $t"; exit 1 }
}

$pkgDir = Join-Path $root "build\pkg-restore"
if (Test-Path -LiteralPath $pkgDir) { Remove-Item -LiteralPath $pkgDir -Recurse -Force }
New-Item -ItemType Directory -Path $pkgDir -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root "packaging\Assets") -Destination (Join-Path $pkgDir "Assets") -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root "build\FolderMarker.exe") -Destination (Join-Path $pkgDir "FolderMarker.exe") -Force
Copy-Item -LiteralPath $backup -Destination (Join-Path $pkgDir "AppxManifest.xml") -Force

if (Test-Path -LiteralPath $msix) { Remove-Item -LiteralPath $msix -Force }
& $pack pack /d $pkgDir /p $msix /o | Out-Null
& $sign sign /fd SHA256 /a /f $pfx /p "fm" $msix | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Output "签名失败"; exit 1 }

# 找最近一份实验前的 DLL 备份
$old = Get-ChildItem -LiteralPath $appDir -Filter "FolderMarker.Shell.old-*.dll" -ErrorAction SilentlyContinue |
       Where-Object { $_.Name -notlike "*dual*" } |
       Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $old) { Write-Output "找不到实验前的 DLL 备份，仅回滚 sparse 包" }
else {
    Get-Process dllhost -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    $target = Join-Path $appDir "FolderMarker.Shell.dll"
    if (Test-Path -LiteralPath $target) {
        Rename-Item -LiteralPath $target -NewName ("FolderMarker.Shell.old-restore-{0}.dll" -f (Get-Date -Format "HHmmss")) -Force
    }
    Copy-Item -LiteralPath $old.FullName -Destination $target -Force
    Write-Output ("DLL 已回滚到 {0}" -f $old.Name)
}

Get-AppxPackage -Name "FolderMarkerShell" | Remove-AppxPackage -ErrorAction SilentlyContinue
Get-AppxPackage -Name "FolderMarkerBadgeShell" | Remove-AppxPackage -ErrorAction SilentlyContinue
try { Add-AppxPackage -Path $msix -ExternalLocation $appDir -AllowUnsigned -ErrorAction Stop }
catch { Add-AppxPackage -Path $msix -ExternalLocation $appDir }
Write-Output "sparse 包已回滚为单 Verb"
Write-Output "DONE"
