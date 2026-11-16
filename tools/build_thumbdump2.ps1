$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $here "thumb_dump2.cs"; $out = Join-Path $here "ThumbDump2.exe"
$code = Get-Content -LiteralPath $src -Raw -Encoding UTF8
$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateExecutable = $true; $cp.OutputAssembly = $out; $cp.IncludeDebugInformation = $false
$cp.CompilerOptions = "/target:exe /optimize+"
$cp.ReferencedAssemblies.Add("System.dll") | Out-Null
$cp.ReferencedAssemblies.Add("System.Drawing.dll") | Out-Null
Add-Type -TypeDefinition $code -CompilerParameters $cp -ErrorAction Stop
Write-Output "OK"
