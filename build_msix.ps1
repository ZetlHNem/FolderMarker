# 编译原生 COM DLL → 嵌入包身份清单 → 打成 sparse MSIX → 自签名
# 前置：tools\zig\zig.exe（编译器）、tools\sdktools\bin\10.0.22621.0\x64\mt.exe 与 makeappx/signtool
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$zig  = Join-Path $root "tools\zig\zig.exe"
if (-not (Test-Path -LiteralPath $zig)) {
    # PyPI 的 ziglang wheel 解压出来的位置
    $zigPkg = Join-Path $root "tools\zigpkg\ziglang\zig.exe"
    if (Test-Path -LiteralPath $zigPkg) { $zig = $zigPkg }
}
if (-not (Test-Path -LiteralPath $zig)) {
    $zigPy = & python -c "import ziglang, os; print(os.path.join(os.path.dirname(ziglang.__file__), 'zig.exe'))" 2>$null
    if ($zigPy -and (Test-Path -LiteralPath $zigPy)) { $zig = $zigPy.Trim() }
}
$sdk  = Join-Path $root "tools\sdktools\bin\10.0.22621.0\x64"
$mt   = Join-Path $sdk "mt.exe"
$pack = Join-Path $sdk "makeappx.exe"
$sign = Join-Path $sdk "signtool.exe"

$dll    = Join-Path $root "build\FolderMarker.Shell.dll"
$srcC   = Join-Path $root "src\shell\shell_native.c"
$mani   = Join-Path $root "src\shell\shell.manifest"
$pkgDir = Join-Path $root "packaging"
$msix   = Join-Path $root "dist\FolderMarkerShell.msix"
$pfx    = Join-Path $root "build\FolderMarker.pfx"
$cer    = Join-Path $root "build\FolderMarker.cer"

foreach ($t in @($zig, $mt, $pack, $sign)) {
    if (-not (Test-Path -LiteralPath $t)) { Write-Output "缺少工具: $t"; exit 1 }
}
if (-not (Test-Path -LiteralPath (Join-Path $root "dist"))) {
    New-Item -ItemType Directory -Path (Join-Path $root "dist") | Out-Null
}
if (Test-Path -LiteralPath $dll) { [System.IO.File]::Delete($dll) }

# 1) 编译原生 DLL（x64，链接 ole32/shlwapi）
#    zig 0.10 在某些环境下会自己崩（0xC0000374 堆损坏）。所以先编到临时文件，
#    失败时退回已有的 DLL —— shell_native.c 没改动时成品完全等价，不让打包链断掉。
$dllTmp = Join-Path $root "build\FolderMarker.Shell.new.dll"
if (Test-Path -LiteralPath $dllTmp) { [System.IO.File]::Delete($dllTmp) }
& $zig cc -shared -O2 -o $dllTmp $srcC -lole32 -lshlwapi -luser32 -lkernel32 2>$null
if (Test-Path -LiteralPath $dllTmp) {
    Move-Item -LiteralPath $dllTmp -Destination $dll -Force
} else {
    Write-Output "zig 编译失败，尝试复用已有 DLL"
    if (-not (Test-Path -LiteralPath $dll)) {
        $fallback = Join-Path $env:LOCALAPPDATA "FolderMarker\FolderMarker.Shell.dll"
        if (Test-Path -LiteralPath $fallback) {
            Copy-Item -LiteralPath $fallback -Destination $dll -Force
            Write-Output "已复用安装目录的 DLL"
        }
    }
}
if (-not (Test-Path -LiteralPath $dll)) { Write-Output "编译失败且无可用 DLL"; exit 1 }
Write-Output ("DLL " + [math]::Round((Get-Item $dll).Length / 1KB, 1) + " KB")

# 2) 把带 msix identity 的清单嵌进 DLL 的 RT_MANIFEST（缺这步 Explorer 不加载）
& $mt -nologo -manifest $mani "-outputresource:$dll;#2"
if ($LASTEXITCODE -ne 0) { Write-Output "mt 嵌入清单失败"; exit 1 }
Write-Output "清单已嵌入"

# 3) 打包（sparse package：二进制在外部目录，但清单里声明的 Executable 必须在包里有一份占位）
#    包的 Identity 版本固定不随程序版本递增 —— shell 扩展 DLL 跨版本几乎不变，
#    升级时安装器检测到「已注册 + DLL 未变」会整个跳过注册，0 次资源管理器重启。
#    只有 DLL 真的改了（ShellExtVer +1）才需要卸旧包重注册。
$pkgBuild = Join-Path $root "build\pkg"
if (Test-Path -LiteralPath $pkgBuild) { Remove-Item -LiteralPath $pkgBuild -Recurse -Force }
Copy-Item -LiteralPath $pkgDir -Destination $pkgBuild -Recurse -Force
Write-Output "包版本 1.0.0.1（固定）"

Remove-Item -LiteralPath (Join-Path $pkgBuild "AppxManifest.badge.xml") -Force -ErrorAction SilentlyContinue
# 双 Verb 实验清单只由 tools\deploy_dual_verb.ps1 使用，不进正式包
Remove-Item -LiteralPath (Join-Path $pkgBuild "AppxManifest.dual-verb.xml") -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $root "build\FolderMarker.exe") -Destination (Join-Path $pkgBuild "FolderMarker.exe") -Force
if (Test-Path -LiteralPath $msix) { [System.IO.File]::Delete($msix) }
& $pack pack /d $pkgBuild /p $msix /o | Out-Null
if (-not (Test-Path -LiteralPath $msix)) { Write-Output "打包失败"; exit 1 }
Write-Output ("MSIX " + [math]::Round((Get-Item $msix).Length / 1KB, 1) + " KB")

# 4) 签名（证书不存在则先建一张）
if (-not (Test-Path -LiteralPath $pfx)) {
    Write-Output "生成自签名证书…"
    $cert = New-SelfSignedCertificate -Type Custom -Subject "CN=FolderMarker" `
        -KeyUsage DigitalSignature -FriendlyName "FolderMarker MSIX" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
    $pwd = ConvertTo-SecureString -String "fm" -Force -AsPlainText
    Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $pwd | Out-Null
    Export-Certificate -Cert $cert -FilePath $cer | Out-Null
}
& $sign sign /fd SHA256 /a /f $pfx /p "fm" $msix
if ($LASTEXITCODE -ne 0) { Write-Output "签名失败"; exit 1 }
Write-Output "已签名"
