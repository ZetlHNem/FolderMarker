using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// 颜色算法：HSV 染色、遮罩 alpha、RGB/HSV 互转。
// 从 FolderMarker.cs 拆出来 —— 纯数学，和文件/注册表无关，改配色时只看这一个文件。

internal static partial class Program
{
    /// <summary>整张位图按 RecolorSandwich 的壳分支同一公式做 HSV 换色相</summary>
    private static void TintBitmapHsv(Bitmap bmp, double hueN, double satTarget)
    {
        bool gray = satTarget <= 0.001;
        Rectangle r = new Rectangle(0, 0, bmp.Width, bmp.Height);
        BitmapData bd = bmp.LockBits(r, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            int stride = bd.Stride;
            byte[] buf = new byte[stride * bmp.Height];
            Marshal.Copy(bd.Scan0, buf, 0, buf.Length);

            for (int y = 0; y < bmp.Height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < bmp.Width; x++)
                {
                    int i = row + x * 4;
                    if (buf[i + 3] == 0) { continue; }
                    double bv = buf[i] / 255.0, gv = buf[i + 1] / 255.0, rv = buf[i + 2] / 255.0;
                    if (gray)
                    {
                        int lv = (int)((0.299 * rv + 0.587 * gv + 0.114 * bv) * 255.0 * 0.72 + 0.5);
                        if (lv > 255) { lv = 255; }
                        buf[i] = (byte)lv; buf[i + 1] = (byte)lv; buf[i + 2] = (byte)lv;
                    }
                    else
                    {
                        double hh, ss, vv;
                        RgbToHsv(rv, gv, bv, out hh, out ss, out vv);
                        double ns = satTarget * ss / 0.9;
                        if (ns > 1.0) { ns = 1.0; }
                        double nr, ng, nb;
                        HsvToRgb(hueN, ns, vv, out nr, out ng, out nb);
                        buf[i] = ToByte(nb); buf[i + 1] = ToByte(ng); buf[i + 2] = ToByte(nr);
                    }
                }
            }
            Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
        }
        finally { bmp.UnlockBits(bd); }
    }

    /// <summary>闭合文件夹壳（imageres 3 号，实心没有开口），小尺寸帧用它</summary>
    private static Bitmap closedShell;

    private static Bitmap LoadClosedShell()
    {
        if (closedShell != null) { return closedShell; }
        try
        {
            string dll = Path.Combine(Environment.SystemDirectory, "imageres.dll");
            closedShell = ExtractIconBitmap(dll, 3, 256);
        }
        catch { }
        return closedShell;
    }
    /// <summary>目录里按资源管理器自然序的第一个文件，用于内容变化签名</summary>
    /// <summary>能拿来当文件夹封面的扩展名（图片/视频）。</summary>
    private static readonly string[] MediaExts = new string[]
    {
        ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".gif", ".webp", ".bmp",
        ".tif", ".tiff", ".heic", ".heif", ".avif", ".ico",
        ".mp4", ".m4v", ".mov", ".avi", ".mkv", ".wmv", ".webm", ".mpg", ".mpeg", ".m2ts", ".ts"
    };

    private static string FindFirstMediaFile(string folder)
    {
        try
        {
            List<string> files = new List<string>(Directory.EnumerateFiles(Io(folder)));
            files.Sort((a, b) => StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b)));
            foreach (string f in files)
            {
                string name = Path.GetFileName(f);

                // 只认图片/视频。以前把 desktop.ini 之外的第一名直接当封面，
                // 结果「只有一个 txt 的文件夹」也被认为有内容，每轮白跑一次合成。
                string ext = Path.GetExtension(name).ToLowerInvariant();
                if (Array.IndexOf(MediaExts, ext) < 0) { continue; }

                return f;
            }
        }
        catch { }
        return null;
    }
    /// <summary>老式 24bpp 图标没有 alpha 通道，透明度只能从 AND 掩码里读</summary>
    private static void ApplyMaskAlpha(IntPtr hdc, IntPtr hbmMask, int w, int h, byte[] bits)
    {
        if (hbmMask == IntPtr.Zero)
        {
            for (int i = 3; i < bits.Length; i += 4)
            {
                bits[i] = 255;
            }
            return;
        }

        BITMAPINFOHEADER bi = new BITMAPINFOHEADER();
        bi.biSize = 40;
        bi.biWidth = w;
        bi.biHeight = -h;
        bi.biPlanes = 1;
        bi.biBitCount = 1;
        bi.biCompression = 0;

        int stride = ((w + 31) / 32) * 4;
        byte[] mask = new byte[stride * h];
        IntPtr dc = hdc == IntPtr.Zero ? GetDC(IntPtr.Zero) : hdc;

        try
        {
            if (GetDIBits(dc, hbmMask, 0, (uint)h, mask, ref bi, 0) == 0)
            {
                for (int i = 3; i < bits.Length; i += 4)
                {
                    bits[i] = 255;
                }
                return;
            }
        }
        finally
        {
            if (hdc == IntPtr.Zero) { ReleaseDC(IntPtr.Zero, dc); }
        }

        for (int y = 0; y < h; y++)
        {
            int mrow = y * stride;
            int prow = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                bool transparent = (mask[mrow + (x >> 3)] & (0x80 >> (x & 7))) != 0;
                bits[prow + x * 4 + 3] = transparent ? (byte)0 : (byte)255;
            }
        }
    }



    private static void RgbToHsv(double r, double g, double b, out double h, out double s, out double v)
    {
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        v = max;

        double d = max - min;
        s = max <= 0 ? 0 : d / max;
        h = 0;

        if (d <= 0)
        {
            return;
        }

        if (max == r)
        {
            h = (g - b) / d;
            if (h < 0) { h += 6.0; }
        }
        else if (max == g)
        {
            h = (b - r) / d + 2.0;
        }
        else
        {
            h = (r - g) / d + 4.0;
        }

        h /= 6.0;
    }

    private static void HsvToRgb(double h, double s, double v, out double r, out double g, out double b)
    {
        if (s <= 0)
        {
            r = g = b = v;
            return;
        }

        double hh = h * 6.0;
        int i = (int)Math.Floor(hh);
        double f = hh - i;
        double p = v * (1.0 - s);
        double q = v * (1.0 - s * f);
        double t = v * (1.0 - s * (1.0 - f));

        switch (i % 6)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }
    }
}
