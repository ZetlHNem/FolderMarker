using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

// 把一个 DLL 里的图标批量导出成一张带编号的对照表，方便肉眼浏览
internal static class IconGallery
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint PrivateExtractIcons(string szFileName, int nIconIndex,
        int cxIcon, int cyIcon, IntPtr[] phicon, IntPtr piconid, uint nIcons, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static int Main(string[] args)
    {
        if (args.Length < 4)
        {
            Console.WriteLine("usage: IconGallery <dll> <first> <last> <out.png> [cell] [cols]");
            return 1;
        }

        string dll = args[0];
        int first = int.Parse(args[1]);
        int last = int.Parse(args[2]);
        string outp = args[3];
        int cell = args.Length > 4 ? int.Parse(args[4]) : 48;
        int cols = args.Length > 5 ? int.Parse(args[5]) : 16;

        int count = last - first + 1;
        int rows = (count + cols - 1) / cols;

        using (Bitmap sheet = new Bitmap(cols * cell, rows * (cell + 14), PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.White);
            using (Font font = new Font("Consolas", 7))
            using (Brush brush = Brushes.Black)
            {
                for (int i = 0; i < count; i++)
                {
                    int idx = first + i;
                    int cx = (i % cols) * cell;
                    int cy = (i / cols) * (cell + 14);

                    IntPtr[] icons = new IntPtr[1];
                    uint got = PrivateExtractIcons(dll, idx, cell, cell, icons, IntPtr.Zero, 1, 0);
                    if (got == 0 || icons[0] == IntPtr.Zero)
                    {
                        g.DrawString(idx.ToString(), font, Brushes.LightGray, cx + 2, cy + cell + 1);
                        continue;
                    }

                    try
                    {
                        using (Icon ic = Icon.FromHandle(icons[0]))
                        {
                            g.DrawIcon(ic, cx + (cell - ic.Width) / 2, cy + (cell - ic.Height) / 2);
                        }
                    }
                    finally
                    {
                        DestroyIcon(icons[0]);
                    }

                    g.DrawString(idx.ToString(), font, brush, cx + 2, cy + cell + 1);
                }
            }

            sheet.Save(outp, ImageFormat.Png);
            Console.WriteLine("OK " + count + " 个图标 -> " + outp);
        }

        return 0;
    }
}
