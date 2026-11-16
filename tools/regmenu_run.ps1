# 一体化菜单验证：启动 regmenu_probe（菜单保持 25s）→ UIA 展开「文件夹标记」
# 及其全部子级 → 探针自动 ESC → 读取输出。
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$dir   = 'C:\Users\EDY\WorkBuddy\2026-11-06-14-47-56\FolderMarker\build\probe_dir'
$probe = 'C:\Users\EDY\WorkBuddy\2026-11-06-14-47-56\FolderMarker\build\regmenu_probe.exe'
$out   = 'C:\Users\EDY\WorkBuddy\2026-11-06-14-47-56\FolderMarker\build\regmenu_out.txt'

Remove-Item $out -ErrorAction SilentlyContinue
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName        = 'cmd.exe'
$psi.Arguments       = ('/c ""' + $probe + '" "' + $dir + '" 0 25000 > "' + $out + '" 2>&1"')
$psi.UseShellExecute = $false
$psi.CreateNoWindow  = $true
$p = [System.Diagnostics.Process]::Start($psi)

# 等菜单弹层出现（UIA 根的直接子级里找 #32768）
$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ClassNameProperty, "32768")
$menu = $null
for ($i = 0; $i -lt 40 -and -not $menu; $i++) {
    $menu = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
    if (-not $menu) { Start-Sleep -Milliseconds 500 }
}
if (-not $menu) { Write-Output "NO_MENU (probe exited: $($p.HasExited))"; exit 1 }
Write-Output ("MENU: [" + $menu.Current.Name + "]")

# 展开「文件夹标记」
$itemCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, "文件夹标记")
$item = $menu.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $itemCond)
if (-not $item) { Write-Output "NO_ITEM"; exit 1 }
try {
    $ec = $item.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $ec.Expand()
    Start-Sleep -Milliseconds 1500
    Write-Output "EXPANDED_OK"
} catch {
    Write-Output ("EXPAND_FAIL: " + $_.Exception.Message)
}

# 等探针结束并输出
if (-not $p.WaitForExit(40000)) { $p.Kill() }
Get-Content $out -Encoding UTF8 | Select-Object -First 60
