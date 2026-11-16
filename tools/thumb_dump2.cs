using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

internal static class ThumbDump2
{
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx; public int cy; }
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm); }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string pszPath, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);

    private static int Main(string[] args)
    {
        if (args.Length < 2) { return 1; }
        IShellItemImageFactory f;
        SHCreateItemFromParsingName(args[0], IntPtr.Zero, new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), out f);
        // flags: 0=默认(缩略图优先) 4=ICONONLY(只图标)
        int flags = args.Length > 2 ? int.Parse(args[2]) : 0;
        IntPtr hbm;
        int hr = f.GetImage(new SIZE { cx = 256, cy = 256 }, flags, out hbm);
        if (hr != 0 || hbm == IntPtr.Zero) { Console.WriteLine("ERR 0x" + hr.ToString("X8")); return 3; }
        using (Bitmap bmp = Image.FromHbitmap(hbm))
        {
            long r=0,g=0,b=0,n=0;
            for (int y=0;y<bmp.Height;y+=3) for(int x=0;x<bmp.Width;x+=3){ Color c=bmp.GetPixel(x,y); if(c.A<8)continue; r+=c.R;g+=c.G;b+=c.B;n++; }
            Console.WriteLine(string.Format("{0}x{1} avg={2:X2}{3:X2}{4:X2} flags={5}", bmp.Width, bmp.Height, n==0?0:r/n, n==0?0:g/n, n==0?0:b/n, flags));
            bmp.Save(args[1], ImageFormat.Png);
        }
        DeleteObject(hbm);
        return 0;
    }
}
