# FolderMarker 新版右键菜单注册脚本（随安装包分发，由安装器 -File 调用）
# 用法：
#   RegisterMenu.ps1 -MsixPath <msix> -AppDir <安装目录>        # 注册/保活
#   RegisterMenu.ps1 -Uninstall -AppDir <安装目录>              # 卸载
# 退出码：0 = 成功（或已是最新），2 = 失败（详见日志）
param(
    [string]$MsixPath,
    [string]$AppDir = "$env:LOCALAPPDATA\FolderMarker",
    [switch]$Uninstall
)
$ErrorActionPreference = 'Stop'
$log = Join-Path $AppDir 'register_menu.log'

function Log($msg) {
    $line = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + '  ' + $msg
    try { Add-Content -LiteralPath $log -Value $line -Encoding UTF8 } catch { }
    Write-Output $msg
}

function Get-MsixVersion($path) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($path)
    try {
        $entry = $zip.Entries | Where-Object { $_.FullName -eq 'AppxManifest.xml' } | Select-Object -First 1
        if (-not $entry) { return $null }
        $sr = New-Object System.IO.StreamReader($entry.Open())
        $xml = [xml]$sr.ReadToEnd()
        $sr.Close()
        return [version]$xml.Package.Identity.Version
    } finally { $zip.Dispose() }
}

try {
    $pkg = Get-AppxPackage -Name FolderMarkerShell

    if ($Uninstall) {
        if ($pkg) {
            $pkg | Remove-AppxPackage
            Log '已卸载菜单扩展包'
        } else {
            Log '未安装菜单扩展包，无需卸载'
        }
        exit 0
    }

    if (-not (Test-Path -LiteralPath $MsixPath)) { Log ('找不到 msix: ' + $MsixPath); exit 2 }

    $want = Get-MsixVersion $MsixPath
    Log ('目标版本: ' + $want + ' / 当前: ' + $(if ($pkg) { $pkg.Version } else { '无' }))

    if ($pkg -and $pkg.Version -eq $want) {
        Log '已注册且版本一致，跳过（外部 DLL 已随安装替换，无需重装包）'
        exit 0
    }

    if ($pkg) {
        try { $pkg | Remove-AppxPackage; Log '已卸载旧版本包' }
        catch { Log ('卸载旧包失败（继续尝试安装）: ' + $_.Exception.Message) }
    }

    $added = $false
    try {
        Add-AppxPackage -Path $MsixPath -ExternalLocation $AppDir -AllowUnsigned -ErrorAction Stop
        $added = $true
        Log 'AllowUnsigned 注册成功'
    } catch {
        Log ('AllowUnsigned 失败: ' + $_.Exception.Message)
        try {
            Add-AppxPackage -Path $MsixPath -ExternalLocation $AppDir -ErrorAction Stop
            $added = $true
            Log '签名注册成功（证书已受信任）'
        } catch {
            Log ('签名注册失败: ' + $_.Exception.Message)
        }
    }

    if ($added) { exit 0 } else { exit 2 }
} catch {
    Log ('异常: ' + $_.Exception.Message)
    exit 2
}
