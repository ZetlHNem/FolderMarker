# 启动管理台并截取窗口画面，用于自测「目录抽屉」渲染
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName PresentationFramework

$exe = Join-Path $env:LOCALAPPDATA "FolderMarker\FolderMarker.exe"
$out = "C:\Users\EDY\WorkBuddy\2026-11-06-14-47-56\FolderMarker\build\manager_shot.png"
Remove-Item $out -ErrorAction SilentlyContinue

$p = Start-Process -FilePath $exe -ArgumentList "ui" -PassThru
Start-Sleep -Milliseconds 2500
$p.Refresh()
$h = $p.MainWindowHandle
if ($h -eq 0) { Write-Output "no window"; exit 1 }

$sig = @'
using System;
using System.Runtime.InteropServices;
public class Cap {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
'@
Add-Type -TypeDefinition $sig

[Cap]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 600

$r = New-Object Cap+RECT
[Cap]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.R - $r.L
$ht = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap($w, $ht)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[Cap]::PrintWindow($h, $hdc, 2) | Out-Null
$g.ReleaseHdc($hdc)
$g.Dispose()
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output ("saved " + $out + " " + $w + "x" + $ht)
Start-Sleep -Milliseconds 300
try { $p.CloseMainWindow() | Out-Null } catch { }
Start-Sleep -Milliseconds 400
if (-not $p.HasExited) { try { $p.Kill() } catch { } }
