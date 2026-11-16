using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using WF = System.Windows.Forms;

// 巡检与守护：RefreshAll / RefreshOne 主循环、缓存回收、自动清理副本（RunAutoDemark）、
// FileSystemWatcher 即时响应（RebuildWatchers）、后台守护 RunWatch。
// 从 FolderMarker.cs 拆出来 —— 这是最长的一条业务链，独立成文件后排查巡检问题不用翻全文。

internal static partial class Program
{
    /// <summary>
    /// 校验并按需重建一个文件夹的图标。三种情况都要处理：
    ///   ① cache 图标：内容变了重画；媒体文件没了 → 降级成纯色
    ///   ② 纯色图标：出现了图片/视频 → 升级成内容预览
    ///   ③ 不是我们写的图标：不管
    /// 换图之后要同时做两件事：通知 shell 重读，并把文件夹 mtime 挪几毫秒
    /// 让「文件夹缩略图缓存」失效 —— 只覆盖 ico 文件内容时，已经缓存过的缩略图
    /// 会继续显示旧画面（这就是「缓存里有新图标、窗口里没换」的根因）。
    /// 合成失败时保留旧 ico，避免缓存缺失导致掉色。
    /// </summary>
    private static bool RefreshOne(string folder, bool force)
    {
        string ignored;
        return RefreshOne(folder, force, out ignored);
    }

    /// <summary>liveIcon 返回 ini 里当前引用的图标文件名，供缓存回收判断「还在用」</summary>
    private static bool RefreshOne(string folder, bool force, out string liveIcon)
    {
        liveIcon = null;

        string iniPath = Path.Combine(Io(folder), "desktop.ini");
        if (!File.Exists(iniPath))
        {
            return false;
        }

        string iconName, color;
        bool isCache;
        if (!TryReadMarkedIcon(iniPath, out iconName, out color, out isCache))
        {
            return false;
        }

        liveIcon = iconName;

        if (!isCache)
        {
            return TryUpgradePlain(folder, iniPath, color, force, out liveIcon);
        }

        string ico = Path.Combine(CacheIconDir(), iconName);
        string cachedSig = ReadCacheSig(ico);
        string sig = ComputeSignature(folder, cachedSig);
        bool missing = !File.Exists(Io(ico));
        bool changed = !string.Equals(cachedSig, sig, StringComparison.Ordinal);

        if (!force && !missing && !changed)
        {
            return false;
        }

        // 「内容变化自动跟随」可以关掉：这时只补缺失的缓存，内容变了不重画
        // （省 CPU / 不想后台一直重画大文件夹时用）
        if (!force && !missing && changed && GetSettingInt("AutoFollow", 1) == 0)
        {
            return false;
        }

        if (FindFirstMediaFile(folder) == null)
        {
            // 媒体文件被删光了。以前这里直接 return，图标会永远停在旧照片上。
            if (DowngradeToPlain(folder, iniPath, color))
            {
                liveIcon = IsCustomToken(color)
                    ? CacheIconName(folder, color)
                    : color + ".ico";
                return true;
            }
            return false;
        }

        string built = TryBuildThumbnailIcon(folder, color, ico);
        if (built == null)
        {
            if (missing)
            {
                // 缓存没了又画不出来（缺解码器 / 权限）：退回纯色，总比掉成默认黄图标好
                if (DowngradeToPlain(folder, iniPath, color))
                {
                    liveIcon = IsCustomToken(color)
                        ? CacheIconName(folder, color)
                        : color + ".ico";
                    return true;
                }
            }

            DebugLog("refresh skip: 合成失败，保留旧图标 " + folder);
            return false;
        }

        // 内容变了 / 缓存刚补回来：让缩略图和图标缓存一起失效，再把签名落到新状态。
        // 只在「真正变了」的时候挪时间 —— 开机强制重画（内容没变）不该改所有文件夹的 mtime。
        NotifyFolder(folder);

        if (changed || missing)
        {
            NudgeWriteTime(folder, 1 + ValidColorIndex(color));
            sig = ContentSignature(folder);
        }

        WriteCacheSig(ico, sig);
        DebugLog("refresh ok: " +
            (force ? "开机强制重画" : (missing ? "缓存重建" : "内容变化重画")) +
            " -> " + iconName);
        return true;
    }

    private static int RefreshAll(string root, bool force)
    {
        // 守护进程巡检是恢复 .fm_tmp 的主要时机（Main 里那次的清单可能不含共享盘新标记）
        try { RecoverParkedIni(); } catch { }

        Dictionary<string, string> folders =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string p in ReadMarked())
        {
            if (!string.IsNullOrEmpty(p.Trim()))
            {
                string f = Plain(p.Trim());
                try { f = Path.GetFullPath(f); }
                catch { }
                folders[f] = f;
            }
        }

        // 自动发现新标记：A 电脑上新标的文件夹，desktop.ini 会同步过来，
        // 但 B 的 marked.txt 里没有它。定期扫一次配置过的共享盘根，把新发现的记进清单，
        // 之后每轮巡检就都会带上。开机（force）和手动指定 root 时立即扫，平时按自适应间隔。
        List<string> roots = new List<string>();
        if (!string.IsNullOrEmpty(root)) { roots.Add(root); }
        foreach (string r in ReadRoots())
        {
            bool dup = false;
            for (int i = 0; i < roots.Count; i++)
            {
                if (SamePath(roots[i], r)) { dup = true; break; }
            }
            if (!dup) { roots.Add(r); }
        }

        if (roots.Count > 0)
        {
            // _nextRootScanAt 里已含随机抖动；开机强制 / 手动指定 root 立即执行
            bool due = force || !string.IsNullOrEmpty(root) ||
                       DateTime.UtcNow >= _nextRootScanAt;

            if (due)
            {
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                int added = 0;

                foreach (string r in roots)
                {
                    // 断了的共享盘不扫：扫也扫不出来，只会白等超时
                    if (IsUncPath(r) && !ShareReachable(ShareRoot(r) ?? r))
                    {
                        DebugLog("root scan skip: 共享盘不可达 " + r);
                        continue;
                    }

                    foreach (string f in ScanMarkedFolders(r))
                    {
                        if (!folders.ContainsKey(f))
                        {
                            AddMarked(f);   // 新发现的，写进清单
                            added++;
                        }
                        folders[f] = f;
                    }
                }

                sw.Stop();
                int configured = _rootScanSettingMinutes > 0
                    ? _rootScanSettingMinutes : DefaultRootScanMinutes;
                // 慢根保护优先：单次超过 2 秒就拉到 30 分钟，不让用户设的小值压垮大共享盘；
                // 否则严格用设置页的值，不再硬写 5 分钟（这就是原来的覆盖 bug）。
                _rootScanInterval = sw.Elapsed > TimeSpan.FromSeconds(2)
                    ? TimeSpan.FromMinutes(30)
                    : TimeSpan.FromMinutes(Math.Max(1, configured));
                TimeSpan delay = NextRootScanDelay();
                _nextRootScanAt = DateTime.UtcNow + delay;

                DebugLog("root scan: " + sw.ElapsedMilliseconds.ToString() +
                         "ms 新增=" + added.ToString() +
                         " 下次=" + delay.TotalSeconds.ToString("F1") + "秒后(含抖动)");
            }
        }

        int n = 0;
        int skipped = 0;
        Dictionary<string, bool> live = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        List<string> ghosts = new List<string>();
        Dictionary<string, bool> reach = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        // 自动规则：先按规则把颜色改到位，再走正常巡检渲染
        List<AutoRule> autoRules = ReadRules();

        foreach (string f in folders.Keys)
        {
            try
            {
                if (autoRules.Count > 0)
                {
                    AutoRule hit = MatchRule(f, autoRules);
                    if (hit != null)
                    {
                        string iniPath0 = Path.Combine(Io(f), "desktop.ini");
                        if (File.Exists(iniPath0))
                        {
                            string curColor = CurrentFolderColor(f);
                            bool same = string.Equals(curColor, hit.Color, StringComparison.OrdinalIgnoreCase);
                            if (!same)
                            {
                                Main(new string[] { "set", hit.Color, f, "quiet" });
                                DebugLog("rule: " + hit.Raw + " -> " + f);
                            }
                        }
                    }
                }

                if (!PathReachable(f, reach))
                {
                    skipped++;
                    continue;
                }

                string ini = Path.Combine(Io(f), "desktop.ini");

                // 老标记补 Path=（一次性）。共享盘也补 —— 从共享盘复制到本地时靠它识别；
                // 单台机器每个标记只写一次，失败就跳过（只读共享盘不会反复尝试）。
                try { EnsureStatePath(ini, f); } catch { }

                // 幽灵条目：另一台电脑把标记清了、或者用户自己换了图标，
                // 这台的清单还留着这条，缓存也一直占着。读不到 ini 时不敢动（可能是权限/抖动）。
                // 抓缩略图时 ini 会被临时改名成 .fm_tmp（几百毫秒）。这期间
                // ini 不存在只是暂时的，不能当「幽灵」删标记 + 删缓存。
                if (File.Exists(ini + ".fm_tmp")) { continue; }

                if (!File.Exists(ini))
                {
                    if (Directory.Exists(Io(f)))
                    {
                        ghosts.Add(f);
                    }
                    continue;
                }

                string iconName, color;
                bool isCache;
                if (!TryReadMarkedIcon(ini, out iconName, out color, out isCache) && !HasMarkerIcon(ini))
                {
                    List<string> check;
                    if (TryReadIni(ini, out check))
                    {
                        ghosts.Add(f);
                        continue;
                    }
                }

                // 上一轮读这个 ini 时发现过「[FolderMarkerState] 粘在 IconResource 后面」
                // 的坏行（读的时候已在内存里拆开）：这里把拆好的内容写回文件并通知
                // Explorer 重读。不修的话 Windows 把 "0[FolderMarkerState]" 当进图标值，
                // 整条 IconResource 作废，文件夹永远掉回默认缩略图。
                if (TakeIniHealed(ini))
                {
                    try
                    {
                        WriteIni(ini, ReadIni(ini));
                        NotifyFolder(f);
                        DebugLog("ini heal: desktop.ini 粘行已修复 " + f);
                    }
                    catch { }
                }

                string liveIcon;
                if (RefreshOne(f, force, out liveIcon)) { n++; }
                if (!string.IsNullOrEmpty(liveIcon)) { live[liveIcon] = true; }

                // 复制检测：同级目录里如果有「别的路径」的标记，按策略还原
                TryRevertForeignCopies(f);

                // 纯色标记还有一份「上次看到的 mtime / 上次失败的签名」状态文件，别被当成孤儿删掉
                if (!isCache)
                {
                    live[PathHash(f) + "-" + color + ".lm"] = true;
                }
            }
            catch (Exception ex) { DebugLog("巡检 " + f + " 失败: " + ex.Message); }
        }

        if (ghosts.Count > 0)
        {
            foreach (string g in ghosts)
            {
                try { RemoveMarked(g); }
                catch { }
                try { DeleteCacheIcons(g); }
                catch { }
            }
            DebugLog("ghost cleanup: 移除 " + ghosts.Count.ToString() + " 个已不再标记的条目");
        }

        if (skipped > 0)
        {
            DebugLog("refresh: 跳过 " + skipped.ToString() + " 个（共享盘不可达）");
        }

        // 有文件夹这轮没读到（共享盘离线），它的缓存不在 live 里。
        // 这时候做回收会把还能用的图标当孤儿删掉，等它恢复再清。
        if (skipped == 0)
        {
            SweepCache(live, force);
        }
        else
        {
            DebugLog("cache sweep: 本轮跳过（有文件夹读不到，避免误删）");
        }

        return n;
    }

    /// <summary>
    /// 回收没人用的缓存文件：文件夹被删除/移走、从缩略图降级成纯色、另一台电脑清了标记……
    /// 以前这些 ico 会一直留在 cache 里，越攒越多。
    /// </summary>
    private static void SweepCache(Dictionary<string, bool> live, bool force)
    {
        try
        {
            if (!force && (DateTime.UtcNow - _lastCacheSweep) < TimeSpan.FromHours(6))
            {
                return;
            }
            _lastCacheSweep = DateTime.UtcNow;

            string dir = CacheIconDir();
            if (!Directory.Exists(Io(dir)))
            {
                return;
            }

            int removed = 0;

            foreach (string f in Directory.GetFiles(Io(dir)))
            {
                string name = Path.GetFileName(f);
                if (live.ContainsKey(name))
                {
                    continue;
                }

                string stem = Path.GetFileNameWithoutExtension(name);
                string ext = Path.GetExtension(name);

                if (ext.Equals(".sig", StringComparison.OrdinalIgnoreCase))
                {
                    // 侧车文件名是 xxx.ico.sig，要退两层才是主图标名
                    string baseName = Path.GetFileNameWithoutExtension(stem);
                    if (File.Exists(Io(Path.Combine(dir, baseName + ".ico"))))
                    {
                        continue;   // 主图标还在，签名跟着留着
                    }
                }
                else if (ext.Equals(".lm", StringComparison.OrdinalIgnoreCase))
                {
                    // 纯色标记的状态文件，没有对应的 ico。只有没被任何标记引用、
                    // 且放了一天的才回收（live 里已经登记了在用的那些）。
                    DateTime lt;
                    try { lt = File.GetLastWriteTimeUtc(f); }
                    catch { continue; }

                    if ((DateTime.UtcNow - lt).TotalDays < 1)
                    {
                        continue;
                    }
                }
                else if (ext.Equals(".ico", StringComparison.OrdinalIgnoreCase))
                {
                    // 只回收一天以上没动过的，避免误删刚建好、这轮还没登记的缓存
                    DateTime t;
                    try { t = File.GetLastWriteTimeUtc(f); }
                    catch { continue; }

                    if ((DateTime.UtcNow - t).TotalDays < 1)
                    {
                        continue;
                    }
                }
                else
                {
                    continue;
                }

                try
                {
                    File.Delete(f);
                    removed++;
                }
                catch { }
            }

            if (removed > 0)
            {
                DebugLog("cache sweep: 回收 " + removed.ToString() + " 个孤儿文件");
            }
        }
        catch { }
    }

    /// <summary>防止有人把整个盘当根 import，最多扫这么多目录（正常共享盘几百到几千个）</summary>
    private const int MaxScanDirs = 20000;
    private static int _scanDirCount;

    /// <summary>递归扫一个根目录，找出 desktop.ini 里带本程序标记的文件夹（另一台电脑导入用）</summary>
    private static List<string> ScanMarkedFolders(string root)
    {
        List<string> found = new List<string>();

        if (IsUncPath(root))
        {
            string share = ShareRoot(root);
            if (!string.IsNullOrEmpty(share) && !ShareReachable(share))
            {
                DebugLog("scan skip: 共享盘不可达 " + root);
                return found;
            }
        }

        _scanDirCount = 0;
        try { ScanDir(Io(Plain(root)), 0, 8, found); }
        catch { }
        return found;
    }

    private static void ScanDir(string dir, int depth, int maxDepth, List<string> found)
    {
        if (depth > maxDepth || ++_scanDirCount > MaxScanDirs)
        {
            return;
        }

        try
        {
            string ini = Path.Combine(dir, "desktop.ini");
            string iconName, color;
            bool isCache;
            // 纯色标记也要认（cache + icons 两种）。以前只认 cache，
            // 结果不含图片/视频的文件夹在另一台电脑上永远同步不过去。
            if (File.Exists(ini) && TryReadMarkedIcon(ini, out iconName, out color, out isCache))
            {
                // 递归扫出来的路径可能混用 / 和 \（GetImage 会因此取不到缩略图），统一规范化
                string normalized;
                try { normalized = Path.GetFullPath(Plain(dir)); }
                catch { normalized = Plain(dir); }
                found.Add(normalized);
            }

            foreach (string sub in Directory.GetDirectories(dir))
            {
                string name = Path.GetFileName(sub);
                if (name.Equals("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("."))
                {
                    continue;
                }

                ScanDir(sub, depth + 1, maxDepth, found);
            }
        }
        catch { }
    }

    /// <summary>
    /// 手动清理：递归扫描一个目录，把「复制/移动出来的副本标记」还原成普通文件夹。
    /// 共享盘路径跳过（那里是跨电脑同步，不能当副本清掉）。
    /// 结果写 demark_result.txt（第一行=扫描到的标记数，第二行=还原数），供界面显示。
    /// </summary>
    private static int RunDemark(string root, bool showResult)
    {
        if (string.IsNullOrEmpty(root))
        {
            return 1;
        }

        string norm;
        try { norm = Path.GetFullPath(Plain(root)); }
        catch { norm = Plain(root); }

        if (!Directory.Exists(Io(norm)))
        {
            DebugLog("demark: 目录不存在 " + norm);
            return 2;
        }

        List<string> found = ScanMarkedFolders(norm);
        int n = 0;

        foreach (string f in found)
        {
            try
            {
                if (IsUncPath(f)) { continue; }

                string ini = Path.Combine(Io(f), "desktop.ini");
                string owner;
                if (!IsForeignMark(f, ini, out owner)) { continue; }

                Main(new string[] { "clear", "none", f, "quiet" });
                n++;
                DebugLog("demark: " + f + " <- " + owner);
            }
            catch { }
        }

        DebugLog("demark: 扫描 " + found.Count.ToString() +
                 " 个标记，还原 " + n.ToString() + " 个副本（根 " + norm + "）");

        try
        {
            File.WriteAllLines(Path.Combine(DataDirPath(), "demark_result.txt"),
                new string[] { found.Count.ToString(), n.ToString(), norm },
                new UTF8Encoding(false));
        }
        catch { }

        if (showResult)
        {
            try
            {
                string msg = n > 0
                    ? "扫描到 " + found.Count.ToString() + " 个标记，还原了 " + n.ToString()
                      + " 个从别处复制/移动来的副本。\r\n\r\n" + norm
                    : "扫描到 " + found.Count.ToString() + " 个标记，没有发现复制/移动来的副本。\r\n\r\n" + norm;
                WF.MessageBox.Show(msg, "文件夹标记", WF.MessageBoxButtons.OK, WF.MessageBoxIcon.Information);
            }
            catch { }
        }

        return 0;
    }

    private static DateTime _lastAutoDemark = DateTime.MinValue;
    private static bool _lastAutoDemarkLoaded;
    private static DateTime _autoDemarkAfter = DateTime.MinValue;
    private const int AutoDemarkMaxDirs = 200000;

    private static string AutoDemarkStampPath()
    {
        return Path.Combine(DataDirPath(), "last_autodemark.txt");
    }

    /// <summary>
    /// 上次扫描时间落到文件：不落盘的话每次重启守护都会当成「首次」全量扫一遍（实测 20 秒）。
    /// 落盘之后重启只需走增量。
    /// </summary>
    private static void LoadAutoDemarkStamp()
    {
        if (_lastAutoDemarkLoaded) { return; }
        _lastAutoDemarkLoaded = true;

        try
        {
            string p = AutoDemarkStampPath();
            if (!File.Exists(Io(p))) { return; }
            long ticks;
            if (long.TryParse(File.ReadAllText(Io(p)).Trim(), out ticks) && ticks > 0)
            {
                DateTime t = new DateTime(ticks, DateTimeKind.Utc);
                if (t <= DateTime.UtcNow) { _lastAutoDemark = t; }
            }
        }
        catch { }
    }

    private static void SaveAutoDemarkStamp()
    {
        try
        {
            File.WriteAllText(Io(AutoDemarkStampPath()),
                _lastAutoDemark.Ticks.ToString(), new UTF8Encoding(false));
        }
        catch { }
    }

    /// <summary>
    /// 自动清理：把所有固定盘扫一遍，还原「从别处复制/移动过来」的副本标记。
    /// 只读目录项和 desktop.ini，不读文件内容；跳过系统/隐藏/依赖目录；
    /// 用「目录修改时间早于上次扫描」剪枝，稳定状态下几乎零开销（实测全盘约 3 秒）。
    /// </summary>
    private static void RunAutoDemark(bool force)
    {
        if (!force && GetSettingInt("AutoCleanCopies", 1) == 0) { return; }

        LoadAutoDemarkStamp();

        DateTime started = DateTime.UtcNow;
        int scanned = 0, reverted = 0;
        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            foreach (DriveInfo di in DriveInfo.GetDrives())
            {
                try
                {
                    if (di.DriveType != DriveType.Fixed || !di.IsReady) { continue; }
                    DemarkWalk(di.RootDirectory.FullName, 0, 8, ref scanned, ref reverted);
                }
                catch { }
            }
        }
        catch (Exception ex) { TryLog(ex); }

        sw.Stop();
        _lastAutoDemark = started;
        SaveAutoDemarkStamp();

        if (reverted > 0 || sw.ElapsedMilliseconds > 2000)
        {
            DebugLog("auto demark: 扫描 " + scanned.ToString() + " 个目录，还原 "
                + reverted.ToString() + " 个副本，用时 " + sw.ElapsedMilliseconds.ToString() + "ms");
        }
    }

    private static void DemarkWalk(string dir, int depth, int maxDepth,
        ref int scanned, ref int reverted)
    {
        if (depth > maxDepth || scanned >= AutoDemarkMaxDirs) { return; }

        try
        {
            // 剪枝：这个目录自上次扫描后没动过 → 子树里不可能新增副本。
            // 首次扫描 _lastAutoDemark 为 MinValue，不做剪枝（全量走一遍）。
            if (depth > 0 && _lastAutoDemark > DateTime.MinValue)
            {
                DateTime mtime = Directory.GetLastWriteTimeUtc(Io(dir));
                if (mtime < _lastAutoDemark.AddSeconds(-2)) { return; }
            }

            scanned++;
            string ini = Path.Combine(dir, "desktop.ini");
            if (File.Exists(ini))
            {
                string owner;
                if (IsForeignMark(dir, ini, out owner))
                {
                    DebugLog("auto demark: " + dir + " <- " + owner);
                    Main(new string[] { "clear", "none", dir, "quiet" });
                    reverted++;
                }
            }

            foreach (string sub in Directory.EnumerateDirectories(dir))
            {
                if (SkipAutoDemarkDir(Path.GetFileName(sub))) { continue; }
                DemarkWalk(sub, depth + 1, maxDepth, ref scanned, ref reverted);
            }
        }
        catch { }
    }

    /// <summary>自动清理时要跳过的目录（系统目录 / 依赖目录 / 隐藏目录）</summary>
    private static bool SkipAutoDemarkDir(string name)
    {
        if (string.IsNullOrEmpty(name) || name[0] == '.') { return true; }

        switch (name.ToLowerInvariant())
        {
            case "$recycle.bin":
            case "system volume information":
            case "windows":
            case "program files":
            case "program files (x86)":
            case "programdata":
            case "appdata":
            case "recovery":
            case "perflogs":
            case "msocache":
            case "config.msi":
            case "node_modules":
            case "$windows.~bt":
            case "$windows.~ws":
                return true;
        }
        return false;
    }

    /// <summary>另一台电脑：导入共享盘上别人标好的文件夹，并立刻生成自己的缓存 ico</summary>
    private static int ImportRoot(string root)
    {
        if (string.IsNullOrEmpty(root))
        {
            return 1;
        }

        // 命令行传进来的 UNC/盘符路径可能混用 / 和 \，先规范化，
        // 免得存进 roots.txt 后扫盘和比较路径时对不上。
        string normRoot;
        try { normRoot = Path.GetFullPath(root); }
        catch { normRoot = root; }

        // 记住这个共享盘根：以后守护进程会定期扫它，对方新标的文件夹自动跟上，
        // 不需要再手动 import 第二次。
        AddRoot(normRoot);

        List<string> found = ScanMarkedFolders(normRoot);
        foreach (string f in found)
        {
            AddMarked(f);
        }

        int n = 0;
        foreach (string f in found)
        {
            try { if (RefreshOne(f, false)) { n++; } }
            catch { }
        }

        DebugLog("import: 发现 " + found.Count.ToString() + " 个标记，重建 " + n.ToString() + " 个缓存");
        return 0;
    }

    /// <summary>后台守护：登录自启，开机先全量补齐，之后定时巡检（缺了补、内容变了重画）</summary>
    private static int RunWatch(int seconds)
    {
        if (seconds < 10) { seconds = 10; }
        if (seconds > 3600) { seconds = 3600; }
        StartWatchPump();

        bool created;
        using (System.Threading.Mutex m =
            new System.Threading.Mutex(true, "FolderMarkerWatchSingleton", out created))
        {
            if (!created)
            {
                return 0;   // 已经有一个在跑
            }

            // 把 PID 落盘：管理台的「停止守护」只按这个 PID 杀进程，
            // 不会再顺手把正在标色/批量处理的无窗口子进程一起打死。
            try
            {
                File.WriteAllText(Io(WatchPidPath()),
                    System.Diagnostics.Process.GetCurrentProcess().Id.ToString(),
                    new UTF8Encoding(false));
            }
            catch { }

            try
            {
                // 设置页里的扫描间隔：启动时应用一次
                _rootScanSettingMinutes = Math.Max(1,
                    GetSettingInt("RootScanMinutes", DefaultRootScanMinutes));
                _rootScanInterval = TimeSpan.FromMinutes(_rootScanSettingMinutes);

                // 自动清理副本：首次安装（还没有时间戳）等 5 分钟避开开机高峰；
                // 之后只等 30 秒 —— 增量扫描实测 156ms，没必要让用户干等 5 分钟。
                LoadAutoDemarkStamp();
                _autoDemarkAfter = _lastAutoDemark > DateTime.MinValue
                    ? DateTime.UtcNow.AddSeconds(30)
                    : DateTime.UtcNow.AddMinutes(5);

                // 开机先强制全量重画一次 —— 相当于「重启后重新获取」，
                // 之后即使缓存被清、内容被改，也不会出现旧预览或缺图。
                try { RefreshAll(null, true); }
                catch (Exception ex) { DebugLog("watch 开机全量刷新失败: " + ex.Message); }
                try { RebuildWatchers(ReadMarkedNormalized()); }
                catch (Exception ex) { DebugLog("watch 建立 watcher 失败: " + ex.Message); }

                while (true)
                {
                    // 每轮重读设置，设置页改完不用重启进程就能生效（最长等一轮）
                    int iv = GetSettingInt("WatchInterval", seconds);
                    if (iv < 10) { iv = 10; }
                    if (iv > 3600) { iv = 3600; }

                    // 只有用户真的改过设置才重置（大根自适应出的 30 分钟不该被打回）。
                    // 同时把 deadline 清零，让新间隔立刻生效，不用等旧倒计时走完。
                    int scanMin = Math.Max(1,
                        GetSettingInt("RootScanMinutes", DefaultRootScanMinutes));
                    if (scanMin != _rootScanSettingMinutes)
                    {
                        _rootScanSettingMinutes = scanMin;
                        _rootScanInterval = TimeSpan.FromMinutes(scanMin);
                        _nextRootScanAt = DateTime.MinValue;
                    }

                    System.Threading.Thread.Sleep(iv * 1000);

                    // 托盘里可暂停：暂停时只跳过巡检，watcher 即时响应照常
                    if (GetSettingInt("Paused", 0) == 1) { continue; }

                    try { RefreshAll(null, false); }
                    catch (Exception ex) { DebugLog("watch 巡检失败: " + ex.Message); }

                    // 自动清理复制/移动出来的副本标记：默认每分钟一次。
                    // 靠「目录 mtime 早于上次扫描」剪枝 + 时间戳落盘，稳定状态一次几毫秒。
                    try
                    {
                        if (DateTime.UtcNow >= _autoDemarkAfter &&
                            (DateTime.UtcNow - _lastAutoDemark) > TimeSpan.FromMinutes(1))
                        {
                            RunAutoDemark(false);
                        }
                    }
                    catch (Exception ex) { DebugLog("watch 自动清理副本失败: " + ex.Message); }

                    try { RebuildWatchers(ReadMarkedNormalized()); }
                    catch (Exception ex) { DebugLog("watch 重建 watcher 失败: " + ex.Message); }
                }
            }
            finally
            {
                try { File.Delete(Io(WatchPidPath())); }
                catch { }
            }
        }
    }

    // ---------------- 即时响应：本地盘 FileSystemWatcher ----------------
    // 轮询能兜底但最慢要等一整个巡检周期；本地盘挂 watcher 后，
    // 文件夹内容一变 0.5 秒内就重建预览。网络盘（UNC）事件不可靠，交给轮询。

    private static readonly object _watchLock = new object();
    private static readonly Dictionary<string, FileSystemWatcher> _watchers =
        new Dictionary<string, FileSystemWatcher>(StringComparer.OrdinalIgnoreCase);
    private static readonly object _pendingLock = new object();
    private static readonly Dictionary<string, DateTime> _pendingFolders =
        new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

    /// <summary>把标记清单规范成绝对路径（和 RefreshAll 的口径一致）</summary>
    private static List<string> ReadMarkedNormalized()
    {
        List<string> list = new List<string>();
        foreach (string raw in ReadMarked())
        {
            string f = raw.Trim();
            if (f.Length == 0) { continue; }
            try { f = Path.GetFullPath(Plain(f)); } catch { continue; }
            list.Add(f);
        }
        return list;
    }

    private static bool _pumpStarted;

    private static void RebuildWatchers(List<string> folders)
    {
        // 泵随 watcher 一起启动（守护进程和 selftest 都可能先建 watcher）
        if (!_pumpStarted)
        {
            _pumpStarted = true;
            StartWatchPump();
        }

        lock (_watchLock)
        {
            List<string> dead = new List<string>();
            foreach (KeyValuePair<string, FileSystemWatcher> kv in _watchers)
            {
                bool alive = false;
                foreach (string f in folders)
                {
                    if (SamePath(f, kv.Key)) { alive = true; break; }
                }
                if (!alive) { dead.Add(kv.Key); }
            }
            foreach (string k in dead)
            {
                try { _watchers[k].Dispose(); } catch { }
                _watchers.Remove(k);
            }

            foreach (string f in folders)
            {
                if (_watchers.ContainsKey(f)) { continue; }
                if (f.StartsWith(@"\", StringComparison.Ordinal)) { continue; }   // UNC 交给轮询
                try
                {
                    FileSystemWatcher w = new FileSystemWatcher(Io(f))
                    {
                        IncludeSubdirectories = false,
                        InternalBufferSize = 64 * 1024,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                                       NotifyFilters.LastWrite | NotifyFilters.Size
                    };
                    w.Created += WatcherOnFsEvent;
                    w.Changed += WatcherOnFsEvent;
                    w.Deleted += WatcherOnFsEvent;
                    w.Renamed += WatcherOnFsEvent;
                    w.Error += WatcherOnFsError;
                    w.EnableRaisingEvents = true;
                    _watchers[f] = w;
                }
                catch { }
            }
        }
    }
}
