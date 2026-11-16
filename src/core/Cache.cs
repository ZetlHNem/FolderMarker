using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// 图标缓存层：内容签名、纯色<->内容预览双向切换、缓存 ico 清理与降级。
// 从 FolderMarker.cs 拆出来 —— 这些函数只依赖同 class 的其它静态成员。

internal static partial class Program
{

    /// <summary>
    /// 内容签名：第一个图片/视频文件的 名字+大小+修改时间，再加文件夹自身修改时间。
    /// 增删文件会改文件夹 mtime，改封面内容会改文件 mtime —— 两种变化都能抓到。
    /// 格式：v2\t文件夹mtime\t媒体文件名\t大小\t媒体mtime
    /// </summary>
    private static string ContentSignature(string folder)
    {
        return ComputeSignature(folder, null);
    }

    /// <summary>
    /// 带「快路径」的签名计算：文件夹 mtime 没变、封面文件的大小/时间也没变，
    /// 就直接复用上次的签名，跳过整个目录的文件枚举。
    /// 巡检每分钟跑一次，对每个文件夹都 GetFiles 一遍是纯浪费 —— 大文件夹在共享盘上能占掉大半时间。
    /// </summary>
    private static string ComputeSignature(string folder, string cached)
    {
        long folderTicks = 0;
        try
        {
            DirectoryInfo di = new DirectoryInfo(Plain(folder));
            if (!di.Exists) { return ""; }
            folderTicks = di.LastWriteTimeUtc.Ticks;
        }
        catch { return ""; }

        string cachedName = "";
        long cachedSize = 0;
        long cachedTicks = 0;

        if (!string.IsNullOrEmpty(cached) && cached.StartsWith("v2\t", StringComparison.Ordinal))
        {
            string[] parts = cached.Split('\t');
            if (parts.Length >= 5)
            {
                long ft;
                if (long.TryParse(parts[1], out ft) && ft == folderTicks)
                {
                    cachedName = parts[2];
                    long.TryParse(parts[3], out cachedSize);
                    long.TryParse(parts[4], out cachedTicks);

                    if (cachedName.Length == 0)
                    {
                        // 上次就没有媒体文件，文件夹又没动过 → 现在也不会有
                        return cached;
                    }

                    try
                    {
                        FileInfo fi = new FileInfo(Path.Combine(Plain(folder), cachedName));
                        if (fi.Exists && fi.Length == cachedSize && fi.LastWriteTimeUtc.Ticks == cachedTicks)
                        {
                            return cached;
                        }
                    }
                    catch { }
                }
            }
        }

        try
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("v2\t").Append(folderTicks).Append('\t');

            string media = FindFirstMediaFile(folder);
            if (media != null)
            {
                FileInfo fi = new FileInfo(Plain(media));
                sb.Append(Path.GetFileName(Plain(media))).Append('\t')
                  .Append(fi.Exists ? fi.Length : 0).Append('\t')
                  .Append(fi.Exists ? fi.LastWriteTimeUtc.Ticks : 0);
            }

            return sb.ToString();
        }
        catch
        {
            return "";
        }
    }

    private static string SigPath(string icoPath)
    {
        return icoPath + ".sig";
    }

    private static void WriteCacheSig(string icoPath, string sig)
    {
        try { File.WriteAllText(Io(SigPath(icoPath)), sig ?? "", Encoding.UTF8); }
        catch { }
    }

    private static string ReadCacheSig(string icoPath)
    {
        try { return File.ReadAllText(Io(SigPath(icoPath)), Encoding.UTF8); }
        catch { return null; }
    }

    // ---------------- 纯色 <-> 内容预览 的双向切换 ----------------

    /// <summary>纯色图标的 IconResource 值（用环境变量，两台电脑各自解析到本机安装目录）</summary>
    private static string PlainIconValue(string color)
    {
        return "%LOCALAPPDATA%\\FolderMarker\\icons\\" + color + ".ico,0";
    }

    private static string CacheIconValue(string iconName)
    {
        return "%LOCALAPPDATA%\\FolderMarker\\cache\\" + iconName + ",0";
    }

    /// <summary>只改 IconResource 这一行，用户自己放在 ini 里的其它键原样保留</summary>
    private static bool SetIconValue(string iniPath, string value)
    {
        string dir = Path.GetDirectoryName(iniPath);
        DateTime write = DateTime.MinValue;
        DateTime access = DateTime.MinValue;

        // 写 desktop.ini 会把这层文件夹的修改时间刷成现在，用户看属性会以为被动过。
        // 记下来，写完还原（和标记时的处理一致）。
        try
        {
            write = Directory.GetLastWriteTimeUtc(dir);
            access = Directory.GetLastAccessTimeUtc(dir);
        }
        catch { }

        try
        {
            List<string> lines = SetIconResource(StripMarkKeys(ReadIni(iniPath)), value);
            WriteIni(iniPath, lines);
            try { File.SetAttributes(iniPath, FileAttributes.Hidden | FileAttributes.System); }
            catch { }

            if (access != DateTime.MinValue)
            {
                try { Directory.SetLastAccessTimeUtc(dir, access); }
                catch { }
            }

            if (write != DateTime.MinValue)
            {
                try { Directory.SetLastWriteTimeUtc(dir, write); }
                catch { }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static long FolderTicks(string folder)
    {
        try
        {
            DirectoryInfo di = new DirectoryInfo(Plain(folder));
            return di.Exists ? di.LastWriteTimeUtc.Ticks : 0;
        }
        catch { return 0; }
    }

    /// <summary>纯色标记的巡检状态：第一行是上次看到的文件夹 mtime，第二行是上次合成失败的签名</summary>
    private static string PlainStatePath(string folder, string color)
    {
        return Path.Combine(CacheIconDir(), PathHash(folder) + "-" + color + ".lm");
    }

    private static string[] ReadPlainState(string path)
    {
        try
        {
            if (!File.Exists(Io(path))) { return null; }
            return File.ReadAllLines(Io(path), Encoding.UTF8);
        }
        catch { return null; }
    }

    private static void WritePlainState(string path, long ticks, string sig)
    {
        try
        {
            Directory.CreateDirectory(CacheIconDir());
            File.WriteAllText(Io(path), ticks.ToString() + "\r\n" + (sig ?? ""), new UTF8Encoding(false));
        }
        catch { }
    }

    /// <summary>删掉缓存里属于这个文件夹的所有 ico / 签名 / 状态文件（不动被标记文件夹本身）</summary>
    private static void DeleteCacheIcons(string folder)
    {
        try
        {
            string cache = Io(CacheIconDir());
            if (!Directory.Exists(cache)) { return; }

            foreach (string f in Directory.GetFiles(cache, PathHash(folder) + "-*"))
            {
                try { File.Delete(f); }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>
    /// 缓存图标降级成纯色图标。
    /// 场景：文件夹里的图片/视频被删光了，或者换了台没有解码器的电脑 —— 不降级的话
    /// 图标会一直挂着那张已经不存在的旧照片，而且每轮巡检都白试一次。
    /// </summary>
    private static bool DowngradeToPlain(string folder, string iniPath, string color)
    {
        bool custom = IsCustomToken(color);
        // 自定义色的纯色 ico 也在 cache（<hash>-c<hex>.ico），ini 继续指它；
        // 不能走 DeleteCacheIcons（会把自己删掉），重建纯色版本并补签名即可。
        if (!SetIconValue(iniPath, custom
                ? CacheIconValue(CacheIconName(folder, color))
                : PlainIconValue(color)))
        {
            return false;
        }

        if (!custom)
        {
            DeleteCacheIcons(folder);           // 连同 .sig / .lm 一起清掉
        }
        else
        {
            try
            {
                int ci; double hue, sat;
                ResolveColor(color, out ci, out hue, out sat);
                string p = Path.Combine(CacheIconDir(), CacheIconName(folder, color));
                BuildPlainCustomIco(p, hue, sat);
            }
            catch { }
        }
        NotifyFolder(folder);
        NudgeWriteTime(folder, 1 + ValidColorIndex(color));

        // 签名必须在挪完时间之后写。写早了记录的是旧 mtime，下一轮必然判定
        // 「内容变了」→ 又降级、又挪时间，mtime 每轮 +2~4 秒，一天能跑进未来几十分钟。
        if (custom)
        {
            try
            {
                string p = Path.Combine(CacheIconDir(), CacheIconName(folder, color));
                WriteCacheSig(p, ContentSignature(folder));
            }
            catch { }
        }
        DebugLog("refresh downgrade: 没有可用的内容缩略图，改回纯色 " + color + " " + folder);
        return true;
    }

    /// <summary>颜色在 ValidColors 里的序号，没有就返回 0（给 mtime 偏移用）</summary>
    private static int ValidColorIndex(string color)
    {
        int i = Array.IndexOf(ValidColors, color);
        return i < 0 ? 0 : i;
    }

    /// <summary>
    /// 纯色标记发现文件夹里出现了图片/视频 → 升级成带内容预览的图标。
    /// 只在「文件夹 mtime 变了」时才去枚举文件，平时一轮巡检只有一次 stat 的开销。
    /// 合成失败会记住这次的签名，同样内容不会反复重试。
    /// </summary>
    private static bool TryUpgradePlain(string folder, string iniPath, string color,
        bool force, out string liveIcon)
    {
        liveIcon = color + ".ico";

        long ticks = FolderTicks(folder);
        if (ticks == 0)
        {
            return false;   // 文件夹当前不可访问
        }

        string statePath = PlainStatePath(folder, color);
        string[] state = ReadPlainState(statePath);

        if (!force && state != null && state.Length > 0 &&
            string.Equals(state[0].Trim(), ticks.ToString(), StringComparison.Ordinal))
        {
            return false;   // 文件夹没动过，不可能突然出现媒体文件
        }

        if (FindFirstMediaFile(folder) == null)
        {
            WritePlainState(statePath, ticks, "");
            return false;
        }

        string sig = ContentSignature(folder);

        if (!force && state != null && state.Length > 1 &&
            string.Equals(state[1].Trim(), sig, StringComparison.Ordinal))
        {
            WritePlainState(statePath, ticks, sig);   // 这个内容上次就没合成出来，别反复试
            return false;
        }

        string name = CacheIconName(folder, color);
        string ico = Path.Combine(CacheIconDir(), name);

        if (TryBuildThumbnailIcon(folder, color, ico) == null)
        {
            WritePlainState(statePath, ticks, sig);
            DebugLog("refresh skip: 纯色升级失败（无可用缩略图） " + folder);
            return false;
        }

        if (!SetIconValue(iniPath, CacheIconValue(name)))
        {
            return false;
        }

        // 换成新图标之后同样要让「文件夹缩略图缓存」失效，否则窗口里还是旧样子。
        // 顺序：先通知（触发重绘），再挪时间（作废缓存）。
        NotifyFolder(folder);
        NudgeWriteTime(folder, 1 + ValidColorIndex(color));
        WriteCacheSig(ico, ContentSignature(folder));

        try { File.Delete(Io(statePath)); }
        catch { }

        liveIcon = name;
        DebugLog("refresh upgrade: 纯色 -> 内容预览 " + name);
        return true;
    }
}
