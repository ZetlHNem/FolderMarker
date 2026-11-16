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
        // ---------------- 第 4 页：日志 ----------------
        private Panel BuildLogPage()
        {
            Grid page = new Grid();
            RowDefinition r0 = new RowDefinition();
            r0.Height = new GridLength(1, GridUnitType.Star);
            page.RowDefinitions.Add(r0);
            RowDefinition r1 = new RowDefinition();
            r1.Height = GridLength.Auto;
            page.RowDefinitions.Add(r1);

            Grid host = new Grid();

            logScroll = new ScrollViewer();
            logScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            logScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            logScroll.Padding = new Thickness(18, 14, 6, 14);
            Style svStyle = WinUi.S("WinScrollViewer");
            if (svStyle != null) { logScroll.Style = svStyle; }

            logText = new TextBlock();
            logText.FontFamily = new FontFamily(WinUi.FontMono);
            logText.FontSize = 12.5;
            logText.Foreground = WinUi.B("TextSecondary");
            logText.TextWrapping = TextWrapping.Wrap;
            logText.LineHeight = 19;
            logText.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            logScroll.Content = logText;

            host.Children.Add(logScroll);

            Border card = WinUi.Card(host, new Thickness(0));
            Grid.SetRow(card, 0);
            page.Children.Add(card);

            StackPanel bar = new StackPanel();
            bar.Orientation = Orientation.Horizontal;
            bar.HorizontalAlignment = HorizontalAlignment.Right;
            bar.Margin = new Thickness(2, 12, 2, 0);

            Button bReload = MakeButton("WinButton", "刷新");
            bReload.MinWidth = 80;
            bReload.Click += delegate { ReloadLog(); };
            bar.Children.Add(bReload);

            Button bOpen = MakeButton("WinSubtleButton", "打开日志文件");
            bOpen.MinWidth = 112;
            bOpen.Margin = new Thickness(8, 0, 0, 0);
            bOpen.Click += delegate
            {
                try { Process.Start("notepad.exe", Io(System.IO.Path.Combine(LogDir(), "refresh.log"))); }
                catch { }
            };
            bar.Children.Add(bOpen);

            Grid.SetRow(bar, 1);
            page.Children.Add(bar);
            return page;
        }

        private void ReloadLog()
        {
            if (logText == null) { return; }
            try
            {
                string p = Io(System.IO.Path.Combine(LogDir(), "refresh.log"));
                if (!File.Exists(p)) { logText.Text = "(暂无日志)"; return; }
                string[] lines = File.ReadAllLines(p, Encoding.UTF8);
                int start = Math.Max(0, lines.Length - 300);
                StringBuilder sb = new StringBuilder();
                for (int i = start; i < lines.Length; i++) { sb.AppendLine(lines[i]); }
                logText.Text = sb.ToString();
                logScroll.ScrollToEnd();
            }
            catch { logText.Text = "(读取日志失败)"; }
        }

        /// <summary>拖放：把文件夹拖进窗口，用当前选中的颜色标记</summary>
        private void OnFolderDrop(DragEventArgs e)
        {
            if (busy) { return; }
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) { return; }

            string[] items = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (items == null || items.Length == 0) { return; }

            List<string> dirs = new List<string>();
            foreach (string it in items)
            {
                if (Directory.Exists(it)) { dirs.Add(it); }
            }

            if (dirs.Count == 0)
            {
                WinUiDialog.Info(this, "只能标记文件夹", "拖进来的是文件。标记功能作用于文件夹。");
                return;
            }

            // 「自定义…」在 ValidColors 里没有对应项，直接索引会越界崩溃，
            // 这里和 Batch() 走同一条路：弹取色器，取消就什么都不做。
            string color;
            if (colorBox.SelectedIndex == ColorTitles.Length)
            {
                string token = ColorPickerWindow.Pick(dirs[0]);
                if (string.IsNullOrEmpty(token)) { return; }
                color = token;
            }
            else
            {
                color = ValidColors[Math.Max(0, colorBox.SelectedIndex)];
            }

            SelectNavPages(0);
            ApplyTo(dirs, color, false);
        }

    }
}
