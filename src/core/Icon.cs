using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// 图标合成：纯色 ico 生成、三明治（壳+内容预览）合成、原生图抓取与校验。
// 从 FolderMarker.cs 拆出来。这块和 ini 读写耦合很紧（要读颜色/签名），
// 但内部自成体系，独立成文件后改合成算法不用再在大文件里找。

internal static partial class Program
{
    /// <summary>
    /// 自定义颜色没有预生成的内置 ico：把系统纯壳染成目标色现做一颗。
    /// 落在 cache 里、名字带路径哈希（&lt;hash&gt;-c&lt;hex&gt;.ico），纯色和内容
    /// 三明治共用同一个名字 —— 内容增删时签名机制自然在纯色/三明治之间切换。
    /// </summary>
    private static void BuildPlainCustomIco(string outPath, double hue, double sat)
    {
        Bitmap shell = LoadShell();
        if (shell == null) { throw new InvalidOperationException("无法加载系统纯壳"); }

        List<Bitmap> frames = new List<Bitmap>();
        try
        {
            foreach (int size in IconSizes)
            {
                Bitmap f = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(f))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(shell, new Rectangle(0, 0, size, size));
                }
                TintBitmapHsv(f, hue / 360.0, sat);
                frames.Add(f);
            }

            Directory.CreateDirectory(Io(Path.GetDirectoryName(outPath)));
            if (!WriteIco(outPath, frames))
            {
                throw new InvalidOperationException("写 ico 失败");
            }
        }
        finally
        {
            foreach (Bitmap b in frames) { try { b.Dispose(); } catch { } }
        }
    }

    // ================= 取色器配色服务（ColorPickerWindow 用） =================

    /// <summary>预览用系统纯壳缓存（取色器拖动时每帧重染，不能每次都解 imageres）</summary>
    private static Bitmap _previewShell;

    /// <summary>
    /// 取色器实时预览：把系统纯壳按目标色染成 size 见方的位图。
    /// 走的是和最终 ico 完全相同的染色管线（LoadShell + TintBitmapHsv），
    /// 保证「预览里看到的壳色 = 应用后文件夹的颜色」。
    /// </summary>
    internal static Bitmap PreviewFolder(int size, double hue, double sat)
    {
        try
        {
            if (_previewShell == null)
            {
                _previewShell = LoadShell();
            }
            if (_previewShell == null) { return null; }

            Bitmap f = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(f))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(_previewShell, new Rectangle(0, 0, size, size));
            }
            TintBitmapHsv(f, hue / 360.0, sat);
            return f;
        }
        catch { return null; }
    }

    /// <summary>
    /// RGB → 实际生效的 (hue, sat)。规则和 ResolveColor 对 c&lt;RRGGBB&gt; 的解析完全一样：
    /// 饱和度极低的颜色按纯灰处理（sat=0，hue 忽略）。取色器预览必须用这份结果，
    /// 否则预览会把一个「看起来有色其实会变灰」的颜色画成彩色的。
    /// </summary>
    internal static void RgbToEffectiveHueSat(int rgb, out double hue, out double sat)
    {
        double r = ((rgb >> 16) & 255) / 255.0;
        double g = ((rgb >> 8) & 255) / 255.0;
        double b = (rgb & 255) / 255.0;
        double hh, ss, vv;
        RgbToHsv(r, g, b, out hh, out ss, out vv);
        if (ss <= 0.03)
        {
            hue = 0;
            sat = 0;
        }
        else
        {
            hue = hh * 360.0;
            sat = ss;
        }
    }

    /// <summary>文件夹当前的颜色 token（没标记/读不到返回 null）。取色器用它做初始色。</summary>
    internal static string CurrentFolderColor(string folder)
    {
        try
        {
            string ini = Path.Combine(Io(folder), "desktop.ini");
            if (!File.Exists(ini)) { return null; }
            string iconName, color;
            bool isCache;
            if (TryReadMarkedIcon(ini, out iconName, out color, out isCache))
            {
                return color;
            }
        }
        catch { }
        return null;
    }

    /// <summary>最近使用的自定义色（6 位 hex，新的在前，最多 8 个）</summary>
    internal static string[] ReadRecentColors()
    {
        try
        {
            string path = Path.Combine(DataDir(), "recent_colors.txt");
            if (!File.Exists(Io(path))) { return new string[0]; }

            List<string> list = new List<string>();
            foreach (string raw in File.ReadAllLines(Io(path), Encoding.UTF8))
            {
                string t = raw.Trim().TrimStart('#').ToLowerInvariant();
                if (t.Length == 6 && IsHex6(t) && !list.Contains(t) && list.Count < 8)
                {
                    list.Add(t);
                }
            }
            return list.ToArray();
        }
        catch { return new string[0]; }
    }

    /// <summary>记一笔最近用过的颜色（去重、置顶、最多 8 个）</summary>
    internal static void PushRecentColor(string hex6)
    {
        try
        {
            string t = (hex6 ?? "").Trim().TrimStart('#').ToLowerInvariant();
            if (t.Length != 6 || !IsHex6(t)) { return; }

            List<string> list = new List<string>();
            list.Add(t);
            foreach (string old in ReadRecentColors())
            {
                if (!old.Equals(t, StringComparison.OrdinalIgnoreCase) && list.Count < 8)
                {
                    list.Add(old);
                }
            }

            string path = Path.Combine(DataDir(), "recent_colors.txt");
            Directory.CreateDirectory(Io(DataDir()));
            File.WriteAllLines(Io(path), list.ToArray(), new UTF8Encoding(false));
        }
        catch { }
    }

    private static bool IsHex6(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
            if (!hex) { return false; }
        }
        return true;
    }

    /// <summary>原版三明治缓存：cache\native\&lt;hash&gt;.png，绑定内容签名，换色复用免 rename 舞步</summary>
    private static string NativeCachePath(string folder)
    {
        return Path.Combine(Path.Combine(CacheIconDir(), "native"), PathHash(folder) + ".png");
    }

    /// <summary>
    /// 判定一张抓到的原版缩略图是不是可信的三明治（大片壳 + 一块内容）。
    /// 缓存命中时和实时抓取后都过一遍，坏图（误缓存了我们自己的彩色 ico、
    /// 纯色文件夹图标、半张残影）一律拒绝，宁可重抓也不把脏图写进缓存。
    /// </summary>
    private static bool IsPlausibleSandwichNative(Bitmap native, Bitmap shell)
    {
        if (native == null || shell == null) { return false; }
        if (native.Width != shell.Width || native.Height != shell.Height) { return false; }
        if (native.Width != 256) { return false; }

        Rectangle r = new Rectangle(0, 0, 256, 256);
        BitmapData dn = null, ds = null;
        try
        {
            dn = native.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            ds = shell.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int sn = dn.Stride, sw = ds.Stride;
            byte[] bn = new byte[sn * 256];
            byte[] bs = new byte[sw * 256];
            Marshal.Copy(dn.Scan0, bn, 0, bn.Length);
            Marshal.Copy(ds.Scan0, bs, 0, bs.Length);

            int hardBg = 0, hardContent = 0;
            for (int y = 0; y < 256; y++)
            {
                int a = y * sn, b = y * sw;
                for (int x = 0; x < 256; x++)
                {
                    int ia = a + x * 4, ib = b + x * 4;
                    if (bs[ib + 3] == 0) { continue; }
                    double bv = bn[ia] / 255.0, gv = bn[ia + 1] / 255.0, rv = bn[ia + 2] / 255.0;
                    double sbv = bs[ib] / 255.0, sgv = bs[ib + 1] / 255.0;
                    int d = Math.Abs(bn[ia] - bs[ib])
                          + Math.Abs(bn[ia + 1] - bs[ib + 1])
                          + Math.Abs(bn[ia + 2] - bs[ib + 2]);
                    double hh, ssv, vv;
                    RgbToHsv(rv, gv, bv, out hh, out ssv, out vv);
                    bool veil = (gv - sgv) * 255.0 > 15 && (bv - sbv) * 255.0 > 50
                             && hh >= 40.0 / 360.0 && hh <= 48.0 / 360.0
                             && ssv > 0.10 && vv > 0.80;
                    if (d <= 8 || veil) { hardBg++; }
                    else { hardContent++; }
                }
            }
            return hardBg >= 5000 && hardContent >= 600;
        }
        catch { return false; }
        finally
        {
            if (dn != null) { native.UnlockBits(dn); }
            if (ds != null) { shell.UnlockBits(ds); }
        }
    }

    /// <summary>
    /// 抓文件夹的 Windows 原版三明治。文件夹已被标记时 shell 只会返回我们自己的 ico，
    /// 所以临时把 desktop.ini 移走（二进制重命名，保留 UTF-16 编码与系统/隐藏属性），
    /// 抓完立刻原样移回。返回 256×256 位图。
    /// </summary>
    private static Bitmap GrabNativeSandwich(string folder)
    {
        string ini = Path.Combine(Io(folder), "desktop.ini");
        string parked = ini + ".fm_tmp";
        bool moved = false;
        try
        {
            // 上次崩溃残留优先恢复：ini 不在了但 .fm_tmp 还在，先移回去，
            // 再走下面的正常「摘标记 → 抓 → 移回」流程
            if (!File.Exists(ini) && File.Exists(parked))
            {
                try { File.Move(parked, ini); }
                catch { }
            }

            // 只有当前真的挂着我们的图标才需要摘标记。首次标记时文件夹
            // 本来就没有 IconResource，直接抓，省掉一次 rename + shell 通知。
            bool markedNow = false;
            if (File.Exists(ini))
            {
                string curIcon = ReadIconResourceRaw(ini);
                markedNow = !string.IsNullOrEmpty(curIcon);
            }

            if (markedNow)
            {
                if (File.Exists(parked)) { try { File.Delete(parked); } catch { } }
                File.Move(ini, parked);
                moved = true;
                NotifyPath(SHCNE_UPDATEDIR, SHCNF_PATHW_FLUSH, folder, IntPtr.Zero);
            }

            for (int i = 0; i < 3; i++)
            {
                Bitmap b = GrabShellThumbnail(folder, 256);
                if (b != null)
                {
                    if (b.Width == 256 && b.Height == 256) { return b; }
                    Bitmap fit = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(fit))
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(b, new Rectangle(0, 0, 256, 256));
                    }
                    b.Dispose();
                    return fit;
                }
                NotifyPath(SHCNE_UPDATEDIR, SHCNF_PATHW_FLUSH, folder, IntPtr.Zero);
                System.Threading.Thread.Sleep(150);
            }
            return null;
        }
        catch { return null; }
        finally
        {
            if (moved) { try { File.Move(parked, ini); } catch { } }
        }
    }

    /// <summary>
    /// 换壳：只改「属于文件夹外壳」的像素，内容预览原样保留。
    /// 判据来自系统纯壳图（imageres 5+6）：与纯壳逐像素一致（d≤8），或被
    /// Windows 半透明白纱提亮的浅色底板（G/B 明显高于纯壳、黄相、高亮），
    /// 都属于壳。硬分类之后两步软化，消除二值边界留下的杂色：
    ///   · 形态学闭合（膨胀→腐蚀）补上壳区域里 1~2px 的漏网洞 ——
    ///     native 缩略图在标签/边缘处的抗锯齿与纯壳有细微差异（d≈27），
    ///     硬分类会误当内容保留成黄色杂点；
    ///   · 3×3 均值模糊两遍，让壳/内容交界在内容侧形成渐变；
    ///   · 最终 mask 取 max(闭合, 模糊)：背景侧永远实心，细窄的壳结构
    ///     （如纸张折角缺口线）不会被模糊削弱。
    /// 输出 = native + (染色 - native) × mask —— 混合像素按比例合成，
    /// 不再非黑即白，抗锯齿过渡自然。alpha 取纯壳形状。
    /// 同一份统计顺带判定这张图是不是「大片壳 + 一块内容」的原版三明治。
    /// </summary>
    private static Bitmap RecolorSandwich(Bitmap native, Bitmap shell, double hue, double sat)
    {
        double satTarget = sat;
        bool gray = satTarget <= 0.001;

        const int N = 256;
        Bitmap result = new Bitmap(N, N, PixelFormat.Format32bppArgb);
        Rectangle r = new Rectangle(0, 0, N, N);
        BitmapData dn = native.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        BitmapData ds = shell.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        BitmapData dr = result.LockBits(r, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        bool ok = false;

        try
        {
            int sn = dn.Stride, sw = ds.Stride, so = dr.Stride;
            byte[] bn = new byte[sn * N];
            byte[] bs = new byte[sw * N];
            byte[] bo = new byte[so * N];
            Marshal.Copy(dn.Scan0, bn, 0, bn.Length);
            Marshal.Copy(ds.Scan0, bs, 0, bs.Length);

            // ---- 1. 硬分类：d≤8 或 veil => 壳；否则内容 ----
            byte[] hard = new byte[N * N];
            int hardBg = 0, hardContent = 0;
            for (int y = 0; y < N; y++)
            {
                int a = y * sn, b = y * sw;
                for (int x = 0; x < N; x++)
                {
                    int ia = a + x * 4, ib = b + x * 4;
                    if (bs[ib + 3] == 0) { continue; }
                    double bv = bn[ia] / 255.0, gv = bn[ia + 1] / 255.0, rv = bn[ia + 2] / 255.0;
                    double sbv = bs[ib] / 255.0, sgv = bs[ib + 1] / 255.0;
                    int d = Math.Abs(bn[ia] - bs[ib])
                          + Math.Abs(bn[ia + 1] - bs[ib + 1])
                          + Math.Abs(bn[ia + 2] - bs[ib + 2]);
                    double hh, ssv, vv;
                    RgbToHsv(rv, gv, bv, out hh, out ssv, out vv);
                    bool veil = (gv - sgv) * 255.0 > 15 && (bv - sbv) * 255.0 > 50
                             && hh >= 40.0 / 360.0 && hh <= 48.0 / 360.0
                             && ssv > 0.10 && vv > 0.80;
                    if (d <= 8 || veil) { hard[y * N + x] = 255; hardBg++; }
                    else { hardContent++; }
                }
            }

            // 原版三明治 = 大片壳 + 一块内容；纯色 ico / 空文件夹都不满足
            ok = hardBg >= 5000 && hardContent >= 600;

            byte[] clo = null, blur = null;
            if (ok)
            {
                // ---- 2. 形态学闭合：膨胀 -> 腐蚀 ----
                byte[] dil = new byte[N * N];
                clo = new byte[N * N];
                blur = new byte[N * N];
                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        int i = y * N + x;
                        if (bs[y * sw + x * 4 + 3] < 200) { dil[i] = hard[i]; }
                        else { dil[i] = Fold3x3(hard, bs, sw, x, y, 0); }
                    }
                }
                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        int i = y * N + x;
                        if (bs[y * sw + x * 4 + 3] < 200) { clo[i] = dil[i]; }
                        else { clo[i] = Fold3x3(dil, bs, sw, x, y, 1); }
                    }
                }
                // ---- 3. 模糊两遍 ----
                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        int i = y * N + x;
                        if (bs[y * sw + x * 4 + 3] < 200) { blur[i] = clo[i]; }
                        else { blur[i] = Fold3x3(clo, bs, sw, x, y, 2); }
                    }
                }
                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        int i = y * N + x;
                        if (bs[y * sw + x * 4 + 3] < 200) { continue; }
                        byte b2 = Fold3x3(blur, bs, sw, x, y, 2);
                        if (b2 > clo[i]) { clo[i] = b2; }   // max(闭合, 模糊)
                    }
                }

                // ---- 4. 比例合成：out = native + (染色 - native) × mask ----
                for (int y = 0; y < N; y++)
                {
                    int a = y * sn, b = y * sw, o = y * so;
                    for (int x = 0; x < N; x++)
                    {
                        int ia = a + x * 4, ib = b + x * 4, io = o + x * 4;
                        if (bs[ib + 3] == 0) { continue; }
                        double m = clo[y * N + x] / 255.0;
                        if (m <= 0)
                        {
                            bo[io] = bn[ia]; bo[io + 1] = bn[ia + 1]; bo[io + 2] = bn[ia + 2];
                        }
                        else
                        {
                            double bv = bn[ia] / 255.0, gv = bn[ia + 1] / 255.0, rv = bn[ia + 2] / 255.0;
                            double ob, og, orr;
                            if (gray)
                            {
                                int lv = (int)((0.299 * rv + 0.587 * gv + 0.114 * bv) * 255.0 * 0.72 + 0.5);
                                if (lv > 255) { lv = 255; }
                                ob = lv; og = lv; orr = lv;
                            }
                            else
                            {
                                double hh, ssv, vv;
                                RgbToHsv(rv, gv, bv, out hh, out ssv, out vv);
                                double ns = satTarget * ssv / 0.9;
                                if (ns > 1.0) { ns = 1.0; }
                                double nr, ng, nb;
                                HsvToRgb(hue, ns, vv, out nr, out ng, out nb);
                                orr = nr * 255.0; og = ng * 255.0; ob = nb * 255.0;
                            }
                            bo[io]     = (byte)(bn[ia]     + (ob  - bn[ia]     ) * m + 0.5);
                            bo[io + 1] = (byte)(bn[ia + 1] + (og  - bn[ia + 1] ) * m + 0.5);
                            bo[io + 2] = (byte)(bn[ia + 2] + (orr - bn[ia + 2] ) * m + 0.5);
                        }
                        bo[io + 3] = bs[ib + 3];
                    }
                }

                Marshal.Copy(bo, 0, dr.Scan0, bo.Length);
            }
        }
        finally
        {
            native.UnlockBits(dn);
            shell.UnlockBits(ds);
            result.UnlockBits(dr);
        }

        if (!ok) { result.Dispose(); return null; }
        return result;
    }

    /// <summary>3×3 邻域聚合，只统计纯壳不透明的像素。mode: 0=max 1=min 2=avg</summary>
    private static byte Fold3x3(byte[] src, byte[] bs, int sw, int x, int y, int mode)
    {
        const int N = 256;
        int mx = 0, mn = 255, sum = 0, cnt = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || nx >= N || ny < 0 || ny >= N) { continue; }
                if (bs[ny * sw + nx * 4 + 3] < 200) { continue; }
                byte t = src[ny * N + nx];
                if (t > mx) { mx = t; }
                if (t < mn) { mn = t; }
                sum += t; cnt++;
            }
        }
        if (cnt == 0) { return src[y * N + x]; }
        if (mode == 0) { return (byte)mx; }
        if (mode == 1) { return (byte)mn; }
        return (byte)(sum / cnt);
    }

    private static string TryBuildThumbnailIcon(string folder, string color, string outPath)
    {
        int ci;
        double hue, sat;
        if (!ResolveColor(color, out ci, out hue, out sat)) { return null; }
        double hueN = hue / 360.0;

        try
        {
            Bitmap shell = LoadShell();
            if (shell == null) { return null; }

            // 原版三明治缓存：命中（内容签名一致 + 可靠性校验通过）就跳过
            // 「摘 desktop.ini → 通知 shell → 重试」的整套流程，换色快且稳。
            string nativePath = NativeCachePath(folder);
            string sig = ComputeSignature(folder, null);
            Bitmap native = null;
            try
            {
                if (sig.Length > 0 && File.Exists(Io(nativePath)))
                {
                    Bitmap cand = new Bitmap(Io(nativePath));
                    if (IsPlausibleSandwichNative(cand, shell))
                    {
                        native = cand;
                    }
                    else
                    {
                        cand.Dispose();
                        try { File.Delete(Io(nativePath)); } catch { }
                        DebugLog("native cache: 校验失败，丢弃重抓 " + folder);
                    }
                }
            }
            catch { }

            if (native == null)
            {
                native = GrabNativeSandwich(folder);
                if (native != null && IsPlausibleSandwichNative(native, shell))
                {
                    try
                    {
                        Directory.CreateDirectory(Io(Path.GetDirectoryName(nativePath)));
                        native.Save(Io(nativePath), ImageFormat.Png);
                        WriteCacheSig(nativePath, sig);
                    }
                    catch { }
                }
            }

            if (native == null) { return null; }

            Bitmap colored = RecolorSandwich(native, shell, hueN, sat);
            if (colored == null && _shellIdxFromCache)
            {
                // 合成失败 + 索引来自跨进程缓存 => 缓存索引可能已过期
                //（Windows 更新挪动了文件夹图标位置）。丢缓存重新检测，重试一次。
                DebugLog("shell idx: 合成失败，怀疑缓存索引过期，重新检测");
                ResetShell();
                EnsureShellIndices();
                shell = LoadShell();
                if (shell != null)
                {
                    if (!IsPlausibleSandwichNative(native, shell))
                    {
                        // 索引变了会导致旧 native 的判定也变，缓存图直接弃用重抓
                        native.Dispose();
                        native = GrabNativeSandwich(folder);
                    }
                    if (native != null)
                    {
                        colored = RecolorSandwich(native, shell, hueN, sat);
                    }
                }
            }

            if (colored == null) { native.Dispose(); return null; }

            try
            {
                using (Bitmap colored2 = colored)
                // ≤32px 的小帧用「染色后的纯壳」：小尺寸下塞内容卡片只剩一团噪点，
                // 且与内容帧走同一染色公式，大小帧颜色完全一致
                using (Bitmap tintedShell = (Bitmap)shell.Clone())
                {
                    if (colored == null) { return null; }
                    TintBitmapHsv(tintedShell, hueN, sat);

                    List<Bitmap> frames = new List<Bitmap>();
                    try
                    {
                        foreach (int size in IconSizes)
                        {
                            Bitmap f = null;

                            if (size <= 32)
                            {
                                // 小尺寸用「闭合文件夹」上标色 —— Windows 原生同款结构：
                                // 小图标是闭合文件夹，中/大图标才是带缩略图的开壳。
                                // 预设色直接取内置闭合 ico 的对应帧（和纯色标记文件夹逐像素一致）；
                                // 自定义色拿 imageres 3 号闭合壳现染。
                                if (ci >= 0)
                                {
                                    string plainIco = Path.Combine(
                                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icons"),
                                        color + ".ico");
                                    if (File.Exists(Io(plainIco)))
                                    {
                                        f = ExtractIconBitmap(plainIco, 0, size);
                                    }
                                }

                                if (f == null)
                                {
                                    Bitmap cs = LoadClosedShell();
                                    if (cs != null)
                                    {
                                        f = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                                        using (Graphics g = Graphics.FromImage(f))
                                        {
                                            g.CompositingMode = CompositingMode.SourceCopy;
                                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                            g.DrawImage(cs, new Rectangle(0, 0, size, size));
                                        }
                                        TintBitmapHsv(f, hueN, sat);
                                    }
                                }
                            }

                            if (f == null)
                            {
                                // ≥40px：内容三明治；≤32px 且拿不到闭合壳时才退回染色开壳
                                Bitmap src = size <= 32 ? (Bitmap)tintedShell : colored2;
                                f = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                                using (Graphics g = Graphics.FromImage(f))
                                {
                                    g.CompositingMode = CompositingMode.SourceCopy;
                                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                    g.DrawImage(src, new Rectangle(0, 0, size, size));
                                }
                            }
                            frames.Add(f);
                        }

                        string dir = Path.GetDirectoryName(outPath);
                        Directory.CreateDirectory(Io(dir));
                        if (!WriteIco(outPath, frames)) { return null; }
                    }
                    finally
                    {
                        foreach (Bitmap f in frames) { try { f.Dispose(); } catch { } }
                    }
                }
            }
            finally
            {
                native.Dispose();
            }

            // 换色后清掉同文件夹的其它颜色 ico
            try
            {
                string dir = Path.GetDirectoryName(outPath);
                string hash = Path.GetFileNameWithoutExtension(outPath).Split('-')[0];
                foreach (string old in Directory.GetFiles(Io(dir), hash + "-*"))
                {
                    if (string.Equals(old, outPath, StringComparison.OrdinalIgnoreCase)) { continue; }

                    // 只清同一文件夹的旧颜色 ico。.sig 是签名、.lm 是纯色标记状态，
                    // 一起删会让下一轮又白扫一遍（并发换色时还可能删掉刚生效那颗）。
                    if (!string.Equals(Path.GetExtension(old), ".ico", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    File.Delete(old);
                }
            }
            catch { }

            return outPath;
        }
        catch (Exception ex)
        {
            TryLog(ex);
            return null;
        }
    }

    /// <summary>
    /// 向 shell 要一个路径当前的缩略图（图片/视频文件给内容画面；文件夹给「外壳+内容」合成图）。
    /// </summary>
    private static Bitmap GrabShellThumbnail(string path, int size)
    {
        IShellItemImageFactory factory = null;

        try
        {
            SHCreateItemFromParsingName(Plain(path), IntPtr.Zero,
                IID_IShellItemImageFactory, out factory);

            if (factory == null)
            {
                return null;
            }

            SIZE sz = new SIZE();
            sz.cx = size;
            sz.cy = size;

            IntPtr hbm;
            if (factory.GetImage(sz, 0, out hbm) != 0 || hbm == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return HBitmapToBitmap(hbm);
            }
            finally
            {
                DeleteObject(hbm);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            // 守护进程是常驻的：不显式释放 RCW 会一直攒 shell item，
            // 期间还可能占着原图文件（用户想删都删不掉）。
            if (factory != null)
            {
                try { Marshal.ReleaseComObject(factory); }
                catch { }
            }
        }
    }

    /// <summary>HBITMAP → 32bpp ARGB 位图</summary>
    private static Bitmap HBitmapToBitmap(IntPtr hbm)
    {
        BITMAP bm = new BITMAP();
        if (GetObject(hbm, Marshal.SizeOf(typeof(BITMAP)), ref bm) == 0)
        {
            return null;
        }

        int w = bm.bmWidth;
        int h = bm.bmHeight;
        if (w <= 0 || h <= 0)
        {
            return null;
        }

        BITMAPINFOHEADER bi = new BITMAPINFOHEADER();
        bi.biSize = 40;
        bi.biWidth = w;
        bi.biHeight = -h;
        bi.biPlanes = 1;
        bi.biBitCount = 32;
        bi.biCompression = 0;

        byte[] bits = new byte[w * h * 4];
        IntPtr hdc = GetDC(IntPtr.Zero);
        int lines;

        try
        {
            lines = GetDIBits(hdc, hbm, 0, (uint)h, bits, ref bi, 0);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }

        if (lines == 0)
        {
            return null;
        }

        // GetDIBits 用 BI_RGB 读 32bpp HBITMAP 时，alpha 通道可能整片为 0
        // （GDI 不填该字节）。此时位图实际是不透明的，需要补成 255；
        // 只要存在任一非零 alpha，就说明 shell 给了有效 alpha，原样保留。
        // 全程只依据 alpha 通道，不按像素颜色猜背景。
        bool hasAlpha = false;
        for (int i = 3; i < bits.Length; i += 4)
        {
            if (bits[i] != 0) { hasAlpha = true; break; }
        }
        if (!hasAlpha)
        {
            for (int i = 3; i < bits.Length; i += 4) { bits[i] = 255; }
        }


        Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            Marshal.Copy(bits, 0, bd.Scan0, bits.Length);
        }
        finally
        {
            bmp.UnlockBits(bd);
        }

        return bmp;
    }


    /// <summary>清除标记时顺手删掉这个文件夹的合成图标（各种颜色的都删），以及放进文件夹内的 FolderMarker.ico</summary>
    private static void DeleteTintedIcons(string folder)
    {
        try
        {
            string dir = CustomIconDir();
            if (Directory.Exists(Io(dir)))
            {
                // <hash>-<颜色>.ico 都归这个文件夹所有
                foreach (string f in Directory.GetFiles(Io(dir), PathHash(folder) + "-*"))
                {
                    try { File.Delete(f); }
                    catch { }
                }
            }
        }
        catch { }

        // 本机缓存里属于这个文件夹的 ico / 签名 / 状态（缓存不在被标记文件夹里，删它不会刷文件夹时间戳）
        DeleteCacheIcons(folder);

        // 删掉放进文件夹里的那个图标（跨电脑同步用的相对路径图标）
        try
        {
            string local = Io(Path.Combine(Plain(folder), LocalIconName));
            if (File.Exists(local))
            {
                File.SetAttributes(local, FileAttributes.Normal);
                File.Delete(local);
            }
        }
        catch { }
    }

    /// <summary>从 ico/exe/dll 里按尺寸取一张带 alpha 的位图</summary>
    private static Bitmap ExtractIconBitmap(string file, int index, int size)
    {
        IntPtr[] icons = new IntPtr[1];

        try
        {
            uint got = PrivateExtractIcons(file, index, size, size, icons, IntPtr.Zero, 1, 0);
            if (got == 0 || icons[0] == IntPtr.Zero)
            {
                return null;
            }

            return HIconToBitmap(icons[0]);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (icons[0] != IntPtr.Zero)
            {
                DestroyIcon(icons[0]);
            }
        }
    }

    /// <summary>HICON → 32bpp ARGB 位图（Icon.ToBitmap 会丢 alpha，只能自己 GetDIBits）</summary>
    private static Bitmap HIconToBitmap(IntPtr hIcon)
    {
        ICONINFO ii;
        if (!GetIconInfo(hIcon, out ii))
        {
            return null;
        }

        try
        {
            BITMAP bm = new BITMAP();
            if (GetObject(ii.hbmColor, Marshal.SizeOf(typeof(BITMAP)), ref bm) == 0)
            {
                return null;
            }

            int w = bm.bmWidth;
            int h = bm.bmHeight;
            if (w <= 0 || h <= 0)
            {
                return null;
            }

            BITMAPINFOHEADER bi = new BITMAPINFOHEADER();
            bi.biSize = 40;
            bi.biWidth = w;
            bi.biHeight = -h;          // 负高度 = 从上到下，省得再翻一遍
            bi.biPlanes = 1;
            bi.biBitCount = 32;
            bi.biCompression = 0;

            byte[] bits = new byte[w * h * 4];
            IntPtr hdc = GetDC(IntPtr.Zero);
            int lines;
            try
            {
                lines = GetDIBits(hdc, ii.hbmColor, 0, (uint)h, bits, ref bi, 0);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdc);
            }

            if (lines == 0)
            {
                return null;
            }

            bool hasAlpha = false;
            for (int i = 3; i < bits.Length; i += 4)
            {
                if (bits[i] != 0) { hasAlpha = true; break; }
            }

            if (!hasAlpha)
            {
                ApplyMaskAlpha(IntPtr.Zero, ii.hbmMask, w, h, bits);
            }

            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                Marshal.Copy(bits, 0, bd.Scan0, bits.Length);
            }
            finally
            {
                bmp.UnlockBits(bd);
            }

            return bmp;
        }
        finally
        {
            if (ii.hbmColor != IntPtr.Zero) { DeleteObject(ii.hbmColor); }
            if (ii.hbmMask != IntPtr.Zero) { DeleteObject(ii.hbmMask); }
        }
    }
}
