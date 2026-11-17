using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// desktop.ini 的数据层：标记键增删、[FolderMarkerState] 存取、图标资源读写、复制来源判定。
// 从 FolderMarker.cs 拆出来 —— 这些是所有功能共用的地方，集中一处方便核对「值到底怎么写进去的」。

internal static partial class Program
{
    /// <summary>段里一条键都不剩时，把段标题一起拿掉（连带段内的空行）</summary>
    private static List<string> DropEmptySection(List<string> lines, string section)
    {
        List<string> result = new List<string>();

        for (int i = 0; i < lines.Count; i++)
        {
            string trimmed = lines[i].Trim();

            if (trimmed.Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase))
            {
                int j = i + 1;
                bool hasBody = false;

                for (; j < lines.Count; j++)
                {
                    string tj = lines[j].Trim();
                    if (tj.StartsWith("[") && tj.EndsWith("]")) break;
                    if (tj.Length > 0) { hasBody = true; break; }
                }

                if (!hasBody)
                {
                    i = j - 1;
                    continue;
                }
            }

            result.Add(lines[i]);
        }

        while (result.Count > 0 && result[result.Count - 1].Trim().Length == 0)
        {
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }

    /// <summary>
    /// desktop.ini 里是否真的写上了本次要用的图标。
    /// 判断"是否生效"只能看 ini 的实际内容 —— SHGetSetFolderCustomSettings 在
    /// 目录 ACL 拒绝写入时也可能返回 S_OK，返回码不可信。
    /// 比的是图标文件名：内置的 red.ico 和合成出来的 &lt;hash&gt;-red.ico 都不会撞名。
    /// </summary>
    private static bool MarkApplied(string iniPath, string icoFileName)
    {
        if (string.IsNullOrEmpty(icoFileName))
        {
            return false;
        }

        try
        {
            foreach (string line in ReadIni(iniPath))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("IconResource=", StringComparison.OrdinalIgnoreCase) &&
                    trimmed.IndexOf(icoFileName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    /// <summary>desktop.ini 里是否还留着本程序的图标引用</summary>
    private static bool HasMarkerIcon(string iniPath)
    {
        try
        {
            foreach (string line in ReadIni(iniPath))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("IconResource=", StringComparison.OrdinalIgnoreCase) &&
                    IsOwnIcon(trimmed.Substring("IconResource=".Length).Trim()))
                {
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// 把「标记前」的属性 / 时间戳写进 desktop.ini 的 [FolderMarkerState] 段。
    /// 只写一次：换色时已有的记录不能被后来（已被标记过）的状态覆盖。
    /// 这比侧车文件稳 —— 文件夹被改名、搬走时记录跟着 ini 一起走。
    /// </summary>
    private static void SaveState(string iniPath, FolderState before, string prevIcon)
    {
        if (!before.Valid || !File.Exists(iniPath))
        {
            return;
        }

        if (LoadState(iniPath).Valid)
        {
            return;
        }

        try
        {
            List<string> lines = ReadIni(iniPath);
            List<string> result = StripSection(lines, "FolderMarkerState");

            while (result.Count > 0 && result[result.Count - 1].Trim().Length == 0)
            {
                result.RemoveAt(result.Count - 1);
            }

            if (result.Count > 0)
            {
                result.Add("");
            }

            result.Add(StateSection);
            result.Add("Attributes=" + ((int)before.Attributes).ToString());
            result.Add("WriteUtc=" + before.WriteUtc.Ticks.ToString());
            result.Add("AccessUtc=" + before.AccessUtc.Ticks.ToString());
            if (!string.IsNullOrEmpty(before.Path))
            {
                result.Add("Path=" + before.Path);
            }
            if (!string.IsNullOrEmpty(prevIcon))
            {
                result.Add("PrevIcon=" + prevIcon);
            }

            WriteIni(iniPath, result);

            try { File.SetAttributes(iniPath, FileAttributes.Hidden | FileAttributes.System); }
            catch { }
        }
        catch { }
    }

    private static FolderState LoadState(string iniPath)
    {
        FolderState state = new FolderState();

        try
        {
            if (!File.Exists(iniPath))
            {
                return state;
            }

            bool inSection = false;
            int attrs = -1;
            long write = 0, access = 0;
            string prevIcon = null;
            string ownerPath = null;

            foreach (string line in ReadIni(iniPath))
            {
                string trimmed = line.Trim();

                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    inSection = trimmed.Equals(StateSection, StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inSection)
                {
                    continue;
                }

                int eq = trimmed.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                string key = trimmed.Substring(0, eq).Trim();
                string value = trimmed.Substring(eq + 1).Trim();

                if (key.Equals("Attributes", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(value, out attrs);
                }
                else if (key.Equals("WriteUtc", StringComparison.OrdinalIgnoreCase))
                {
                    long.TryParse(value, out write);
                }
                else if (key.Equals("AccessUtc", StringComparison.OrdinalIgnoreCase))
                {
                    long.TryParse(value, out access);
                }
                else if (key.Equals("PrevIcon", StringComparison.OrdinalIgnoreCase))
                {
                    prevIcon = value;
                }
                else if (key.Equals("Path", StringComparison.OrdinalIgnoreCase))
                {
                    ownerPath = value;
                }
            }

            state.PrevIcon = prevIcon;
            state.Path = ownerPath;

            if (attrs >= 0 && write > 0)
            {
                state.Attributes = (FileAttributes)attrs;
                state.WriteUtc = new DateTime(write, DateTimeKind.Utc);
                state.AccessUtc = access > 0 ? new DateTime(access, DateTimeKind.Utc) : state.WriteUtc;
                state.Valid = true;
            }
        }
        catch { }

        return state;
    }

    // ================= 复制/移动来的「副本标记」=================

    /// <summary>
    /// 老标记的 [FolderMarkerState] 里没有 Path=：补写当前路径（只补一次）。
    /// 补上之后，desktop.ini 被复制/移动到别处时就能通过「记录路径 ≠ 实际路径」识别出来。
    /// </summary>
    private static readonly object _statePathLock = new object();
    private static readonly Dictionary<string, bool> _statePathSkip =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

    private static void EnsureStatePath(string iniPath, string folder)
    {
        try
        {
            if (!File.Exists(iniPath)) { return; }

            lock (_statePathLock)
            {
                if (_statePathSkip.ContainsKey(iniPath)) { return; }
            }

            List<string> lines = ReadIni(iniPath);
            bool inState = false, hasState = false, hasPath = false;
            int lastStateLine = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                string t = lines[i].Trim();
                if (t.StartsWith("[") && t.EndsWith("]"))
                {
                    inState = t.Equals(StateSection, StringComparison.OrdinalIgnoreCase);
                    if (inState) { hasState = true; lastStateLine = i; }
                    continue;
                }
                if (!inState) { continue; }
                lastStateLine = i;
                if (t.StartsWith("Path=", StringComparison.OrdinalIgnoreCase)) { hasPath = true; break; }
            }

            if (!hasState || hasPath) { return; }

            string full;
            try { full = Path.GetFullPath(Plain(folder)); }
            catch { full = Plain(folder); }

            lines.Insert(lastStateLine + 1, "Path=" + full);

            FileAttributes attrs = FileAttributes.Hidden | FileAttributes.System;
            try { attrs = File.GetAttributes(Io(iniPath)); } catch { }

            WriteIni(iniPath, lines);
            try { File.SetAttributes(Io(iniPath), attrs); } catch { }

            // 校验是否真的写进去了（共享盘只读 / 权限不足时 WriteIni 会静默失败）。
            // 失败就记住，避免每轮巡检都重复写一次共享盘。
            bool ok = false;
            foreach (string l in ReadIni(iniPath))
            {
                if (l.Trim().StartsWith("Path=", StringComparison.OrdinalIgnoreCase)) { ok = true; break; }
            }
            if (!ok)
            {
                lock (_statePathLock) { _statePathSkip[iniPath] = true; }
                DebugLog("state path: 写入失败，本轮起跳过 " + iniPath);
                return;
            }

            DebugLog("state path: 补写 " + full);
        }
        catch { }
    }

    /// <summary>
    /// 判断这个 desktop.ini 是否属于「另一个路径」（复制或移动出来的副本）。
    /// 只对本地路径判定 —— UNC 一律不判、不还原，否则会把「另一台电脑标的共享盘文件夹」
    /// 当成副本清掉，共享盘同步就废了。
    /// </summary>
    private static bool IsForeignMark(string folder, string iniPath, out string ownerPath)
    {
        ownerPath = null;

        try
        {
            // 当前路径是共享盘（UNC 或映射盘符 Z:\）：一律不判 ——
            // 那里是跨电脑同步，不能当副本清掉。映射盘符必须算进来，
            // 否则同一份共享盘用 UNC 标、用 Z: 访问时会被误判成副本删除。
            if (IsNetworkPath(folder)) { return false; }

            string cur;
            try { cur = Path.GetFullPath(Plain(folder)); }
            catch { cur = Plain(folder); }

            FolderState st = LoadState(iniPath);

            // 新数据：以 State 里记录的标记路径为准
            if (st.Valid && !string.IsNullOrEmpty(st.Path))
            {
                // 记录里的源路径本身就在共享盘上（另一台电脑用 UNC 标的），
                // 说明这是共享盘同步来的标记，本地无论从哪访问都不能当副本清。
                if (IsNetworkPath(st.Path)) { return false; }

                if (SamePath(cur, st.Path)) { return false; }
                ownerPath = st.Path;
                return true;
            }

            // 老数据没有 Path=：用缓存图标名里的路径 hash 兜底
            // 缓存名格式 <源路径hash>-<color>.ico，和当前路径的 hash 不一致就是复制/移动来的。
            // 纯色图标（icons 里的 red.ico 等）不含 hash，无法判定，保持不动。
            string iconName, color;
            bool isCache;
            if (TryReadMarkedIcon(iniPath, out iconName, out color, out isCache) &&
                isCache && !string.IsNullOrEmpty(iconName))
            {
                int dash = iconName.IndexOf('-');
                if (dash > 0)
                {
                    string fileHash = iconName.Substring(0, dash);
                    if (!string.Equals(fileHash, PathHash(cur), StringComparison.OrdinalIgnoreCase))
                    {
                        ownerPath = "(按图标 hash 判定：源 " + fileHash + ")";
                        return true;
                    }
                }
            }

            return false;
        }
        catch { return false; }
    }

    private static readonly object _parentScanLock = new object();
    private static readonly Dictionary<string, DateTime> _parentScanAt =
        new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 复制检测：扫一遍已标记文件夹的同级目录，把「别人路径的标记」还原成普通文件夹。
    /// 只处理本地父目录（共享盘交给 roots/import），同一父目录 5 分钟内只扫一次。
    /// </summary>
    private static void TryRevertForeignCopies(string folder)
    {
        try
        {
            // 共享盘（UNC / 映射盘符）交给 roots/import 做同步，绝不当副本扫
            if (IsNetworkPath(folder)) { return; }

            string parent;
            try { parent = Path.GetDirectoryName(Path.GetFullPath(Plain(folder))); }
            catch { return; }
            if (string.IsNullOrEmpty(parent) || IsNetworkPath(parent)) { return; }

            lock (_parentScanLock)
            {
                DateTime last;
                if (_parentScanAt.TryGetValue(parent, out last) &&
                    (DateTime.UtcNow - last) < TimeSpan.FromMinutes(5))
                {
                    return;
                }
                if (_parentScanAt.Count > 512) { _parentScanAt.Clear(); }
                _parentScanAt[parent] = DateTime.UtcNow;
            }

            foreach (string d in Directory.GetDirectories(Io(parent)))
            {
                try
                {
                    string name = Path.GetFileName(d);
                    if (name.StartsWith(".") ||
                        name.Equals("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (SamePath(d, folder)) { continue; }

                    string ini = Path.Combine(d, "desktop.ini");
                    if (!File.Exists(ini)) { continue; }

                    string owner;
                    if (!IsForeignMark(d, ini, out owner)) { continue; }

                    DebugLog("revert copy: " + d + " <- 原路径 " + owner);
                    Main(new string[] { "clear", "none", Plain(d), "quiet" });
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>读 desktop.ini 里用户自己设的文件夹图标（不是本程序写的那些）</summary>
    private static string ExtractIconResource(string iniPath)
    {
        try
        {
            bool inSection = false;

            foreach (string line in ReadIni(iniPath))
            {
                string trimmed = line.Trim();

                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    inSection = trimmed.Equals("[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inSection ||
                    !trimmed.StartsWith("IconResource=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string value = trimmed.Substring("IconResource=".Length).Trim();
                if (value.Length == 0 || IsOwnIcon(value))
                {
                    return null;
                }

                return value;
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// 这个 IconResource 是不是我们自己写的。
    /// 四种来源：① %LOCALAPPDATA%\FolderMarker\...（icons 纯色 / cache 合成）；
    /// ② 塞进文件夹内的 FolderMarker.ico（旧版本）；③ custom\&lt;hash&gt;-&lt;color&gt;.ico（旧版本）；
    /// ④ 本机绝对路径形式的 cache 图标（shell 写入、还没改写成环境变量时）。
    /// 判错后果很严重 —— 会把我们自己的成品当成"用户原图标"再染一遍。
    /// </summary>
    private static bool IsOwnIcon(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        // ① 环境变量路径（当前方案）：%LOCALAPPDATA%\FolderMarker\icons\<color>.ico
        //    两台电脑各自展开到自己的安装目录，所以也算「我们的图标」
        if (value.IndexOf("%LOCALAPPDATA%", StringComparison.OrdinalIgnoreCase) >= 0 &&
            value.IndexOf("FolderMarker", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        // ①b 绝对路径形式的缓存图标（shell 刚写入、还没改写成环境变量时）
        if (value.IndexOf("FolderMarker\\cache\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
            value.IndexOf("FolderMarker/cache/", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        // ② 旧版本塞进文件夹内的 FolderMarker.ico（保留识别，便于清除旧标记）
        if (value.IndexOf(LocalIconName, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        string custom = CustomIconDir() + Path.DirectorySeparatorChar;
        if (value.IndexOf(custom, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        string mark = "icons" + Path.DirectorySeparatorChar;
        foreach (string color in ValidColors)
        {
            if (value.IndexOf(mark + color + ".ico", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> SetIconResource(List<string> lines, string iconValue)
    {
        List<string> result = new List<string>(lines);
        int header = -1;

        for (int i = 0; i < result.Count; i++)
        {
            string trimmed = result[i].Trim();
            if (trimmed.Equals("[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase))
            {
                header = i;
                break;
            }
        }

        if (header >= 0)
        {
            result.Insert(header + 1, "IconResource=" + iconValue);
        }
        else
        {
            if (result.Count > 0 && result[result.Count - 1].Trim().Length > 0)
            {
                result.Add("");
            }
            result.Add("[.ShellClassInfo]");
            result.Add("IconResource=" + iconValue);
        }

        return result;
    }
}
