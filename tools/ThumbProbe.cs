using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

// 诊断用：问 Shell「资源管理器此刻会给这个文件夹画什么图」。
// 走的是缩略图管线（IShellItemImageFactory），所以能看出「缩略图会不会盖掉自定义图标」。
// SHGetFileInfo 只返回图标，看不到缩略图，排查图标不生效时会误判。
internal static class ThumbProbe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
        public SIZE(int x, int y) { cx = x; cy = y; }
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private const int SIIGBF_RESIZETOFIT = 0x00;
    private const int SIIGBF_ICONONLY = 0x04;
    private const int SIIGBF_THUMBNAILONLY = 0x08;

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("usage: ThumbProbe.exe <folder> <out.png> [icon|thumb]");
            return 1;
        }

        int flags = SIIGBF_RESIZETOFIT;
        if (args.Length > 2 && args[2] == "icon") { flags = SIIGBF_ICONONLY; }
        if (args.Length > 2 && args[2] == "thumb") { flags = SIIGBF_THUMBNAILONLY; }

        IShellItemImageFactory factory;
        try
        {
            SHCreateItemFromParsingName(args[0], IntPtr.Zero,
                new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), out factory);
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERR create: " + ex.Message);
            return 2;
        }

        IntPtr hbm;
        int hr = factory.GetImage(new SIZE(256, 256), flags, out hbm);
        if (hr != 0 || hbm == IntPtr.Zero)
        {
            Console.WriteLine("ERR getimage hr=0x" + hr.ToString("X8"));
            return 3;
        }

        try
        {
            using (Bitmap bmp = Image.FromHbitmap(hbm))
            {
                bmp.Save(args[1], ImageFormat.Png);

                long r = 0, g = 0, b = 0, n = 0;
                for (int y = 0; y < bmp.Height; y += 2)
                {
                    for (int x = 0; x < bmp.Width; x += 2)
                    {
                        Color c = bmp.GetPixel(x, y);
                        if (c.A < 8) { continue; }
                        r += c.R; g += c.G; b += c.B; n++;
                    }
                }

                Console.WriteLine(string.Format("{0}x{1} avg={2:X2}{3:X2}{4:X2} -> {5}",
                    bmp.Width, bmp.Height, n == 0 ? 0 : r / n, n == 0 ? 0 : g / n,
                    n == 0 ? 0 : b / n, args[1]));
            }
        }
        finally
        {
            DeleteObject(hbm);
        }

        return 0;
    }
}
