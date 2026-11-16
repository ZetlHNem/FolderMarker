# UIA 展开当前弹出的 #32768 菜单里的「文件夹标记」子菜单。
# 与 regmenu_probe.exe 协作：探针打开菜单后延迟 N 毫秒才 ESC，
# 本脚本在这段窗口期里用 UIA ExpandCollapse 打开二级级联，
# 触发真实的 WM_INITMENUPOPUP 填充。
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ClassNameProperty, "32768")
$menu = $null
for ($i = 0; $i -lt 40 -and -not $menu; $i++) {
    $menu = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
    if (-not $menu) { Start-Sleep -Milliseconds 500 }
}
if (-not $menu) { Write-Output "NO_MENU"; exit 1 }
Write-Output ("MENU: " + $menu.Current.Name)

$itemCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, "文件夹标记")
$item = $menu.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $itemCond)
if (-not $item) { Write-Output "NO_ITEM"; exit 1 }
Write-Output ("ITEM: " + $item.Current.Name + " class=" + $item.Current.ClassName)

try {
    $ec = $item.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $ec.Expand()
    Start-Sleep -Milliseconds 1200
    Write-Output "EXPANDED"
} catch {
    Write-Output ("EXPAND_FAIL: " + $_.Exception.Message)
    exit 1
}
