# 把 src\core\FolderMarker.cs + src\ui\ManagerWindow.cs 编译成 GUI 子系统 EXE
# 用 CompileAssemblyFromFile 而不是 Add-Type：后者只能吃单个源码字符串，
# 而 WPF 管理台单独放一个文件（有自己的 using），两个文件必须分开编译。
$ErrorActionPreference = "Stop"

$root   = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcs   = @(
    (Join-Path $root "src\core\FolderMarker.cs"),
    (Join-Path $root "src\core\SelfTest.cs"),
    (Join-Path $root "src\core\Tray.cs"),
    (Join-Path $root "src\core\Icon.cs"),
    (Join-Path $root "src\core\Watch.cs"),
    (Join-Path $root "src\core\Command.cs"),
    (Join-Path $root "src\core\Ini.cs"),
    (Join-Path $root "src\core\Color.cs"),
    (Join-Path $root "src\core\Rules.cs"),
    (Join-Path $root "src\core\Cache.cs"),
    (Join-Path $root "src\core\IniIo.cs"),
    (Join-Path $root "src\core\Config.cs"),
    (Join-Path $root "src\core\Scan.cs"),
    (Join-Path $root "src\core\Lists.cs"),
    (Join-Path $root "src\core\ShellIndex.cs"),
    (Join-Path $root "src\core\Mark.cs"),
    (Join-Path $root "src\core\Ico.cs"),
    (Join-Path $root "src\core\Native.cs"),
    (Join-Path $root "src\ui\WinUi.cs"),
    (Join-Path $root "src\ui\ManagerWindow.cs"),
    (Join-Path $root "src\ui\Watch.cs"),
    (Join-Path $root "src\ui\Roots.cs"),
    (Join-Path $root "src\ui\Log.cs"),
    (Join-Path $root "src\ui\Settings.cs"),
    (Join-Path $root "src\ui\ColorPickerWindow.cs")
)
$outDir = Join-Path $root "build"
$exe    = Join-Path $outDir "FolderMarker.exe"

if (-not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Path $outDir | Out-Null
}
if (Test-Path -LiteralPath $exe) {
    [System.IO.File]::Delete($exe)
}

foreach ($s in $srcs) {
    if (-not (Test-Path -LiteralPath $s)) { Write-Output ("缺少源码: " + $s); exit 1 }
}

$icon = Join-Path $root "icons\app.ico"

# WPF 程序集不在默认引用路径里，必须给完整路径
$fx  = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
$wpf = Join-Path $fx "WPF"
if (-not (Test-Path -LiteralPath (Join-Path $wpf "PresentationFramework.dll"))) {
    $fx  = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319"
    $wpf = Join-Path $fx "WPF"
}

$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateExecutable = $true
$cp.OutputAssembly     = $exe
$cp.IncludeDebugInformation = $false
$cp.CompilerOptions    = "/target:winexe /optimize+ /win32icon:`"$icon`""
$cp.ReferencedAssemblies.Add("System.dll") | Out-Null
$cp.ReferencedAssemblies.Add("System.Drawing.dll") | Out-Null
$cp.ReferencedAssemblies.Add("System.Windows.Forms.dll") | Out-Null
$cp.ReferencedAssemblies.Add((Join-Path $fx  "System.Xaml.dll")) | Out-Null
$cp.ReferencedAssemblies.Add((Join-Path $wpf "WindowsBase.dll")) | Out-Null
$cp.ReferencedAssemblies.Add((Join-Path $wpf "PresentationCore.dll")) | Out-Null
$cp.ReferencedAssemblies.Add((Join-Path $wpf "PresentationFramework.dll")) | Out-Null

$provider = New-Object Microsoft.CSharp.CSharpCodeProvider
$result = $provider.CompileAssemblyFromFile($cp, [string[]]$srcs)

if ($result.Errors.Count -gt 0) {
    foreach ($e in $result.Errors) { Write-Output ("ERR " + $e.ToString()) }
    exit 1
}

if (Test-Path -LiteralPath $exe) {
    # 让 build\FolderMarker.exe 可以就地运行 / selftest：纯色图标按 exe 同级的 icons\ 查找
    $iconsSrc = Join-Path $root "icons"
    $iconsDst = Join-Path $outDir "icons"
    if (Test-Path -LiteralPath $iconsSrc) {
        if (-not (Test-Path -LiteralPath $iconsDst)) {
            New-Item -ItemType Directory -Path $iconsDst | Out-Null
        }
        Copy-Item -Path (Join-Path $iconsSrc "*.ico") -Destination $iconsDst -Force
    }
    $f = Get-Item -LiteralPath $exe
    Write-Output ("OK " + $f.FullName + " " + [math]::Round($f.Length / 1KB, 1) + " KB")
} else {
    Write-Output "FAILED: no output"
    exit 1
}
