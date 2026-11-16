using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Diagnostics;
using WF = System.Windows.Forms;

// 程序没有 app.config，运行时就会把「目标框架」按 4.0 处理，于是走 legacy 路径逻辑，
// 任何超过 MAX_PATH 的路径都直接抛 PathTooLongException（Directory.Exists 还会默默返回 false）。
// 显式声明目标框架（4.6.2 是长路径支持开始默认开启的版本）之后，
// 配合 Io() 加的 \\?\ 前缀，超长路径才能正常读写。
[assembly: TargetFramework(".NETFramework,Version=v4.6.2", FrameworkDisplayName = ".NET Framework 4.6.2")]

// FolderMarker - 文件夹彩色标记核心程序
// 编译为 GUI 子系统 (WindowsApplication)，因此永远不会创建控制台窗口。
// 用法: FolderMarker.exe <set|clear> <color|none> "<folder path>"
//
// 关键实现说明（踩过的坑都在这里）：
//   自己拼 desktop.ini 再发 SHChangeNotify，在 Windows 11 上几乎永远不生效 ——
//   资源管理器按「文件夹」缓存外观，通知不会让它重读 desktop.ini，
//   要等约 40~60 秒缓存过期才能看到变色（甚至 F5 也没用）。
//   正确做法是调 shell 自己的 SHGetSetFolderCustomSettings(FCS_FORCEWRITE)，
//   它既负责写 desktop.ini，又负责即时刷新该文件夹的外观缓存，实测 1 秒内见效。
//
//   另一个坑：往文件夹里写 desktop.ini，NTFS 会把该文件夹的「修改时间/访问时间」
//   刷成当前时间，shell 还会顺手给文件夹打上「只读」属性 —— 用户一看属性面板
//   就发现文件夹被改动了。所以标记前后要记下原始时间戳与属性，写完再原样恢复。

internal static partial class Program
{
    /// <summary>
    /// 确保托盘在跑。用户可能手动结束过托盘、或开机时被安全软件拦了，
    /// 打开管理台时顺手拉起来，省得「托盘图标不见了」找不到原因。
    /// 已有实例时新进程会被 FolderMarkerTraySingleton 互斥体挡掉，秒退，无害。
    /// </summary>
    private static void EnsureTrayRunning()
    {
        try
        {
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(
                System.Reflection.Assembly.GetExecutingAssembly().Location, "tray");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            System.Diagnostics.Process.Start(psi);
        }
        catch { }
    }

    private static int RunUi()
    {
        try
        {
            EnsureTrayRunning();

            System.Windows.Application app = new System.Windows.Application();
            app.Run(new ManagerWindow());
            return 0;
        }
        catch (Exception ex)
        {
            TryLog(ex);
            ShowFailure("管理台启动失败：\r\n\r\n" + ex.Message);
            return 90;
        }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        // 注意：watch 命令只带 1 个参数（安装脚本 / HKCU Run 都是「FolderMarker.exe watch」），
        // 所以这里最少只要求 1 个。早期写成 < 2，导致守护进程每次启动都直接 return 1、
        // 从来没真正跑起来过；用「watch 60」测试时反而被掩盖了。
        // 没有参数 = 用户双击了 exe，直接开管理台（以前是静默退出，看起来像程序坏了）。
        if (args.Length < 1)
        {
            return RunUi();
        }

        // 抓原版缩略图的 rename 舞步可能留下 .fm_tmp 残留（进程被杀），
        // 任何操作开始前先做一次廉价恢复（读一遍标记清单）。
        try { RecoverParkedIni(); } catch { }

        string action = args[0].Trim().ToLowerInvariant();
        string color = args.Length > 1 ? args[1].Trim().ToLowerInvariant() : "none";

        // 原生右键菜单多选时会在末尾追加 quiet：批量里个别失败不再弹一串模态框。
        // 参数位置固定，从第 4 个开始看（set red "C://x" quiet）。
        for (int i = 3; i < args.Length; i++)
        {
            if (args[i].Trim().Equals("quiet", StringComparison.OrdinalIgnoreCase))
            {
                Environment.SetEnvironmentVariable("FM_QUIET", "1");
            }
        }

        // 全局/诊断类命令：全部收敛到这里，每个只留一行转发。
        // 以前是十几个平铺的 if，找一条命令要在 400 行里翻。
        switch (action)
        {
            // 卸载时调用：把此前标记过的文件夹逐个还原成标记前的样子，然后删掉记录。
            // 必须在卸载程序删文件之前执行 —— 它要用到本 exe。
            case "cleanup":
                return RunCleanup();

            // 缓存巡检：一次性把缺失/内容变化的 ico 补齐
            case "refresh":
                return CmdRefresh(args);

            // 全盘扫一遍，还原复制/移动出来的副本标记
            case "autodemark":
                RunAutoDemark(true);
                return 0;

            // 手动清理：递归扫描一个目录，还原副本标记
            case "demark":
                return CmdDemark(args);

            // 管理台窗口
            case "ui":
                return RunUi();

            // 后台守护：登录自启，定时巡检
            case "watch":
                return CmdWatch(args);

            // 托盘
            case "tray":
                return RunTray();

            // 另一台电脑：导入共享盘上已有的标记
            case "import":
                return ImportRoot(args.Length > 1 ? args[1] : null);

            // 诊断用：让 shell 丢掉所有图标/缩略图缓存
            case "flush":
                NotifyPath(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, null, IntPtr.Zero);
                return 0;

            // 规则：立刻把 rules.txt 套用到标记清单
            case "rule":
                return CmdRule();

            // 诊断：输出一个文件夹的完整状态
            case "diagnose":
                return RunDiagnose(args.Length > 1 ? args[1] : null);

            // 回归自检
            case "selftest":
                return RunSelfTest();
        }

        // 剩下的是「针对单个文件夹」的命令
        if (args.Length < 3)
        {
            return 1;
        }

        return RunFolderCommand(action, color, args);
    }

    /// <summary>refresh：刷新缓存（可选指定共享盘根或 force 全量重画）</summary>
    private static int CmdRefresh(string[] args)
    {
        string root = null;
        bool force = false;
        if (args.Length > 2)
        {
            if (args[2].Trim().Equals("force", StringComparison.OrdinalIgnoreCase))
            {
                force = true;
            }
            else
            {
                root = args[2];
            }
        }
        RefreshAll(root, force);
        return 0;
    }

    /// <summary>demark：递归清理一个目录下「从别处复制/移动过来」的标记</summary>
    private static int CmdDemark(string[] args)
    {
        bool quiet = false;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i].Trim().Equals("quiet", StringComparison.OrdinalIgnoreCase)) { quiet = true; }
        }
        return RunDemark(args.Length > 1 ? args[1] : null, !quiet);
    }

    /// <summary>watch：后台守护，可选巡检间隔（秒）</summary>
    private static int CmdWatch(string[] args)
    {
        int seconds = 60;
        if (args.Length > 1 && !int.TryParse(args[1], out seconds)) { seconds = 60; }
        return RunWatch(seconds);
    }

    /// <summary>rule：按 rules.txt 批量套用规则</summary>
    private static int CmdRule()
    {
        List<string> changed;
        int n = ApplyRules(ReadMarked(), out changed);
        try { Console.WriteLine("已按规则更新 " + n.ToString() + " 个文件夹"); } catch { }
        return 0;
    }
    /// <summary>
    /// 超长路径支持：.NET 的文件 API 在 MAX_PATH 之外必须加 \\?\ 前缀
    /// （系统默认没开 LongPathsEnabled，裸路径会一律「不存在」）。
    /// 短路径原样返回，行为不变。注意：这个前缀只能给文件 API 用，
    /// 传给 shell 接口或命令行时要还原成普通路径。
    /// </summary>
    internal static string Io(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length < 240)
        {
            return path;
        }

        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return path;
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return @"\\?\UNC\" + path.Substring(2);
        }

        return @"\\?\" + path;
    }

    /// <summary>去掉 \\?\ 前缀，得到 shell 接口和资源管理器认识的普通路径</summary>
    private static string Plain(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        if (path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
        {
            return @"\\" + path.Substring(8);
        }

        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return path.Substring(4);
        }

        return path;
    }

    // ================= 外壳染色 =================
    //
    // imageres.dll 里的 5/6 号图标是黄色文件夹外壳。把像素换色相/饱和度、
    // 明度原样保留，就得到「任意颜色但仍是原厂外壳」的两层素材 ——
    // 渐变、阴影、圆角都在，比自己画一个文件夹准确得多。

    private static readonly int[] IconSizes =
        new int[] { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };


    /// <summary>与 ValidColors 一一对应，见 tools/tint_icons.py</summary>
    private static readonly double[] TargetHue = { 357.0, 30.0, 45.0, 145.0, 215.0, 272.0, 0.0 };
    private static readonly double[] TargetSat = { 0.95, 1.00, 0.94, 0.82, 0.90, 0.75, 0.00 };

    /// <summary>
    /// 崩溃恢复：抓原版缩略图时 desktop.ini 会被临时重命名成 .fm_tmp，
    /// 进程若在「移走 → 移回」之间被杀，文件夹就丢了标记、记录变幽灵。
    /// 启动/巡检时把标记清单里所有文件夹的这种残留移回原位。
    /// </summary>
    private static void RecoverParkedIni()
    {
        int fixedCount = 0;
        try
        {
            foreach (string raw in ReadMarked())
            {
                string f = raw.Trim();
                if (f.Length == 0) { continue; }
                try
                {
                    string ini = Path.Combine(Io(f), "desktop.ini");
                    string parked = ini + ".fm_tmp";
                    if (!File.Exists(parked)) { continue; }

                    if (!File.Exists(ini))
                    {
                        File.Move(parked, ini);
                        fixedCount++;
                        NotifyPath(SHCNE_UPDATEDIR, SHCNF_PATHW_FLUSH, f, IntPtr.Zero);
                    }
                    else
                    {
                        // ini 还在（用户自己恢复了或没死干净），残留没有保留价值
                        try { File.Delete(parked); } catch { }
                    }
                }
                catch { }
            }
        }
        catch { }

        if (fixedCount > 0)
        {
            DebugLog("recover: 移回 " + fixedCount.ToString() + " 个 .fm_tmp 残留");
        }
    }

    internal static void DebugLog(string msg)
    {
        try
        {
            string dir = LogDir();
            string log = Io(Path.Combine(dir, "refresh.log"));
            if (File.Exists(log) && new FileInfo(log).Length > 64 * 1024)
            {
                File.Delete(log);
            }
            File.AppendAllText(log, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + "\r\n", Encoding.UTF8);
        }
        catch { }
    }

}
