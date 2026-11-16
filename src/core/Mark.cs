using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// 标记核心层：给单个文件夹上色/清除标记、还原原始属性与时间戳、外壳图标应用。
// 从 FolderMarker.cs 拆出来 —— 右键 set/clear 与管理台操作共用的底层流程。

internal static partial class Program
{

    /// <summary>
    /// 把一个文件夹的标记彻底清掉（含还原原始属性、时间戳、用户原有的自定义图标）。
    /// 右键「清除标记」和卸载时的批量清理都走这里，保证两条路的行为完全一致。
    /// </summary>
    private static bool ClearOne(string folder)
    {
        folder = Plain(folder);

        string ioFolder = Io(folder);
        string iniPath = Path.Combine(ioFolder, "desktop.ini");
        int hr;
        bool cleared;

        using (PathLock(iniPath))
        {
            // 「标记前状态」不能在这里现抓：此刻文件夹早已被标记过，
            // 属性里带着我们加的 System，直接回写就等于没清干净。
            // 原始状态存在 ini 的 [FolderMarkerState] 段里，优先用它。
            FolderState current = FolderState.Capture(ioFolder);
            FolderState stored = LoadState(iniPath);
            bool hadIni = File.Exists(iniPath);
            FileAttributes? iniAttrs = CaptureIniAttributes(iniPath);

            System.Diagnostics.Stopwatch swStep = System.Diagnostics.Stopwatch.StartNew();
            hr = ApplyIcon(folder, "");
            long tApi = swStep.ElapsedMilliseconds;

            CleanAfterClear(iniPath, iniAttrs, stored.PrevIcon);
            cleared = !HasMarkerIcon(iniPath);

            // 合成图标和放进文件夹内的 FolderMarker.ico 都删掉。
            // 必须放在时间戳还原【之前】——在文件夹里删文件会把
            // 修改时间刷成现在，先删再还原才干净。
            DeleteTintedIcons(folder);

            swStep.Restart();
            // 三种情况分开处理：
            //   有原始记录    → 严格还原（只读还只读、系统还系统）
            //   有 ini 但没记录（旧版本标的）→ 那个 System 是我们加的，抹掉
            //   连 ini 都没有（从没标记过）→ 原样还原，别动用户属性
            KeepInvisible(ioFolder, stored.Valid ? stored : current, true, hadIni && !stored.Valid);
            if (tApi > 80 || swStep.ElapsedMilliseconds > 80)
            {
                DebugLog("slow clear api=" + tApi + "ms restore=" + swStep.ElapsedMilliseconds + "ms " + folder);
            }
        }

        if (hr != 0)
        {
            NotifyFolder(folder);
        }

        return cleared;
    }

    /// <summary>
    /// 卸载前批量还原：读标记清单，逐个清掉。
    /// 只动「ini 里确实还指着我们图标」的文件夹 —— 用户后来自己换过图标的
    /// 不能碰，否则等于把人家后设的东西删了。
    /// </summary>
    private static int RunCleanup()
    {
        List<string> marks = ReadMarked();
        int done = 0;

        foreach (string path in marks)
        {
            try
            {
                string folder = Plain(path.Trim());
                if (folder.Length == 0)
                {
                    continue;
                }

                string ioFolder = Io(folder);
                if (!Directory.Exists(ioFolder))
                {
                    continue;   // 已经被删掉或改名了，跳过
                }

                if (!HasMarkerIcon(Path.Combine(ioFolder, "desktop.ini")))
                {
                    continue;   // 用户自己换过图标，保持原样
                }

                if (ClearOne(folder))
                {
                    done++;
                }
            }
            catch { }
        }

        try { File.Delete(Io(MarkedListPath())); }
        catch { }

        try { File.Delete(Io(RootsListPath())); }
        catch { }

        // 合成图标是给已标记文件夹用的，卸载时连同目录一起清掉
        try
        {
            string custom = Io(CustomIconDir());
            if (Directory.Exists(custom))
            {
                Directory.Delete(custom, true);
            }
        }
        catch { }

        try
        {
            string cache = Io(CacheIconDir());
            if (Directory.Exists(cache))
            {
                Directory.Delete(cache, true);
            }
        }
        catch { }

        DebugLog("cleanup " + done.ToString() + "/" + marks.Count.ToString());
        return 0;
    }

    /// <summary>
    /// 把 desktop.ini 里 [.ShellClassInfo] 的 IconResource 改写成 %LOCALAPPDATA% 环境变量形式。
    /// shell 写的是本机绝对路径（含用户名/盘符），另一台电脑对不上；环境变量各自展开到本机。
    /// envRel 形如 icons\red.ico 或 cache\&lt;hash&gt;-red.ico。
    /// 同时把 shell 顺手写的 IconFile / IconIndex 摘掉，保持干净。
    /// </summary>
    private static void RewriteIconAsEnvPath(string iniPath, string iconName, string envRel)
    {
        try
        {
            if (!File.Exists(iniPath))
            {
                return;
            }

            string envIcon = "%LOCALAPPDATA%\\FolderMarker\\" + envRel + ",0";

            List<string> lines = ReadIni(iniPath);
            List<string> result = new List<string>();
            bool inShellInfo = false;
            bool wrote = false;

            for (int i = 0; i < lines.Count; i++)
            {
                string trimmed = lines[i].Trim();

                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    inShellInfo = trimmed.Equals("[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase);
                    result.Add(lines[i]);
                    continue;
                }

                if (inShellInfo &&
                    (trimmed.StartsWith("IconResource=", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("IconIndex=", StringComparison.OrdinalIgnoreCase)))
                {
                    if (trimmed.StartsWith("IconResource=", StringComparison.OrdinalIgnoreCase))
                    {
                        // 只改本程序刚写的那条（指向 FolderMarker 目录下 iconName 的绝对路径）
                        if (trimmed.IndexOf(iconName, StringComparison.OrdinalIgnoreCase) >= 0 &&
                            trimmed.IndexOf("FolderMarker", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            result.Add("IconResource=" + envIcon);
                            wrote = true;
                        }
                        else
                        {
                            result.Add(lines[i]);   // 用户自己的图标，别动
                        }
                    }
                    // IconFile / IconIndex 直接摘掉（shell 写的冗余行）
                    continue;
                }

                result.Add(lines[i]);
            }

            if (wrote)
            {
                WriteIni(iniPath, result);
                try { File.SetAttributes(iniPath, FileAttributes.Hidden | FileAttributes.System); }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>
    /// 让 shell 自己写 desktop.ini 并刷新外观缓存。
    /// iconPath 传空字符串表示「取消自定义图标」，文件夹恢复默认样子。
    /// </summary>
    private static int ApplyIcon(string folder, string iconPath)
    {
        SHFOLDERCUSTOMSETTINGS fcs = new SHFOLDERCUSTOMSETTINGS();
        fcs.dwSize = (uint)Marshal.SizeOf(typeof(SHFOLDERCUSTOMSETTINGS));
        fcs.dwMask = FCSM_ICONFILE;
        fcs.pszIconFile = iconPath;
        fcs.cchIconFile = 0;
        fcs.iIconIndex = 0;

        return SHGetSetFolderCustomSettings(ref fcs, folder, FCS_FORCEWRITE);
    }

    /// <summary>
    /// 标记前的文件夹状态。改图标一定会写 desktop.ini，写完这两样就被系统改掉了，
    /// 必须原样恢复，否则用户看「属性」就发现文件夹被动过。
    /// </summary>
    private struct FolderState
    {
        public bool Valid;
        public DateTime WriteUtc;
        public DateTime AccessUtc;
        public FileAttributes Attributes;
        public string PrevIcon;      // 标记前用户自己设的文件夹图标（如果有）

        /// <summary>
        /// 标记发生时的完整路径。复制/移动之后 desktop.ini 会跟着走，
        /// 但这里的 Path 仍指向原位置 —— 巡检据此识别「副本」并按策略还原。
        /// </summary>
        public string Path;

        public static FolderState Capture(string folder)
        {
            FolderState s = new FolderState();
            try
            {
                DirectoryInfo di = new DirectoryInfo(Io(folder));
                s.WriteUtc = di.LastWriteTimeUtc;
                s.AccessUtc = di.LastAccessTimeUtc;
                s.Attributes = di.Attributes;
                try { s.Path = System.IO.Path.GetFullPath(Plain(folder)); }
                catch { s.Path = Plain(folder); }
                s.Valid = true;
            }
            catch { }
            return s;
        }
    }

    /// <summary>
    /// 把文件夹的修改时间往后挪几秒。
    /// 资源管理器的「文件夹缩略图缓存」键是「路径 + 修改时间」，而修改时间在缓存键里
    /// 是**按 2 秒粒度取整**的 —— 实测只挪几毫秒（甚至 1 秒）缓存照样命中，
    /// 用户就会看到「cache 里有新图标，窗口里还是黄壳 + 旧内容」。
    /// 挪 2~4 秒即可失效，资源管理器和属性面板都只显示到分钟，看不出来。
    /// slot 用颜色序号，换色时偏移不同，保证一定失效。
    /// </summary>
    private static void NudgeWriteTime(string folder, int slot)
    {
        try
        {
            NudgeWriteTime(folder, Directory.GetLastWriteTimeUtc(Io(folder)), slot);
        }
        catch { }
    }

    /// <summary>
    /// 以 baseUtc 为基准挪 2~4 秒。反复换色时基准固定为「最初的修改时间」，
    /// 总偏移不会一次次累加（否则换十次色就多出一分钟）。
    /// </summary>
    private static void NudgeWriteTime(string folder, DateTime baseUtc, int slot)
    {
        try
        {
            if (slot < 0) { slot = 0; }

            DateTime cur = Directory.GetLastWriteTimeUtc(Io(folder));

            // 在「原始时间 +2 秒」和「+4 秒」两个状态之间来回切：
            //   · 两者相差 2 秒，一定落在缩略图缓存的不同 2 秒桶里 → 每次都能失效
            //   · 都不会累积偏移（永远以最初时间为基准）
            DateTime a = baseUtc.AddSeconds(2);
            DateTime b = baseUtc.AddSeconds(4);
            Directory.SetLastWriteTimeUtc(Io(folder),
                Math.Abs((cur - a).TotalSeconds) < 0.5 ? b : a);
        }
        catch { }
    }

    /// <summary>
    /// 把文件夹的时间戳和可见属性恢复成标记前的样子。
    /// 标记时用 System 属性代替 shell 自己加的 ReadOnly —— 两者都能让资源管理器认 desktop.ini，
    /// 但 System 在「属性」对话框里看不出来，不会让用户以为文件夹变成只读了。
    /// 清除时则**整份还原**标记前的属性：原本是只读的还它只读，原本是系统文件夹的还它系统，
    /// 否则「清除标记」会顺手把用户的只读保护 / 系统属性抹掉。
    /// </summary>
    private static void KeepInvisible(string folder, FolderState before, bool clearing = false, bool dropSystem = false)
    {
        folder = Io(folder);

        try
        {
            DirectoryInfo di = new DirectoryInfo(folder);
            FileAttributes attrs;

            if (clearing && before.Valid)
            {
                attrs = before.Attributes;
                if (dropSystem)
                {
                    attrs &= ~FileAttributes.System;
                }
            }
            else if (clearing)
            {
                attrs = di.Attributes & ~(FileAttributes.ReadOnly | FileAttributes.System);
            }
            else
            {
                attrs = di.Attributes & ~FileAttributes.ReadOnly;
                attrs |= FileAttributes.System;
                if (before.Valid && (before.Attributes & FileAttributes.ReadOnly) != 0)
                {
                    attrs |= FileAttributes.ReadOnly;
                }
            }

            if (attrs != di.Attributes)
            {
                di.Attributes = attrs;
            }
        }
        catch { }

        if (!before.Valid)
        {
            return;
        }

        // 时间戳放到最后恢复：写 ini、改属性都会各自刷新一次时间。
        try { Directory.SetLastAccessTimeUtc(folder, before.AccessUtc); }
        catch { }

        try { Directory.SetLastWriteTimeUtc(folder, before.WriteUtc); }
        catch { }
    }

    /// <summary>
    /// 取消标记后，shell 会在 desktop.ini 里留一条空的 IconResource。
    /// 这里只摘掉我们自己写进去的三行（IconResource / IconFile / IconIndex），
    /// **不能整段删掉 [.ShellClassInfo]** —— 用户原本可能在该段里放了 InfoTip、
    /// IconArea_Image（文件夹背景图）之类的东西，整段删会把它们一起干掉。
    /// 收拾干净后如果 ini 空了才删文件，文件属性还原成标记前的样子。
    /// </summary>
    private static void CleanAfterClear(string iniPath, FileAttributes? originalAttrs, string prevIcon)
    {
        iniPath = Io(iniPath);

        try
        {
            if (!File.Exists(iniPath))
            {
                return;
            }

            // 读失败（共享盘抖动/被占用）时绝不能当成「空文件」——
            // 否则 hasContent=false 会把用户自己的 desktop.ini 整个删掉。
            List<string> rawIni;
            if (!TryReadIni(iniPath, out rawIni)) { return; }

            List<string> cleaned = StripMarkKeys(rawIni);
            cleaned = StripSection(cleaned, "FolderMarkerState");
            cleaned = DropEmptySection(cleaned, ".ShellClassInfo");

            // 用户标记前本来就有自己的文件夹图标 → 还给他
            if (!string.IsNullOrEmpty(prevIcon))
            {
                cleaned = SetIconResource(cleaned, prevIcon);
            }

            bool hasContent = false;
            for (int i = 0; i < cleaned.Count; i++)
            {
                if (cleaned[i].Trim().Length > 0) { hasContent = true; break; }
            }

            try { File.SetAttributes(iniPath, FileAttributes.Normal); }
            catch { }

            if (hasContent)
            {
                WriteIni(iniPath, cleaned);
                try
                {
                    File.SetAttributes(iniPath, originalAttrs ?? (FileAttributes.Hidden | FileAttributes.System));
                }
                catch { }
            }
            else
            {
                try { File.Delete(iniPath); }
                catch { }
            }
        }
        catch { }
    }

    private static FileAttributes? CaptureIniAttributes(string iniPath)
    {
        iniPath = Io(iniPath);

        try
        {
            if (File.Exists(iniPath))
            {
                return File.GetAttributes(iniPath);
            }
        }
        catch { }
        return null;
    }

    /// <summary>去掉我们写进 [.ShellClassInfo] 的三行，其余原样保留</summary>
    private static List<string> StripMarkKeys(List<string> lines)
    {
        List<string> result = new List<string>();
        bool inShellInfo = false;

        for (int i = 0; i < lines.Count; i++)
        {
            string trimmed = lines[i].Trim();

            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
            {
                inShellInfo = trimmed.Equals("[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase);
                result.Add(lines[i]);
                continue;
            }

            if (inShellInfo &&
                (trimmed.StartsWith("IconResource=", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.StartsWith("IconIndex=", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            result.Add(lines[i]);
        }

        return result;
    }
    private static void ShowFailure(string message)
    {
        // 管理台批量处理时会连跑很多个子进程，不能让每个失败都弹一个框
        if (Environment.GetEnvironmentVariable("FM_QUIET") == "1")
        {
            return;
        }

        try
        {
            System.Windows.MessageBox.Show(
                message, "文件夹标记",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
        catch { }
    }

    private static void NotifyFolder(string folder)
    {
        try
        {
            NotifyPath(SHCNE_UPDATEITEM, SHCNF_PATHW_FLUSH, folder, IntPtr.Zero);
            string parent = Path.GetDirectoryName(folder);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(Io(parent)))
            {
                NotifyPath(SHCNE_UPDATEDIR, SHCNF_PATHW_FLUSH, parent, IntPtr.Zero);
            }
        }
        catch { }
    }

    private static void WriteMark(string iniPath, string iconPath, string folder)
    {
        // 以前这里是 StripSection(".ShellClassInfo") 整段重写 —— 会把用户自己放的
        // InfoTip / IconArea_Image / Logo 之类一起删掉（清了才发现，找不回来）。
        // 现在只替换我们那三行，其余原样保留。
        List<string> lines = ReadIni(iniPath);
        List<string> result = new List<string>();
        int header = -1;
        bool inShellInfo = false;

        for (int i = 0; i < lines.Count; i++)
        {
            string trimmed = lines[i].Trim();

            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
            {
                inShellInfo = trimmed.Equals("[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase);
                if (inShellInfo) { header = result.Count; }
                result.Add(lines[i]);
                continue;
            }

            if (inShellInfo &&
                (trimmed.StartsWith("IconResource=", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.StartsWith("IconIndex=", StringComparison.OrdinalIgnoreCase)))
            {
                continue;   // 这三行是我们自己的，替换掉
            }

            result.Add(lines[i]);
        }

        if (header >= 0)
        {
            result.Insert(header + 1, "IconIndex=0");
            result.Insert(header + 1, "IconFile=" + iconPath);
            result.Insert(header + 1, "IconResource=" + iconPath + ",0");
        }
        else
        {
            while (result.Count > 0 && result[result.Count - 1].Trim().Length == 0)
            {
                result.RemoveAt(result.Count - 1);
            }

            if (result.Count > 0)
            {
                result.Add("");
            }

            result.Add("[.ShellClassInfo]");
            result.Add("IconResource=" + iconPath + ",0");
            result.Add("IconFile=" + iconPath);
            result.Add("IconIndex=0");
        }

        WriteIni(iniPath, result);

        try { File.SetAttributes(iniPath, FileAttributes.Hidden | FileAttributes.System); }
        catch { }

        try
        {
            DirectoryInfo di = new DirectoryInfo(Io(folder));
            di.Attributes = di.Attributes | FileAttributes.System;
        }
        catch { }
    }

}
