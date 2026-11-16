# 编译诊断探针 IconProbe.exe（控制台程序，仅用于排查图标刷新时机）
$ErrorActionPreference = "Stop"

$root   = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj   = Split-Path -Parent $root
$src    = Join-Path $root "IconProbe.cs"
$exe    = Join-Path $root "IconProbe.exe"

if (Test-Path -LiteralPath $exe) { [System.IO.File]::Delete($exe) }

$code = Get-Content -LiteralPath $src -Raw -Encoding UTF8
Add-Type -TypeDefinition $code -OutputAssembly $exe -OutputType ConsoleApplication `
    -ReferencedAssemblies System.Drawing -ErrorAction Stop

if (Test-Path -LiteralPath $exe) {
    Write-Output ("OK " + $exe)
} else {
    Write-Output "FAILED"
    exit 1
}
