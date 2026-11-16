# 把单 path 的 SVG 渲染成 PNG（WPF Geometry.Parse，零外部依赖）
# 用法：render_svg_path.ps1 <svg文件> <输出png> <尺寸> [填充色]
param(
    [Parameter(Mandatory=$true)][string]$SvgPath,
    [Parameter(Mandatory=$true)][string]$OutPng,
    [int]$Size = 256,
    [string]$Color = "#474747"
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName PresentationCore, WindowsBase, PresentationFramework

[xml]$svg = Get-Content -LiteralPath $SvgPath -Raw -Encoding UTF8
$svgNs = New-Object System.Xml.XmlNamespaceManager($svg.NameTable)
$svgNs.AddNamespace("s", "http://www.w3.org/2000/svg")
$path = $svg.SelectSingleNode("//s:path", $svgNs)
if (-not $path) { $path = $svg.SelectSingleNode("//path", $svgNs) }
$d = $path.GetAttribute("d")
# SVG 里 fill 优先，其次命令行参数
$fill = $path.GetAttribute("fill")
if ($fill -and $fill -ne "currentColor") { $Color = $fill }

# viewBox → 尺寸缩放
$viewBox = "0 0 24 24"
$vbAttr = $svg.SelectSingleNode("//s:svg", $svgNs)
if ($vbAttr) { $viewBox = $vbAttr.GetAttribute("viewBox") }
$vb = $viewBox -split '\s+' | ForEach-Object { [double]$_ }
$scale = $Size / [Math]::Max($vb[2], $vb[3])
$ox = -$vb[0] * $scale
$oy = -$vb[1] * $scale

$geo = [System.Windows.Media.Geometry]::Parse($d)
# Parse 出来的 Geometry 只读，包一层 GeometryGroup 再加变换
$grp = New-Object System.Windows.Media.GeometryGroup
$grp.Children.Add($geo) | Out-Null
$grp.Transform = New-Object System.Windows.Media.MatrixTransform(
    (New-Object System.Windows.Media.Matrix($scale, 0, 0, $scale, $ox, $oy)))
$geo = $grp

$brush = New-Object System.Windows.Media.SolidColorBrush(
    ([System.Windows.Media.ColorConverter]::ConvertFromString($Color)))
$brush.Freeze()

$visual = New-Object System.Windows.Media.DrawingVisual
$ctx = $visual.RenderOpen()
$ctx.DrawGeometry($brush, $null, $geo)
$ctx.Close()

$bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($Size, $Size, 96, 96,
    [System.Windows.Media.Imaging.PixelFormats]::Pbgra32)
$bmp.Render($visual)

$enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
$enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
$fs = [System.IO.File]::Create($OutPng)
$enc.Save($fs)
$fs.Close()
Write-Output ("OK " + $OutPng + " " + $Size + "px fill=" + $Color)
