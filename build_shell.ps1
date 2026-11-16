# 把 src\shell\ShellExtension.cs 编译成 AnyCPU 的 .NET Framework DLL（供 Explorer 通过 COM 加载）
# 用 CodeDom 而不是直接调 csc.exe：本机安全策略允许前者。
# 【注意】这是早期实验版托管 DLL（约 9KB），不是正式产物！
# 正式安装包里用的是 build_msix.ps1 由 shell_native.c 经 zig 编译的原生 DLL（约 191KB）。
# 跑本脚本会覆盖 build\FolderMarker.Shell.dll，覆盖后需重新跑 build_msix.ps1 恢复。
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $root "src\shell\ShellExtension.cs"
$outDir = Join-Path $root "build"
$dll  = Join-Path $outDir "FolderMarker.Shell.dll"

if (-not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Path $outDir | Out-Null
}
if (Test-Path -LiteralPath $dll) {
    [System.IO.File]::Delete($dll)
}

$code = [System.IO.File]::ReadAllText($src, [System.Text.Encoding]::UTF8)

$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateExecutable = $false
$cp.GenerateInMemory   = $false
$cp.OutputAssembly     = $dll
$cp.IncludeDebugInformation = $false
$cp.TempFiles = New-Object System.CodeDom.Compiler.TempFileCollection
$cp.CompilerOptions = "/target:library /platform:anycpu /optimize+"
$cp.ReferencedAssemblies.Add("System.dll") | Out-Null

$provider = New-Object Microsoft.CSharp.CSharpCodeProvider
$result = $provider.CompileAssemblyFromSource($cp, $code)

if ($result.Errors.Count -gt 0) {
    foreach ($e in $result.Errors) { Write-Output ("ERR " + $e.ToString()) }
    exit 1
}

if (Test-Path -LiteralPath $dll) {
    $f = Get-Item -LiteralPath $dll
    Write-Output ("OK " + $f.FullName + "  " + [math]::Round($f.Length / 1KB, 1) + " KB")
} else {
    Write-Output "FAILED: no output"
    exit 1
}
