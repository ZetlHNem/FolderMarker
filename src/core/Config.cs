using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// 配置与路径层：settings.ini 读写、开机自启、数据/缓存目录、图标标记解析。
// 从 FolderMarker.cs 拆出来 —— 集中管理所有「路径从哪来、设置存哪」的规则。

internal static partial class Program
{

    // ================= 设置（%LOCALAPPDATA%\FolderMarker\settings.ini）=================
    // 键值对格式，一行一个 key=value。没有 section —— 项少，简单即可，
    // 用户也能直接拿记事本改。

    /// <summary>程序版本号（与 FolderMarker.iss 的 MyAppVersion 保持一致）</summary>
    private const string AppVersion = "2.3.7";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "FolderMarkerCache";

    private static string SettingsPath()
    {
        return Path.Combine(DataDirPath(), "settings.ini");
    }

    private static string GetSetting(string key, string fallback)
    {
        try
        {
            string p = SettingsPath();
            if (!File.Exists(p)) { return fallback; }
            foreach (string line in File.ReadAllLines(p, Encoding.UTF8))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) { continue; }
                if (string.Equals(line.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                {
                    return line.Substring(eq + 1).Trim();
                }
            }
        }
        catch { }
        return fallback;
    }

    internal static int GetSettingInt(string key, int fallback)
    {
        int v;
        if (int.TryParse(GetSetting(key, fallback.ToString()), out v)) { return v; }
        return fallback;
    }

    private static void SetSetting(string key, string value)
    {
        try
        {
            string p = SettingsPath();
            List<string> lines = new List<string>();
            bool replaced = false;

            if (File.Exists(p))
            {
                foreach (string line in File.ReadAllLines(p, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0 &&
                        string.Equals(line.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    {
                        lines.Add(key + "=" + value);
                        replaced = true;
                    }
                    else
                    {
                        lines.Add(line);
                    }
                }
            }

            if (!replaced) { lines.Add(key + "=" + value); }

            Directory.CreateDirectory(DataDirPath());
            File.WriteAllLines(p, lines.ToArray(), new UTF8Encoding(false));
        }
        catch { }
    }

    /// <summary>开机自启是否开启（HKCU Run 里有这项就算开）</summary>
    private static bool GetAutoStart()
    {
        try
        {
            using (Microsoft.Win32.RegistryKey k =
                Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath))
            {
                if (k == null) { return false; }
                return k.GetValue(RunValueName) != null;
            }
        }
        catch { return false; }
    }

    private static bool SetAutoStart(bool on)
    {
        try
        {
            using (Microsoft.Win32.RegistryKey k =
                Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (k == null) { return false; }
                if (on)
                {
                    string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FolderMarker.exe");
                    k.SetValue(RunValueName, "\"" + exe + "\" watch");
                }
                else
                {
                    k.DeleteValue(RunValueName, false);
                }
            }
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// 日志写在 exe 旁边。之前写到 %LOCALAPPDATA%\FolderMarker，但卸载脚本清的是
    /// {app}\refresh.log —— 一旦产生过日志，卸载后那个目录就删不掉，留下残留。
    /// exe 目录写不进去（装到 Program Files 之类）时退回 LocalAppData。
    /// </summary>
    private static string LogDir()
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            return dir;
        }
        return DataDir();
    }

    private static string DataDirPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FolderMarker");
    }

    private static string DataDir()
    {
        string dir = DataDirPath();
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>给「已有自定义图标」的文件夹合成出来的图标放这里（只算路径，不建目录）</summary>
    private static string CustomIconDir()
    {
        return Path.Combine(DataDirPath(), "custom");
    }

    // ---------------------------------------------------------------------
    // 本机图标缓存：ico 只放这里，绝不写进被标记的文件夹 / 共享盘。
    // desktop.ini 用 %LOCALAPPDATA% 环境变量指向它，另一台电脑各自展开到自己的缓存。
    // ---------------------------------------------------------------------

    private static string CacheIconDir()
    {
        return Path.Combine(DataDirPath(), "cache");
    }

    /// <summary>缓存 ico 名字：&lt;路径哈希&gt;-&lt;颜色&gt;.ico。这个名字会写进 desktop.ini，别的电脑照抄它重建自己的缓存</summary>
    private static string CacheIconName(string folder, string color)
    {
        return PathHash(folder) + "-" + color + ".ico";
    }

    /// <summary>原样读 [.ShellClassInfo] 的 IconResource（不做 IsOwnIcon 过滤，巡检要读我们自己写的那条）</summary>
    private static string ReadIconResourceRaw(string iniPath)
    {
        try
        {
            bool inSection = false;
            foreach (string line in ReadIni(iniPath))
            {
                string t = line.Trim();
                if (t.StartsWith("[") && t.EndsWith("]"))
                {
                    inSection = t.Equals("[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (inSection && t.StartsWith("IconResource=", StringComparison.OrdinalIgnoreCase))
                {
                    return t.Substring("IconResource=".Length).Trim();
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// 从 desktop.ini 认出「本程序写的标记」，取出图标文件名与颜色。
    /// 两种形态都要认：
    ///   ① cache\&lt;hash&gt;-&lt;color&gt;.ico（有内容预览的合成图标）
    ///   ② icons\&lt;color&gt;.ico（纯色图标）
    /// 只认 ① 的话，另一台电脑扫描共享盘会漏掉所有纯色标记（它们永远同步不过去）。
    /// </summary>
    private static bool TryReadMarkedIcon(string iniPath, out string iconName, out string color, out bool isCache)
    {
        iconName = null;
        color = null;
        isCache = false;

        string v = ReadIconResourceRaw(iniPath);
        if (string.IsNullOrEmpty(v))
        {
            return false;
        }

        bool cache = v.IndexOf("FolderMarker\\cache\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     v.IndexOf("FolderMarker/cache/", StringComparison.OrdinalIgnoreCase) >= 0;

        int slash = v.LastIndexOfAny(new char[] { '\\', '/' });
        string name = slash >= 0 ? v.Substring(slash + 1) : v;
        int comma = name.IndexOf(',');
        if (comma >= 0)
        {
            name = name.Substring(0, comma);
        }

        if (!name.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string stem = name.Substring(0, name.Length - 4);

        // ① cache\<hash>-<color>.ico：颜色在最后一段
        // ② icons\<color>.ico：文件名本身就是颜色
        int dash = stem.LastIndexOf('-');
        string token = dash >= 0 ? stem.Substring(dash + 1) : stem;
        token = token.ToLowerInvariant();

        if (Array.IndexOf(ValidColors, token) < 0 && !IsCustomToken(token))
        {
            return false;
        }

        // 必须确认是「我们放的图标」。只凭文件名像 red.ico 就认，
        // 会把用户自己设的 C:\MyIcons\red.ico 当成我们的标记改写掉。
        // 纯色图标的路径可能是任意安装目录（不一定叫 FolderMarker），所以按 icons\<color>.ico 判。
        bool ourIcons =
            v.IndexOf("icons\\" + token + ".ico", StringComparison.OrdinalIgnoreCase) >= 0 ||
            v.IndexOf("icons/" + token + ".ico", StringComparison.OrdinalIgnoreCase) >= 0;

        if (!cache && !ourIcons)
        {
            return false;
        }

        if (cache && dash < 0)
        {
            return false;   // 缓存文件名一定是 <hash>-<color>.ico
        }

        iconName = name;
        color = token;
        isCache = cache;
        return true;
    }

    /// <summary>只要「指向本机缓存」的标记（管理台预览、重建用）</summary>
    private static bool TryReadCacheMark(string iniPath, out string iconName, out string color)
    {
        bool isCache;
        if (TryReadMarkedIcon(iniPath, out iconName, out color, out isCache) && isCache)
        {
            return true;
        }

        iconName = null;
        color = null;
        return false;
    }

}
