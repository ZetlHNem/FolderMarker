Add-Type -AssemblyName System.Drawing

$icons = "C:\Users\EDY\WorkBuddy\2026-11-06-14-47-56\FolderMarker\icons"
$out = "C:\Users\EDY\WorkBuddy\2026-11-06-14-47-56\FolderMarker\build\menu_icon_preview.png"

# .NET 的 Icon 类读不了 PNG 压缩的 ico 帧，这里改用 Python 预先导出的 PNG 帧
$pre = "C://Users//EDY//WorkBuddy//2026-11-06-14-47-56//FolderMarker//build//prev"
$old16 = [System.Drawing.Image]::FromFile((Join-Path $pre "old_16.png"))
$new16 = [System.Drawing.Image]::FromFile((Join-Path $pre "new_16.png"))
$new20 = [System.Drawing.Image]::FromFile((Join-Path $pre "new_20.png"))
$new32 = [System.Drawing.Image]::FromFile((Join-Path $pre "new_32.png"))

$W = 520; $H = 210
$bmp = New-Object System.Drawing.Bitmap $W, $H
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$g.Clear([System.Drawing.Color]::White)

$font = New-Object System.Drawing.Font("Microsoft YaHei UI", 9, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Point)
$tiny = New-Object System.Drawing.Font("Microsoft YaHei UI", 7.5, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Point)
$ink = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 28, 28, 30))
$dim = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 120, 120, 126))
$bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 246, 246, 248))
$bgDark = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 43, 43, 46))

# ---- 上：旧 / 新 的菜单行（浅色底）
$g.FillRectangle($bg, 10, 24, 500, 34)
$g.DrawImage($old16, 22, 33, 16, 16)
$g.DrawString("文件夹标记（旧）", $font, $ink, 48, 31)
$g.DrawImage($new16, 262, 33, 16, 16)
$g.DrawString("文件夹标记（新）", $font, $ink, 288, 31)

# ---- 中：深色菜单行
$g.FillRectangle($bgDark, 10, 64, 500, 34)
$g.DrawImage($old16, 22, 73, 16, 16)
$g.DrawString("文件夹标记（旧）", $font, (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 240,240,240))), 48, 71)
$g.DrawImage($new16, 262, 73, 16, 16)
$g.DrawString("文件夹标记（新）", $font, (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 240,240,240))), 288, 71)

# ---- 下：放大对比
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$g.DrawImage($old16, 20, 116, 80, 80)
$g.DrawString("旧 16px", $tiny, $dim, 20, 198)
$g.DrawImage($new16, 120, 116, 80, 80)
$g.DrawString("新 16px", $tiny, $dim, 120, 198)
$g.DrawImage($new20, 225, 124, 64, 64)
$g.DrawString("新 20px", $tiny, $dim, 225, 198)
$g.DrawImage($new32, 310, 132, 48, 48)
$g.DrawString("新 32px（菜单 150% 缩放时）", $tiny, $dim, 310, 198)

$g.Dispose()
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output ("wrote " + $out)
