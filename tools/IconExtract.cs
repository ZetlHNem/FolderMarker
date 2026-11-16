using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

// 抓取 shell32.dll / imageres.dll 里指定索引的图标，导出 PNG
internal static class IconExtract
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint PrivateExtractIcons(string szFileName, int nIconIndex,
        int cxIcon, int cyIcon, IntPtr[] phicon, IntPtr piconid, uint nIcons, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("usage: IconExtract <dllPath> <index> <out.png> [size]");
            return 1;
        }

        string dll = args[0];
        int index = int.Parse(args[1]);
        string outp = args[2];
        int size = args.Length > 3 ? int.Parse(args[3]) : 256;

        IntPtr[] icons = new IntPtr[1];
        uint got = PrivateExtractIcons(dll, index, size, size, icons, IntPtr.Zero, 1, 0);
        if (got == 0 || icons[0] == IntPtr.Zero)
        {
            Console.WriteLine("ERR 抓不到 index=" + index);
            return 2;
        }

        try
        {
            using (Icon ic = Icon.FromHandle(icons[0]))
            using (Bitmap bmp = ic.ToBitmap())
            {
                // ToBitmap 会丢 alpha，用 GDI 重新取
                bmp.Save(outp, ImageFormat.Png);
                Console.WriteLine(string.Format("OK {0}x{1} -> {2}", bmp.Width, bmp.Height, outp));
            }
        }
        finally
        {
            DestroyIcon(icons[0]);
        }

        return 0;
    }
}
