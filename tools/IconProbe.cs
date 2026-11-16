using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

// 诊断用探针：以全新进程向 Shell 要「某个文件夹此刻的真实大图标」，
// 输出主色（色相）+ 平均色 + 像素指纹。进程是新的，所以不掺杂本进程缓存。
internal static class IconProbe
{
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("usage: IconProbe.exe <folder>");
            return 1;
        }

        SHFILEINFO info = new SHFILEINFO();
        IntPtr r = SHGetFileInfo(args[0], 0, ref info,
            (uint)Marshal.SizeOf(typeof(SHFILEINFO)), SHGFI_ICON | SHGFI_LARGEICON);

        if (r == IntPtr.Zero || info.hIcon == IntPtr.Zero)
        {
            Console.WriteLine("ERR no icon");
            return 2;
        }

        try
        {
            using (Icon ic = Icon.FromHandle(info.hIcon))
            using (Bitmap bmp = ic.ToBitmap())
            {
                int w = bmp.Width, h = bmp.Height;
                BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h),
                    ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                byte[] buf = new byte[bd.Stride * h];
                try { Marshal.Copy(bd.Scan0, buf, 0, buf.Length); }
                finally { bmp.UnlockBits(bd); }

                long rs = 0, gs = 0, bs = 0, n = 0;
                ulong hash = 1469598103934665603UL;
                double bestSat = -1;
                int bestHue = 0;

                for (int y = 0; y < h; y++)
                {
                    int rowBase = y * bd.Stride;
                    for (int x = 0; x < w; x++)
                    {
                        int o = rowBase + x * 4;
                        byte b = buf[o];
                        byte g = buf[o + 1];
                        byte rr = buf[o + 2];
                        byte a = buf[o + 3];
                        if (a < 8) continue;

                        rs += rr; gs += g; bs += b; n++;
                        hash = (hash ^ rr) * 1099511628211UL;
                        hash = (hash ^ g) * 1099511628211UL;
                        hash = (hash ^ b) * 1099511628211UL;

                        int mx = Math.Max(rr, Math.Max(g, b));
                        int mn = Math.Min(rr, Math.Min(g, b));
                        if (mx < 60) continue;
                        int d = mx - mn;
                        double s = (double)d / mx;
                        if (s <= bestSat) continue;
                        bestSat = s;

                        int hue;
                        if (d == 0) hue = 0;
                        else if (mx == rr) hue = (int)(60 * (((g - b) / (double)d) % 6));
                        else if (mx == g) hue = (int)(60 * (((b - rr) / (double)d) + 2));
                        else hue = (int)(60 * (((rr - g) / (double)d) + 4));
                        if (hue < 0) hue += 360;
                        bestHue = hue;
                    }
                }

                if (n == 0) { Console.WriteLine("ERR empty"); return 3; }

                string name;
                if (bestSat < 0.20) name = "greyish";
                else if (bestHue >= 340 || bestHue < 12) name = "RED";
                else if (bestHue < 38) name = "ORANGE";
                else if (bestHue < 68) name = "YELLOW";
                else if (bestHue < 160) name = "GREEN";
                else if (bestHue < 250) name = "BLUE";
                else if (bestHue < 335) name = "PURPLE";
                else name = "RED";

                Console.WriteLine(string.Format("{0}  hue={1} sat={2:0.00}  avg={3:X2}{4:X2}{5:X2}  hash={6:x16}",
                    name, bestHue, bestSat, rs / n, gs / n, bs / n, hash));
            }
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }

        return 0;
    }
}
