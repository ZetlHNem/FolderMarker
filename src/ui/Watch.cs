using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;

// 管理台的一页：从 ManagerWindow.cs 拆出来。页面构建 + 该页辅助逻辑放在一起，
// 改某一页不用在 2800 行里翻。类仍是 partial，共享全部字段和方法。

internal static partial class Program
{
    internal sealed partial class ManagerWindow : Window
    {
        // ---------------- 第 2 页：守护进程 ----------------
        private Panel BuildWatchPage()
        {
            StackPanel page = new StackPanel();

            StackPanel card = new StackPanel();
            card.Margin = new Thickness(24, 20, 24, 22);

            Grid status = new Grid();
            ColumnDefinition c0 = new ColumnDefinition();
            c0.Width = GridLength.Auto;
            status.ColumnDefinitions.Add(c0);
            ColumnDefinition c1 = new ColumnDefinition();
            c1.Width = new GridLength(1, GridUnitType.Star);
            status.ColumnDefinitions.Add(c1);

            watchDot = new Border();
            watchDot.Width = 10;
            watchDot.Height = 10;
            watchDot.CornerRadius = new CornerRadius(5);
            watchDot.Background = WinUi.B("TextTertiary");
            watchDot.VerticalAlignment = VerticalAlignment.Center;
            watchDot.Margin = new Thickness(0, 0, 12, 0);
            status.Children.Add(watchDot);

            StackPanel texts = new StackPanel();
            watchState = WinUi.Text("检查中…", 16, WinUi.B("TextPrimary"));
            watchState.FontWeight = FontWeights.SemiBold;
            texts.Children.Add(watchState);
            watchHint = WinUi.Text("", WinUi.SizeCaption, WinUi.B("TextSecondary"));
            watchHint.Margin = new Thickness(0, 3, 0, 0);
            texts.Children.Add(watchHint);

            Grid.SetColumn(texts, 1);
            status.Children.Add(texts);
            card.Children.Add(status);

            Border sep = new Border();
            sep.Height = 1;
            sep.Background = WinUi.B("DividerStroke");
            sep.Margin = new Thickness(0, 18, 0, 16);
            card.Children.Add(sep);

            card.Children.Add(InfoRow(WinUi.GlyphRefresh,
                "每 60 秒巡检一次已标记文件夹，内容变了就重画内容预览。", out watchRowInterval));
            card.Children.Add(InfoRow(WinUi.GlyphSync,
                "开机登录时自动启动，并强制全量重画一次；缓存被删也会补回来。"));
            card.Children.Add(InfoRow(WinUi.GlyphCloud,
                "共享盘根每 5 分钟扫一次（大根自动降为 30 分钟），发现另一台新标的文件夹。", out watchRowScan));

            StackPanel btns = new StackPanel();
            btns.Orientation = Orientation.Horizontal;
            btns.Margin = new Thickness(0, 20, 0, 0);

            btnStart = MakeButton("WinAccentButton", "启动守护");
            btnStart.MinWidth = 100;
            btnStart.Click += delegate
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(ExePath(), "watch");
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    Process.Start(psi);
                }
                catch { }
                System.Threading.Thread.Sleep(700);
                UpdateWatchStatus();
            };
            btns.Children.Add(btnStart);

            btnStop = MakeButton("WinButton", "停止守护");
            btnStop.MinWidth = 100;
            btnStop.Margin = new Thickness(8, 0, 0, 0);
            btnStop.Click += delegate
            {
                StopWatchProcesses();
                System.Threading.Thread.Sleep(500);
                UpdateWatchStatus();
            };
            btns.Children.Add(btnStop);

            Button bRef = MakeButton("WinButton", "立即巡检");
            bRef.MinWidth = 100;
            bRef.Margin = new Thickness(8, 0, 0, 0);
            bRef.Click += delegate { RunAsync("refresh none", "巡检中…"); };
            btns.Children.Add(bRef);

            Button bForce = MakeButton("WinButton", "强制全部重画");
            bForce.MinWidth = 124;
            bForce.Margin = new Thickness(8, 0, 0, 0);
            bForce.Click += delegate { RunAsync("refresh none force", "全部重画中…"); };
            btns.Children.Add(bForce);

            card.Children.Add(btns);
            page.Children.Add(WinUi.Card(card, new Thickness(0)));

            TextBlock hint = new TextBlock();
            hint.Text = "任务管理器「详细信息」页可看到 FolderMarker.exe（命令行带 watch）。停掉守护后内容变化不再自动更新，但已有缓存图标仍正常显示。";
            hint.Foreground = WinUi.B("TextTertiary");
            hint.FontSize = WinUi.SizeCaption;
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(4, 14, 4, 0);
            page.Children.Add(hint);

            return page;
        }

        private static UIElement InfoRow(string glyph, string text)
        {
            TextBlock ignored;
            return InfoRow(glyph, text, out ignored);
        }

        /// <summary>返回行里的文字块，方便设置改了之后就地更新文案</summary>
        private static UIElement InfoRow(string glyph, string text, out TextBlock label)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 5, 0, 5);

            ColumnDefinition c0 = new ColumnDefinition();
            c0.Width = new GridLength(24);
            g.ColumnDefinitions.Add(c0);
            ColumnDefinition c1 = new ColumnDefinition();
            c1.Width = new GridLength(1, GridUnitType.Star);
            g.ColumnDefinitions.Add(c1);

            TextBlock ic = WinUi.Glyph(glyph, 14, WinUi.B("TextSecondary"));
            ic.HorizontalAlignment = HorizontalAlignment.Left;
            ic.VerticalAlignment = VerticalAlignment.Top;
            ic.Margin = new Thickness(0, 2, 0, 0);
            g.Children.Add(ic);

            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = 13.5;
            t.Foreground = WinUi.B("TextSecondary");
            t.TextWrapping = TextWrapping.Wrap;
            t.LineHeight = 20;
            t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            Grid.SetColumn(t, 1);
            g.Children.Add(t);

            label = t;
            return g;
        }

        /// <summary>秒数 → 「30 秒 / 1 分钟 / 2 分钟」</summary>
        private static string DescribeSpan(int seconds)
        {
            if (seconds < 60) { return seconds.ToString() + " 秒"; }
            if (seconds % 60 == 0) { return (seconds / 60).ToString() + " 分钟"; }
            return seconds.ToString() + " 秒";
        }

        private void UpdateWatchStatus()
        {
            if (watchState == null) { return; }

            bool running = false;
            try
            {
                using (System.Threading.Mutex.OpenExisting("FolderMarkerWatchSingleton")) { running = true; }
            }
            catch { }

            watchState.Text = running ? "运行中" : "未运行";

            // 这两行文案跟着设置走。以前是写死的「每 60 秒 / 每 5 分钟」，
            // 用户在设置页改成 30 秒，这里还显示 60 秒。
            int interval = GetSettingInt("WatchInterval", 60);
            if (watchRowInterval != null)
            {
                watchRowInterval.Text = "每 " + DescribeSpan(interval)
                    + " 巡检一次已标记文件夹，内容变了就重画内容预览。";
            }

            int scanMinutes = GetSettingInt("RootScanMinutes", Program.DefaultRootScanMinutes);
            if (watchRowScan != null)
            {
                watchRowScan.Text = "共享盘根约每 " + DescribeSpan(scanMinutes * 60)
                    + " 扫一次（随机抖动错峰；慢根自动拉长到 30 分钟），发现另一台新标的文件夹。";
            }

            string lastRun = "";
            try
            {
                string lp = Io(System.IO.Path.Combine(LogDir(), "refresh.log"));
                if (File.Exists(lp))
                {
                    DateTime t = File.GetLastWriteTime(lp);
                    lastRun = " · 上次活动 " + t.ToString("MM-dd HH:mm:ss");
                }
            }
            catch { }

            watchHint.Text = (running
                ? "缓存巡检正常，内容变化会自动更新"
                : "内容变化不会自动更新，可点右侧「启动守护」") + lastRun;
            watchDot.Background = running
                ? new SolidColorBrush(Color.FromRgb(0x0F, 0x7B, 0x0F))
                : new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));

            if (btnStart != null) { btnStart.IsEnabled = !running; }
            if (btnStop != null) { btnStop.IsEnabled = running; }
        }

        private static void StopWatchProcesses()
        {
            // 以前这里把「所有没有主窗口的 FolderMarker.exe」全 Kill，
            // 正在标色 / 批量处理的无窗口子进程也会被一起打死，desktop.ini 可能写到一半。
            // 现在只杀守护进程自己：它启动时会把 PID 写进 watch.pid。
            int pid = 0;
            DateTime writtenAt = DateTime.MinValue;
            try
            {
                string p = WatchPidPath();
                if (File.Exists(p))
                {
                    writtenAt = File.GetLastWriteTimeUtc(p);
                    int.TryParse(File.ReadAllText(p).Trim(), out pid);
                }
            }
            catch { }

            if (pid > 0)
            {
                try
                {
                    Process wp = Process.GetProcessById(pid);

                    // PID 会被系统复用：只有在 pid 文件写下之后不久启动的那个进程才是守护进程。
                    bool same = wp.ProcessName.Equals("FolderMarker", StringComparison.OrdinalIgnoreCase);
                    if (same && writtenAt != DateTime.MinValue)
                    {
                        same = wp.StartTime.ToUniversalTime() <= writtenAt.AddSeconds(5);
                    }

                    if (same)
                    {
                        wp.Kill();
                        wp.WaitForExit(4000);
                    }
                }
                catch { }
            }
            else
            {
                StopWatchByCommandLine();
            }

            try { File.Delete(WatchPidPath()); }
            catch { }
        }

        /// <summary>
        /// 老版本守护进程没有 watch.pid。这时按命令行精确匹配（和安装脚本同一套做法），
        /// 仍然只动带 watch 参数的那个进程。
        /// </summary>
        private static void StopWatchByCommandLine()
        {
            try
            {
                string script =
                    "Get-CimInstance Win32_Process -Filter \\\"Name='FolderMarker.exe'\\\" | "
                    + "Where-Object { $_.CommandLine -like '* watch*' } | "
                    + "ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }";

                ProcessStartInfo psi = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -NonInteractive -WindowStyle Hidden -Command \"" + script + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;

                Process p = Process.Start(psi);
                p.WaitForExit(8000);
            }
            catch { }
        }

    }
}
