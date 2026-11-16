# 一键构建：解压工具 → 编译 EXE → 自检 → 编译/签名 MSIX →（有 Inno Setup 时）打安装包
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Log([string]$msg) { Write-Output ("[build_all] " + $msg) }
function Fail([string]$msg) { Write-Error $msg; exit 1 }
function RunStep([string]$script) {
    Log ("运行 " + $script)
    & (Join-Path $root $script)
    if (-not $?) { Fail ($script + " 失败") }
    if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        Fail ($script + " 失败，退出码 " + $LASTEXITCODE)
    }
}

$buildDir = Join-Path $root "build"
if (-not (Test-Path -LiteralPath $buildDir)) {
    New-Item -ItemType Directory -Path $buildDir | Out-Null
}

# 1) zig：wheel 里的 ziglang\zig.exe + lib 是 zig cc 必需的
$zigDir = Join-Path $root "tools\zig"
$zig = Join-Path $zigDir "zig.exe"
if (-not (Test-Path -LiteralPath $zig)) {
    $whl = Join-Path $root "tools\zig010.whl"
    if (-not (Test-Path -LiteralPath $whl)) { Fail "缺少 tools\zig010.whl" }
    Log "解压 zig 工具链（首次运行较慢）..."
    $tmpZip = Join-Path $buildDir "_zig010.zip"
    $tmpDir = Join-Path $buildDir "_zig_extract"
    Copy-Item -LiteralPath $whl -Destination $tmpZip -Force
    if (Test-Path -LiteralPath $tmpDir) { Remove-Item -LiteralPath $tmpDir -Recurse -Force }
    [System.IO.Compression.ZipFile]::ExtractToDirectory($tmpZip, $tmpDir)
    $libSrc = Join-Path $tmpDir "ziglang\lib"
    if (-not (Test-Path -LiteralPath (Join-Path $tmpDir "ziglang\zig.exe"))) { Fail "zig 包结构不符：找不到 ziglang\zig.exe" }
    if (-not (Test-Path -LiteralPath $libSrc)) { Fail "zig 包结构不符：找不到 ziglang\lib" }
    New-Item -ItemType Directory -Path $zigDir -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $tmpDir "ziglang\zig.exe") -Destination $zig -Force
    Copy-Item -LiteralPath $libSrc -Destination (Join-Path $zigDir "lib") -Recurse -Force
    Remove-Item -LiteralPath $tmpZip -Force
    Remove-Item -LiteralPath $tmpDir -Recurse -Force
}
if (-not (Test-Path -LiteralPath $zig)) { Fail "zig.exe 准备失败" }

# 2) Windows SDK 工具：mt / makeappx / signtool
$sdkBin = Join-Path $root "tools\sdktools\bin\10.0.22621.0\x64"
$need = @("mt.exe", "makeappx.exe", "signtool.exe")
$missing = @()
foreach ($n in $need) {
    if (-not (Test-Path -LiteralPath (Join-Path $sdkBin $n))) { $missing += $n }
}
if ($missing.Count -gt 0) {
    $zip = Join-Path $root "tools\sdktools.zip"
    if (-not (Test-Path -LiteralPath $zip)) { Fail "缺少 tools\sdktools.zip" }
    Log "解压 Windows SDK 工具..."
    $tmpDir = Join-Path $buildDir "_sdk_extract"
    if (Test-Path -LiteralPath $tmpDir) { Remove-Item -LiteralPath $tmpDir -Recurse -Force }
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $tmpDir)
    $src = Join-Path $tmpDir "bin\10.0.22621.0\x64"
    if (-not (Test-Path -LiteralPath $src)) { Fail "sdktools.zip 结构不符" }
    New-Item -ItemType Directory -Path $sdkBin -Force | Out-Null
    Copy-Item -Path (Join-Path $src "*") -Destination $sdkBin -Recurse -Force
    Remove-Item -LiteralPath $tmpDir -Recurse -Force
}
foreach ($n in $need) {
    if (-not (Test-Path -LiteralPath (Join-Path $sdkBin $n))) { Fail ("SDK 工具缺失: " + $n) }
}
Log "工具准备完成"

# 3) 编译主程序（脚本会自动把 icons\ 复制到 build\）
RunStep "build_exe.ps1"

# 4) 自检
Log "运行 selftest..."
$exe = Join-Path $buildDir "FolderMarker.exe"
$p = Start-Process -FilePath $exe -ArgumentList "selftest" -Wait -PassThru
if ($p.ExitCode -ne 0) { Fail ("selftest 失败，退出码 " + $p.ExitCode) }
$report = Join-Path $env:LOCALAPPDATA "FolderMarker\diagnose.txt"
if (Test-Path -LiteralPath $report) {
    $txt = Get-Content -LiteralPath $report -Raw -Encoding UTF8
    if ($txt -notmatch "39 通过 / 0 失败") { Fail "selftest 报告不是 39 通过 / 0 失败" }
    Log "自检通过：39 / 0"
} else {
    Log "未找到自检报告，跳过报告检查"
}

# 5) 原生 shell 扩展 + MSIX（签名证书不存在会自动生成）
RunStep "build_msix.ps1"

# 6) 安装包：找到 Inno Setup 7 就自动打，没装则跳过（其余产物已完成）
$iscc = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 7\ISCC.exe"
if (-not (Test-Path -LiteralPath $iscc)) { $iscc = Join-Path $env:ProgramFiles "Inno Setup 7\ISCC.exe" }
if (Test-Path -LiteralPath $iscc) {
    Log "编译安装包..."
    & $iscc (Join-Path $root "FolderMarker.iss") /Q
    if ($LASTEXITCODE -ne 0) { Fail "Inno Setup 编译失败" }
    $issText = Get-Content -LiteralPath (Join-Path $root "FolderMarker.iss") -Raw
    $ver = [regex]::Match($issText, '#define MyAppVersion "([^"]+)"').Groups[1].Value
    $setup = Join-Path $root ("dist\文件夹标记-安装包-v" + $ver + ".exe")
    if (-not (Test-Path -LiteralPath $setup)) { Fail "安装包未生成" }
    Log ("安装包 " + $setup)
} else {
    Log "未找到 Inno Setup 7，跳过安装包；EXE / MSIX 已完成"
}

Log "全部完成"
