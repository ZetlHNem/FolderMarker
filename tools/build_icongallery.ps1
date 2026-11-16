$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $here "IconGallery.cs"
$out = Join-Path $here "IconGallery.exe"
$code = Get-Content -LiteralPath $src -Raw -Encoding UTF8
$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateExecutable = $true
$cp.OutputAssembly = $out
$cp.IncludeDebugInformation = $false
$cp.CompilerOptions = "/target:exe /optimize+"
$cp.ReferencedAssemblies.Add("System.dll") | Out-Null
$cp.ReferencedAssemblies.Add("System.Drawing.dll") | Out-Null
Add-Type -TypeDefinition $code -CompilerParameters $cp -ErrorAction Stop
Write-Output "OK"
