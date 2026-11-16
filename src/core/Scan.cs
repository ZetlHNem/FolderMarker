using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// 扫描/共享盘/watcher 层：扫根间隔与抖动、UNC 探测、文件系统事件泵、守护锁。
// 从 FolderMarker.cs 拆出来 —— 与 Watch.cs 的巡检逻辑配合，同属 partial class Program。

internal static partial class Program
{

    /// <summary>共享盘根扫描默认间隔（分钟）。守护进程 / 设置页 / 管理台共用这一个常量。</summary>
    public const int DefaultRootScanMinutes = 2;

    /// <summary>下次允许扫根的时间点（已含随机抖动，见 NextRootScanDelay）</summary>
    private static DateTime _nextRootScanAt = DateTime.MinValue;

    /// <summary>
    /// 扫根间隔自适应：小根按设置页的间隔（默认 2 分钟）；
    /// 大根（实测 8400 个目录要 11 秒）自动拉长到 30 分钟，别反复压 SMB。
    /// 以前每轮扫完都硬写成 5 分钟，会把设置页的值覆盖掉（已修）。
    /// </summary>
    private static TimeSpan _rootScanInterval = TimeSpan.FromMinutes(DefaultRootScanMinutes);
    private static int _rootScanSettingMinutes = -1;

    private static readonly Random _scanRng = new Random();
    private static readonly object _scanRngLock = new object();

    /// <summary>
    /// 下一轮延迟 = 基础间隔 + 随机抖动（0~25%，最多 5 分钟）。
    /// 多台客户端不会再在同一秒齐刷刷扫共享盘；每轮重抽一次，长期完全错开。
    /// 开机强制 / 手动指定 root 不受抖动影响，仍然立即执行。
    /// </summary>
    private static TimeSpan NextRootScanDelay()
    {
        double maxMs = Math.Min(_rootScanInterval.TotalMilliseconds * 0.25, 5 * 60 * 1000);
        double jitterMs;
        lock (_scanRngLock) { jitterMs = _scanRng.NextDouble() * maxMs; }
        return _rootScanInterval + TimeSpan.FromMilliseconds(jitterMs);
    }

    private static DateTime _lastCacheSweep = DateTime.MinValue;

    /// <summary>是不是 UNC 网络路径</summary>
    private static bool IsUncPath(string folder)
    {
        return !string.IsNullOrEmpty(folder) && folder.StartsWith(@"\\", StringComparison.Ordinal);
    }

    /// <summary>取 \\server\share 这一段，用来判断整台共享盘是否可达</summary>
    private static string ShareRoot(string folder)
    {
        if (!IsUncPath(folder))
        {
            return null;
        }

        string rest = folder.Substring(2);
        int slash = rest.IndexOf('\\');
        if (slash <= 0)
        {
            return null;
        }

        int slash2 = rest.IndexOf('\\', slash + 1);
        string share = slash2 > 0 ? rest.Substring(0, slash2) : rest;
        return @"\\" + share;
    }

    /// <summary>
    /// 共享盘是否可达。用带超时的探测 —— 直接 Directory.Exists 在断网的 UNC 上
    /// 会卡到 TCP 超时（20 秒级），整轮巡检都被拖住。
    /// </summary>
    private static bool ShareReachable(string share)
    {
        try
        {
            System.Threading.Tasks.Task<bool> t =
                System.Threading.Tasks.Task.Factory.StartNew<bool>(
                    delegate { return Directory.Exists(share); });

            if (!t.Wait(1200))
            {
                return false;
            }

            return t.Result;
        }
        catch
        {
            return false;
        }
    }
    /// <summary>扫标记清单 + 配置过的共享盘根，把所有图标补齐/刷新</summary>
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
    /// <summary>上次颜色记录：DataDir\undo\&lt;hash&gt;.prev，内容是颜色 token</summary>
    private static string PrevColorPath(string folder)
    {
        return Path.Combine(Path.Combine(DataDir(), "undo"), PathHash(folder) + ".prev");
    }

    private static string ReadPrevColor(string folder)
    {
        try
        {
            string p = PrevColorPath(folder);
            if (!File.Exists(p)) { return null; }
            string t = File.ReadAllText(p, Encoding.UTF8).Trim().ToLowerInvariant();
            if (Array.IndexOf(ValidColors, t) >= 0 || IsCustomToken(t)) { return t; }
        }
        catch { }
        return null;
    }
    private static void WatcherOnFsEvent(object sender, FileSystemEventArgs e)
    {
        try
        {
            // 只关心用户的内容文件。我们自己的 desktop.ini 摘/还、.fm_tmp、
            // 签名侧车都会产生事件，不过滤的话会「自己写 → 自己重建 → 再写」打转。
            string name = Path.GetFileName(e.FullPath ?? "");
            if (name.Length == 0 ||
                name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".fm_tmp", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".sig", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            FileSystemWatcher w = sender as FileSystemWatcher;
            if (w != null) { MarkPending(w.Path); }
        }
        catch { }
    }

    private static void WatcherOnFsError(object sender, System.IO.ErrorEventArgs e)
    {
        // 缓冲区溢出等错误：丢掉这个 watcher，下一轮巡检的 RebuildWatchers 会重建
        try
        {
            FileSystemWatcher w = sender as FileSystemWatcher;
            if (w == null) { return; }
            lock (_watchLock)
            {
                string key = null;
                foreach (KeyValuePair<string, FileSystemWatcher> kv in _watchers)
                {
                    if (ReferenceEquals(kv.Value, w)) { key = kv.Key; break; }
                }
                if (key != null) { _watchers.Remove(key); }
            }
            try { w.Dispose(); } catch { }
        }
        catch { }
    }

    private static void MarkPending(string folder)
    {
        lock (_pendingLock)
        {
            _pendingFolders[folder] = DateTime.UtcNow;
        }
    }

    /// <summary>去抖泵：事件停 500ms 后才真正重建，避免一次批量操作触发一串重建</summary>
    private static void StartWatchPump()
    {
        System.Threading.Thread pump = new System.Threading.Thread(WatchPumpLoop);
        pump.IsBackground = true;
        pump.Start();
    }

    private static void WatchPumpLoop()
    {
        while (true)
        {
            System.Threading.Thread.Sleep(300);

            List<string> due = null;
            lock (_pendingLock)
            {
                DateTime now = DateTime.UtcNow;
                foreach (KeyValuePair<string, DateTime> kv in _pendingFolders)
                {
                    if ((now - kv.Value).TotalMilliseconds >= 500)
                    {
                        if (due == null) { due = new List<string>(); }
                        due.Add(kv.Key);
                    }
                }
                if (due != null)
                {
                    foreach (string f in due) { _pendingFolders.Remove(f); }
                }
            }

            if (due == null) { continue; }

            foreach (string f in due)
            {
                // 同一文件夹不并发重建（轮询 RefreshAll 可能正在处理它）：
                // 正在重建就重新排队，500ms 后再看
                lock (_rebuildLock)
                {
                    if (_rebuilding.ContainsKey(f))
                    {
                        MarkPending(f);
                        continue;
                    }
                    _rebuilding.Add(f, true);
                }

                try
                {
                    string liveIcon;
                    RefreshOne(f, false, out liveIcon);
                }
                catch { }
                finally
                {
                    lock (_rebuildLock)
                    {
                        _rebuilding.Remove(f);
                    }
                }
            }
        }
    }

    private static readonly object _rebuildLock = new object();
    private static readonly Dictionary<string, bool> _rebuilding =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

    /// <summary>守护进程把自己的 PID 写在这里，管理台停止它时按 PID 精确处理</summary>
    private static string WatchPidPath()
    {
        return Path.Combine(DataDirPath(), "watch.pid");
    }

}
