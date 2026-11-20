using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using WF = System.Windows.Forms;

// 托盘：宿主窗口 + 右键原生菜单（CreatePopupMenu / TrackPopupMenuEx）+ 菜单命令分发。
// 从 FolderMarker.cs 拆出来 —— 这块自带 P/Invoke 和常量，独立成文件后好找。

internal static partial class Program
{
    /// <summary>
    /// 托盘。winexe + [STAThread]，直接起 WinForms 消息循环。
    /// 单实例互斥；左键/双击图标 = 打开管理台，右键 = 完整菜单。
    /// </summary>
    /// <summary>
    /// 托盘宿主窗口：只用来跑消息循环、承载 NotifyIcon，绝不能出现在任务栏 / Alt+Tab。
    /// WinForms 里 Opacity=0 会重建窗口句柄，把 ShowInTaskbar 设上的 WS_EX_TOOLWINDOW 弄丢，
    /// 任务视图里就会多出一个 300x300 的空白窗口（实测）。这里直接重写 CreateParams 强制带上。
    /// </summary>
    private sealed class TrayHostForm : WF.Form
    {
        protected override WF.CreateParams CreateParams
        {
            get
            {
                WF.CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW
                return cp;
            }
        }
    }

    private static int RunTray()
    {
        bool created;
        System.Threading.Mutex m = new System.Threading.Mutex(true, "FolderMarkerTraySingleton", out created);
        if (!created) { m.Dispose(); return 0; }

        WF.NotifyIcon ni = null;
        try
        {
            WF.Form host = new TrayHostForm();
            host.ShowInTaskbar = false;
            host.FormBorderStyle = WF.FormBorderStyle.None;
            host.Opacity = 0;
            host.StartPosition = WF.FormStartPosition.Manual;
            // 不能放到屏幕外：原生菜单需要 SetForegroundWindow 成功，否则点别处菜单不消失。
            // 放 (0,0) 且 1x1，只占左上角 1 像素，肉眼无感。
            host.Location = new System.Drawing.Point(0, 0);
            host.Size = new System.Drawing.Size(1, 1);
            host.Text = "FolderMarkerTrayHost";

            ni = new WF.NotifyIcon();
            try
            {
                ni.Icon = System.Drawing.Icon.ExtractAssociatedIcon(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
            }
            catch { }
            ni.Text = "文件夹标记";
            ni.Visible = true;

            // 右键弹系统原生菜单：由 Windows 自己绘制，亚克力 / 圆角 / 动画 / 主题都跟系统走。
            // TrackPopupMenuEx 会阻塞到用户选完，但它内部有自己的消息循环，
            // 在 UI 线程调用是 Win32 的标准做法（放到后台线程反而没有消息泵，菜单会不响应）。
            ni.MouseUp += delegate(object s, WF.MouseEventArgs e)
            {
                if (e.Button != WF.MouseButtons.Right) { return; }

                try
                {
                    List<string> marked;
                    int cmd = ShowNativeTrayMenu(host.Handle, out marked);
                    if (cmd != 0) { RunTrayCommand(cmd, marked); }
                }
                catch { }
            };
            ni.DoubleClick += delegate
            {
                try
                {
                    System.Diagnostics.Process.Start(
                        System.Reflection.Assembly.GetExecutingAssembly().Location, "ui");
                }
                catch { }
            };

            // 托盘常驻时自动检查更新（不开管理台也会提示）：
            // 启动 30 秒后查一次，之后每 4 小时一次；受 AutoUpdate 开关控制。
            StartTrayUpdateCheck(ni, host);

            WF.Application.Run(host);
            return 0;
        }
        catch (Exception ex)
        {
            TryLog(ex);
            return 1;
        }
        finally
        {
            if (ni != null) { try { ni.Visible = false; ni.Dispose(); } catch { } }
            try { m.ReleaseMutex(); } catch { }
            m.Dispose();
        }
    }

    /// <summary>
    /// 托盘常驻的自动更新检查：启动 30 秒后一次，之后每 4 小时一次。
    /// 不打开管理台也能收到新版本提示，是「不打开管理页就不更新」的正确落点
    /// （托盘进程一直常驻，而管理台窗口只有用户主动打开才存在）。
    /// </summary>
    private static void StartTrayUpdateCheck(WF.NotifyIcon ni, WF.Form host)
    {
        try
        {
            WF.Timer timer = new WF.Timer();
            timer.Interval = 30 * 1000;              // 启动后 30 秒首次检查
            int ticks = 0;
            System.DateTime lastCheck = System.DateTime.MinValue;
            timer.Tick += delegate
            {
                // 首次之后改成每 4 小时（30 秒那次之后重置间隔）
                ticks++;
                if (ticks == 1)
                {
                    timer.Interval = 4 * 60 * 60 * 1000;
                }
                if (GetSettingInt("AutoUpdate", 1) == 0) { return; }
                // 避免重复：每 4 小时内只弹一次
                if ((System.DateTime.Now - lastCheck).TotalHours < 3.9) { return; }
                lastCheck = System.DateTime.Now;

                System.Threading.Thread t = new System.Threading.Thread(delegate()
                {
                    UpdateInfo info = CheckLatestVersion();
                    if (!info.Ok || !IsNewerVersion(info.Version, AppVersion)) { return; }
                    // ShowBalloonTip 必须在 UI 线程调用，否则抛 InvalidOperationException 气泡不显示
                    try
                    {
                        host.BeginInvoke(new Action(delegate()
                        {
                            ni.BalloonTipClicked -= OnBalloonClicked;
                            ni.BalloonTipClicked += OnBalloonClicked;
                            ni.Tag = info;
                            ni.ShowBalloonTip(8000, "文件夹标记",
                                "发现新版本 " + info.Version + "，点击这里下载更新", WF.ToolTipIcon.Info);
                        }));
                    }
                    catch { }
                });
                t.IsBackground = true;
                t.Start();
            };
            timer.Start();
        }
        catch { }
    }

    /// <summary>托盘气泡被点击：确认后下载并静默安装</summary>
    private static void OnBalloonClicked(object sender, System.EventArgs e)
    {
        WF.NotifyIcon ni = sender as WF.NotifyIcon;
        UpdateInfo info = ni != null ? ni.Tag as UpdateInfo : null;
        if (info == null) { return; }
        try
        {
            string msg = "发现新版本 " + info.Version + "（当前 " + AppVersion + "）。\r\n\r\n立即下载并更新？";
            if (WF.MessageBox.Show(msg, "文件夹标记 - 有可用更新",
                    WF.MessageBoxButtons.YesNo, WF.MessageBoxIcon.Question,
                    WF.MessageBoxDefaultButton.Button1) != WF.DialogResult.Yes)
            { return; }

            System.Threading.Thread t = new System.Threading.Thread(delegate()
            {
                string err = DownloadAndInstall(info, null);
                if (err != null)
                {
                    WF.MessageBox.Show("更新失败：" + err, "文件夹标记",
                        WF.MessageBoxButtons.OK, WF.MessageBoxIcon.Warning);
                }
                // 成功时安装器接管（静默安装会替换 exe 并重启托盘）
            });
            t.IsBackground = true;
            t.Start();
        }
        catch { }
    }

    // ---------------- 托盘菜单（Win32 原生，跟随系统样式）----------------
    // 为什么不用 ContextMenuStrip：它是 WinForms 自绘的 layered window，拿不到系统菜单的
    // 亚克力背景和动画。改用 CreatePopupMenu + TrackPopupMenuEx，交给 Windows 画，
    // 圆角 / 亚克力 / 淡入动画 / 深浅色主题 / 高 DPI 全部跟着系统走。

    private const int TrayCmdOpen = 1;
    private const int TrayCmdScan = 2;
    private const int TrayCmdDemark = 3;
    private const int TrayCmdPause = 4;
    private const int TrayCmdDataDir = 5;
    private const int TrayCmdStopWatch = 6;
    private const int TrayCmdQuit = 7;
    private const int TrayCmdMarkBase = 1000;

    private const uint MF_STRING = 0x0000;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint MF_POPUP = 0x0010;
    private const uint MF_CHECKED = 0x0008;
    private const uint MF_GRAYED = 0x0001;
    private const uint MF_DEFAULT = 0x1000;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_RIGHTBUTTON = 0x0002;

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    /// <summary>原生菜单里 &amp; 是助记符前缀，路径里的 &amp; 要写两次</summary>
    private static string MenuText(string s)
    {
        return string.IsNullOrEmpty(s) ? s : s.Replace("&", "&&");
    }

    /// <summary>
    /// 弹出系统原生托盘菜单，返回选中的命令 ID（0 = 没选）。
    /// markedOut 带回本次菜单里的标记列表，执行时按索引取，避免两次读取不一致。
    /// </summary>
    private static int ShowNativeTrayMenu(IntPtr owner, out List<string> markedOut)
    {
        markedOut = new List<string>();
        try
        {
            foreach (string p in ReadMarked())
            {
                string t = p.Trim();
                if (t.Length > 0) { markedOut.Add(t); }
            }
        }
        catch { }

        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) { return 0; }

        IntPtr sub = IntPtr.Zero;
        try
        {
            AppendMenuW(menu, MF_STRING | MF_DEFAULT, (IntPtr)TrayCmdOpen, MenuText("打开管理台"));

            sub = CreatePopupMenu();

            // 原生菜单不支持 tooltip（旧版 WinForms 菜单靠它显示完整路径），
            // 所以重名的文件夹会变成两条一模一样的条目。这里先统计重名，
            // 重名的补上父目录名，至少能分清是哪个。
            List<string> displays = new List<string>();
            Dictionary<string, int> nameCount =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in markedOut)
            {
                string disp = Path.GetFileName(Plain(path));
                if (disp.Length == 0) { disp = path; }
                displays.Add(disp);

                int c;
                nameCount.TryGetValue(disp, out c);
                nameCount[disp] = c + 1;
            }

            int n = 0;
            for (int i = 0; i < markedOut.Count; i++)
            {
                string disp = displays[i];
                if (nameCount[disp] > 1)
                {
                    string dir = Path.GetDirectoryName(Plain(markedOut[i]));
                    string parent = string.IsNullOrEmpty(dir) ? null : Path.GetFileName(dir);
                    if (!string.IsNullOrEmpty(parent)) { disp = parent + "\\" + disp; }
                }
                AppendMenuW(sub, MF_STRING, (IntPtr)(TrayCmdMarkBase + i), MenuText(disp));
                n++;
            }
            if (n == 0)
            {
                AppendMenuW(sub, MF_STRING | MF_GRAYED, IntPtr.Zero, MenuText("（还没有标记任何文件夹）"));
            }
            AppendMenuW(menu, MF_POPUP, sub, MenuText("已标记的文件夹"));
            sub = IntPtr.Zero;   // 已挂到父菜单，DestroyMenu(menu) 会一并回收

            AppendMenuW(menu, MF_SEPARATOR, IntPtr.Zero, null);
            AppendMenuW(menu, MF_STRING, (IntPtr)TrayCmdScan, MenuText("立即巡检一次"));
            AppendMenuW(menu, MF_STRING, (IntPtr)TrayCmdDemark, MenuText("清理复制来的标记…"));

            bool paused = GetSettingInt("Paused", 0) == 1;
            AppendMenuW(menu,
                MF_STRING | (paused ? MF_CHECKED : 0),
                (IntPtr)TrayCmdPause,
                MenuText(paused ? "恢复巡检" : "暂停巡检"));

            AppendMenuW(menu, MF_SEPARATOR, IntPtr.Zero, null);
            AppendMenuW(menu, MF_STRING, (IntPtr)TrayCmdDataDir, MenuText("打开数据目录"));

            bool watchRunning = false;
            try
            {
                string pp = Path.Combine(DataDirPath(), "watch.pid");
                if (File.Exists(Io(pp)))
                {
                    watchRunning = File.ReadAllText(Io(pp)).Trim().Length > 0;
                }
            }
            catch { }

            AppendMenuW(menu,
                MF_STRING | (watchRunning ? 0 : MF_GRAYED),
                (IntPtr)TrayCmdStopWatch,
                MenuText("退出守护进程"));

            AppendMenuW(menu, MF_SEPARATOR, IntPtr.Zero, null);
            AppendMenuW(menu, MF_STRING, (IntPtr)TrayCmdQuit, MenuText("退出托盘"));

            System.Drawing.Point pt = WF.Cursor.Position;

            // 这两步是原生托盘菜单的必备：不先抢前台，点别处菜单不消失
            SetForegroundWindow(owner);
            int cmd = TrackPopupMenuEx(menu,
                TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.X, pt.Y, owner, IntPtr.Zero);
            PostMessage(owner, 0x0000, IntPtr.Zero, IntPtr.Zero);   // WM_NULL

            return cmd;
        }
        finally
        {
            if (sub != IntPtr.Zero) { DestroyMenu(sub); }
            DestroyMenu(menu);
        }
    }

    /// <summary>执行托盘菜单选中的命令</summary>
    private static void RunTrayCommand(int cmd, List<string> marked)
    {
        try
        {
            switch (cmd)
            {
                case TrayCmdOpen:
                    System.Diagnostics.Process.Start(
                        System.Reflection.Assembly.GetExecutingAssembly().Location, "ui");
                    break;

                case TrayCmdScan:
                    System.Threading.Thread scanWorker = new System.Threading.Thread(delegate()
                    {
                        try { RefreshAll(null, false); } catch { }
                    });
                    scanWorker.IsBackground = true;
                    scanWorker.Start();
                    break;

                case TrayCmdDemark:
                    TrayDemarkDialog();
                    break;

                case TrayCmdPause:
                    SetSetting("Paused", GetSettingInt("Paused", 0) == 1 ? "0" : "1");
                    break;

                case TrayCmdDataDir:
                    System.Diagnostics.Process.Start("explorer.exe", DataDirPath());
                    break;

                case TrayCmdStopWatch:
                    try
                    {
                        string pp = Path.Combine(DataDirPath(), "watch.pid");
                        if (File.Exists(Io(pp)))
                        {
                            string pid = File.ReadAllText(Io(pp)).Trim();
                            if (pid.Length > 0)
                            {
                                System.Diagnostics.Process.GetProcessById(int.Parse(pid)).Kill();
                            }
                        }
                    }
                    catch { }
                    break;

                case TrayCmdQuit:
                    WF.Application.Exit();
                    break;

                default:
                    if (cmd >= TrayCmdMarkBase && marked != null)
                    {
                        int idx = cmd - TrayCmdMarkBase;
                        if (idx >= 0 && idx < marked.Count)
                        {
                            System.Diagnostics.Process.Start("explorer.exe", Plain(marked[idx]));
                        }
                    }
                    break;
            }
        }
        catch { }
    }

    /// <summary>「清理复制来的标记…」：选目录、执行、弹结果</summary>
    private static void TrayDemarkDialog()
    {
        try
        {
            using (WF.FolderBrowserDialog dlg = new WF.FolderBrowserDialog())
            {
                dlg.Description = "选择要扫描的目录：里面「从别处复制/移动过来」的标记会被还原成普通文件夹";
                dlg.ShowNewFolderButton = false;
                if (dlg.ShowDialog() != WF.DialogResult.OK) { return; }

                string picked = dlg.SelectedPath;
                WF.Cursor.Current = WF.Cursors.WaitCursor;
                try { Main(new string[] { "demark", picked, "quiet" }); }
                finally { WF.Cursor.Current = WF.Cursors.Default; }

                string msg = "已完成，详见数据目录的 refresh.log。";
                try
                {
                    string[] r = File.ReadAllLines(
                        Io(Path.Combine(DataDirPath(), "demark_result.txt")), Encoding.UTF8);
                    msg = "扫描到 " + r[0] + " 个标记，还原 " + r[1]
                        + " 个复制/移动来的副本。\r\n\r\n" + r[2];
                }
                catch { }
                WF.MessageBox.Show(msg, "文件夹标记", WF.MessageBoxButtons.OK, WF.MessageBoxIcon.Information);
            }
        }
        catch { }
    }
}
