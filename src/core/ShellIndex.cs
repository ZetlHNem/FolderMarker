using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// 文件夹三明治图标层：加载系统纯色壳、探测 imageres.dll 背板/前盖索引并缓存。
// 从 FolderMarker.cs 拆出来 —— 内容预览图标的底座计算，与 Icon.cs 配合。

internal static partial class Program
{

    // ================= 带内容预览的文件夹图标 =================
    //
    // Windows 的「文件夹三明治」= imageres.dll 5 号背板 + 内容预览 + 6 号前盖。
    // 逐像素实测：5/6 叠出来的纯黄色壳，与 Windows 生成的三明治图里的壳像素
    // 完全相同（壳顶部 253,189,25、前盖 255,215,103 分毫不差）。
    //
    // 因此这里不推测任何几何、不手工摆内容、不按颜色擦背景：
    //   1. 取 Windows 生成的原版三明治（临时移走 desktop.ini 后抓）
    //   2. 纯壳图当参照，逐像素判「属于壳 / 属于内容」，
    //      再做形态学闭合 + 模糊得到软 mask
    //   3. out = native + (染色 - native) × mask：混合像素按比例合成，
    //      内容为主，壳/内容交界的抗锯齿自然过渡，不留杂色边
    //   4. alpha 取纯壳形状，透明区天然干净
    // 内容/卡片/padding/大小/位置全部来自 Windows 原图，没有可调参数。

    /// <summary>系统纯黄色文件夹壳（背板 5 + 前盖 6），进程内只加载一次</summary>
    private static Bitmap shellOriginal;

    private static Bitmap LoadShell()
    {
        if (shellOriginal != null) { return shellOriginal; }
        EnsureShellIndices();
        try
        {
            string dll = Path.Combine(Environment.SystemDirectory, "imageres.dll");
            using (Bitmap back = ExtractIconBitmap(dll, _shellBackIdx, 256))
            using (Bitmap front = ExtractIconBitmap(dll, _shellFrontIdx, 256))
            {
                if (back == null || front == null) { return null; }
                Bitmap shell = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(shell))
                {
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.DrawImage(back, new Rectangle(0, 0, 256, 256));
                    g.DrawImage(front, new Rectangle(0, 0, 256, 256));
                }
                shellOriginal = shell;
            }
        }
        catch (Exception ex) { TryLog(ex); }
        return shellOriginal;
    }

    // 纯壳用的 imageres 索引。不同 Windows 版本文件夹图标的位置可能不同，
    // 进程内首次用壳时拿临时文件夹的原版三明治验证一次，不对就自动搜索。
    private static int _shellBackIdx = 5;
    private static int _shellFrontIdx = 6;
    private static bool _shellIdxChecked;
    private static bool _shellIdxFromCache;   // 当前索引是否来自跨进程缓存（可信度低于实时验证）

    private static string ShellIdxCachePath()
    {
        return Path.Combine(Path.Combine(CacheIconDir(), "native"), "shell_idx.txt");
    }

    /// <summary>丢掉缓存的索引和已加载的纯壳，下次用壳时重新检测。索引过期自愈用。</summary>
    private static void ResetShell()
    {
        if (shellOriginal != null)
        {
            try { shellOriginal.Dispose(); } catch { }
            shellOriginal = null;
        }
        _shellIdxChecked = false;
        try { File.Delete(Io(ShellIdxCachePath())); } catch { }
    }

    /// <summary>进程内只验证一次：缓存索引 -> 验证 -> 不对就搜索并写回缓存</summary>
    private static void EnsureShellIndices()
    {
        if (_shellIdxChecked) { return; }
        _shellIdxChecked = true;

        try
        {
            // 跨进程缓存：上次搜到的索引直接用，省掉验证抓图（约 0.2s）
            string cached = null;
            try { cached = File.ReadAllText(Io(ShellIdxCachePath()), Encoding.UTF8); } catch { }
            if (!string.IsNullOrEmpty(cached))
            {
                string[] parts = cached.Split(',');
                int b, f;
                if (parts.Length == 2 && int.TryParse(parts[0], out b) && int.TryParse(parts[1], out f)
                    && b >= 0 && f >= 0)
                {
                    _shellBackIdx = b;
                    _shellFrontIdx = f;
                    _shellIdxFromCache = true;
                    return;
                }
            }

            Bitmap native = GrabIndexTestNative();
            if (native == null) { return; }   // 验证不了就保持 5/6

            try
            {
                if (CompositeShellScore(_shellBackIdx, _shellFrontIdx, native) >= 15000)
                {
                    // 验证通过也写缓存，后续进程直接信任（过期由自愈重试兜底）
                    try
                    {
                        Directory.CreateDirectory(Io(Path.GetDirectoryName(ShellIdxCachePath())));
                        File.WriteAllText(Io(ShellIdxCachePath()),
                            _shellBackIdx + "," + _shellFrontIdx, new UTF8Encoding(false));
                    }
                    catch { }
                    return;
                }

                int bestB = -1, bestF = -1, bestScore = 0;
                for (int b = 1; b <= 60; b++)
                {
                    int score = CompositeShellScore(b, b + 1, native);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestB = b; bestF = b + 1;
                    }
                }

                if (bestScore >= 15000)
                {
                    _shellBackIdx = bestB;
                    _shellFrontIdx = bestF;
                    _shellIdxFromCache = true;
                    try
                    {
                        Directory.CreateDirectory(Io(Path.GetDirectoryName(ShellIdxCachePath())));
                        File.WriteAllText(Io(ShellIdxCachePath()),
                            bestB + "," + bestF, new UTF8Encoding(false));
                    }
                    catch { }
                    DebugLog("shell idx: 索引修正为 " + bestB + "/" + bestF +
                             " (score=" + bestScore + ")");
                }
            }
            finally
            {
                native.Dispose();
            }
        }
        catch { }
    }

    /// <summary>叠出 back/front 两层的纯壳，与原版三明治逐像素比对，返回精确匹配的壳像素数</summary>
    private static int CompositeShellScore(int backIdx, int frontIdx, Bitmap native)
    {
        try
        {
            string dll = Path.Combine(Environment.SystemDirectory, "imageres.dll");
            using (Bitmap back = ExtractIconBitmap(dll, backIdx, 256))
            using (Bitmap front = ExtractIconBitmap(dll, frontIdx, 256))
            {
                if (back == null || front == null || back.Width != 256 || front.Width != 256)
                {
                    return 0;
                }

                using (Bitmap shell = new Bitmap(256, 256, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(shell))
                    {
                        g.CompositingMode = CompositingMode.SourceOver;
                        g.DrawImage(back, new Rectangle(0, 0, 256, 256));
                        g.DrawImage(front, new Rectangle(0, 0, 256, 256));
                    }

                    Rectangle r = new Rectangle(0, 0, 256, 256);
                    BitmapData ds = shell.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    BitmapData dn = native.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        int sw = ds.Stride, sn = dn.Stride;
                        byte[] bs = new byte[sw * 256];
                        byte[] bn = new byte[sn * 256];
                        Marshal.Copy(ds.Scan0, bs, 0, bs.Length);
                        Marshal.Copy(dn.Scan0, bn, 0, bn.Length);

                        int score = 0;
                        for (int y = 0; y < 256; y++)
                        {
                            int a = y * sn, b = y * sw;
                            for (int x = 0; x < 256; x++)
                            {
                                int ia = a + x * 4, ib = b + x * 4;
                                if (bs[ib + 3] == 0) { continue; }
                                int d = Math.Abs(bn[ia] - bs[ib])
                                      + Math.Abs(bn[ia + 1] - bs[ib + 1])
                                      + Math.Abs(bn[ia + 2] - bs[ib + 2]);
                                if (d <= 8) { score++; }
                            }
                        }
                        return score;
                    }
                    finally
                    {
                        shell.UnlockBits(ds);
                        native.UnlockBits(dn);
                    }
                }
            }
        }
        catch { return 0; }
    }

    /// <summary>
    /// 建一个带图片的临时文件夹，抓它的原版三明治给索引验证用。
    /// 没有桌面.ini，不会触发摘标记流程。
    /// </summary>
    private static Bitmap GrabIndexTestNative()
    {
        string dir = null;
        try
        {
            dir = Path.Combine(Path.GetTempPath(), "FolderMarkerIdxTest-" +
                Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);

            // 64×64 的测试图：一张有内容的图，Windows 就会给文件夹出三明治预览
            using (Bitmap test = new Bitmap(64, 64, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(test))
                {
                    g.Clear(Color.FromArgb(70, 130, 200));
                    using (SolidBrush br = new SolidBrush(Color.FromArgb(240, 180, 60)))
                    {
                        g.FillEllipse(br, 8, 8, 48, 48);
                    }
                }
                test.Save(Io(Path.Combine(dir, "a.png")), ImageFormat.Png);
            }

            return GrabNativeSandwich(dir);
        }
        catch { return null; }
        finally
        {
            if (dir != null)
            {
                try { Directory.Delete(Io(dir), true); } catch { }
            }
        }
    }

}
