# 实验部署：同包双 Verb，验证 Win11 新版菜单的「应用归属 flyout」三级结构。
# 只动 sparse 包与 %LOCALAPPDATA% 里的 DLL，不动经典注册表菜单、不动正式安装包。
# 回滚：运行 tools/restore_single_verb.ps1
$ErrorActionPreference = "Stop"

$root  = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$zig   = Join-Path $root "tools\zigpkg\ziglang\zig.exe"
$sdk   = Join-Path $root "tools\sdktools\bin\10.0.22621.0\x64"
$mt    = Join-Path $sdk "mt.exe"
$pack  = Join-Path $sdk "makeappx.exe"
$sign  = Join-Path $sdk "signtool.exe"
$srcC  = Join-Path $root "src\shell\shell_native.c"
$mani  = Join-Path $root "src\shell\shell.manifest"
$dual  = Join-Path $root "packaging\AppxManifest.dual-verb.xml"
$pfx   = Join-Path $root "build\FolderMarker.pfx"
$dll   = Join-Path $root "build\test_shell.dll"
$msix  = Join-Path $root "dist\FolderMarkerShell.msix"
$appDir = Join-Path $env:LOCALAPPDATA "FolderMarker"

foreach ($t in @($zig, $mt, $pack, $sign, $srcC, $mani, $dual, $pfx)) {
    if (-not (Test-Path -LiteralPath $t)) { Write-Output "缺少: $t"; exit 1 }
}

# 1) 编译 + 嵌入包身份清单
& $zig cc -shared -O2 -o $dll $srcC -lole32 -lshlwapi -luser32 -lkernel32 2>$null
if (-not (Test-Path -LiteralPath $dll)) { Write-Output "DLL 编译失败"; exit 1 }
& $mt -nologo -manifest $mani "-outputresource:$dll;#2" | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Output "清单嵌入失败"; exit 1 }
Write-Output ("DLL 编译+嵌入清单 OK ({0:N1} KB)" -f ((Get-Item $dll).Length / 1KB))

# 2) 用双 Verb 清单打包（临时目录，不改 packaging\AppxManifest.xml）
$pkgDir = Join-Path $root "build\pkg-dual"
if (Test-Path -LiteralPath $pkgDir) { Remove-Item -LiteralPath $pkgDir -Recurse -Force }
New-Item -ItemType Directory -Path $pkgDir -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root "packaging\Assets") -Destination (Join-Path $pkgDir "Assets") -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root "build\FolderMarker.exe") -Destination (Join-Path $pkgDir "FolderMarker.exe") -Force
Copy-Item -LiteralPath $dual -Destination (Join-Path $pkgDir "AppxManifest.xml") -Force
if (Test-Path -LiteralPath $msix) { Remove-Item -LiteralPath $msix -Force }
& $pack pack /d $pkgDir /p $msix /o | Out-Null
if (-not (Test-Path -LiteralPath $msix)) { Write-Output "打包失败"; exit 1 }
& $sign sign /fd SHA256 /a /f $pfx /p "fm" $msix | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Output "签名失败"; exit 1 }
Write-Output ("MSIX 打包+签名 OK ({0:N1} KB)" -f ((Get-Item $msix).Length / 1KB))

# 3) 部署 DLL：先停 COM 宿主，旧文件改名留档，再复制新文件
Get-Process dllhost -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
$target = Join-Path $appDir "FolderMarker.Shell.dll"
if (Test-Path -LiteralPath $target) {
    $bakName = "FolderMarker.Shell.old-dual-{0}.dll" -f (Get-Date -Format "HHmmss")
    # 被加载中的 DLL 不能删，但可以先改名；改名后就能写入同名新文件。
    Rename-Item -LiteralPath $target -NewName $bakName -Force
}
Copy-Item -LiteralPath $dll -Destination $target -Force
Copy-Item -LiteralPath $msix -Destination (Join-Path $appDir "FolderMarkerShell.msix") -Force
Write-Output "DLL / MSIX 已部署到 $appDir"

# 4) 重新注册 sparse 包
Get-AppxPackage -Name "FolderMarkerShell" | Remove-AppxPackage -ErrorAction SilentlyContinue
Get-AppxPackage -Name "FolderMarkerBadgeShell" | Remove-AppxPackage -ErrorAction SilentlyContinue
$ok = $false
try {
    Add-AppxPackage -Path $msix -ExternalLocation $appDir -AllowUnsigned -ErrorAction Stop
    $ok = $true
} catch {
    try { Add-AppxPackage -Path $msix -ExternalLocation $appDir; $ok = $true } catch {}
}
if (-not $ok) { Write-Output "sparse 包注册失败"; exit 1 }

# 5) 刷新 Explorer 的静态 verb / 关联缓存
#    光重启 explorer.exe 不足以清掉缓存的静态级联菜单结构，
#    必须发 SHCNE_ASSOCCHANGED + SHCNF_FLUSH（安装器里也有这一步）。
$refresh = Join-Path $root "build\refresh_shell.exe"
if (Test-Path -LiteralPath $refresh) { & $refresh | Out-Null }
$p = Get-AppxPackage -Name "FolderMarkerShell"
Write-Output ("已注册: {0}  v{1}" -f $p.Name, $p.Version)
Write-Output "已发送 SHCNE_ASSOCCHANGED，重启 Explorer 后生效"
Write-Output "清空 fm_shell.log，等待用户右键取证"
$log = Join-Path $env:TEMP "fm_shell.log"
if (Test-Path -LiteralPath $log) { Clear-Content -LiteralPath $log -Force }
Write-Output "DONE"
