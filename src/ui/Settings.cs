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
        // ---------------- 设置页 ----------------
        private Panel BuildSettingsPage()
        {
            StackPanel page = new StackPanel();

            StackPanel card = new StackPanel();
            card.Margin = new Thickness(24, 20, 24, 22);

            tgAutoStart = MakeToggle();
            card.Children.Add(MakeRow("开机自动启动守护进程",
                "登录 Windows 时自动拉起后台巡检，缓存不会因为重启而缺失", tgAutoStart));

            card.Children.Add(Sep());

            tgAutoFollow = MakeToggle();
            card.Children.Add(MakeRow("内容变化时自动重画预览",
                "文件夹里换了图片/视频，图标里的内容预览跟着更新；关掉只补缺失的缓存", tgAutoFollow));

            card.Children.Add(Sep());

            tgAutoUpdate = MakeToggle();
            card.Children.Add(MakeRow("启动时自动检查更新",
                "从 GitHub 检查新版本，发现后提示你选择是否更新；关掉就只在「关于」页手动检查", tgAutoUpdate));

            card.Children.Add(Sep());

            cbTheme = MakeCombo(
                new string[] { "跟随系统", "浅色", "深色" },
                new int[] { 0, 1, 2 });
            card.Children.Add(MakeRow("外观主题",
                "管理台和取色器的深浅色；改完重启管理台生效", cbTheme));

            card.Children.Add(Sep());

            cbWatch = MakeCombo(
                new string[] { "30 秒", "1 分钟", "2 分钟", "5 分钟", "10 分钟" },
                new int[] { 30, 60, 120, 300, 600 });
            card.Children.Add(MakeRow("巡检间隔",
                "多久检查一次已标记文件夹的内容变化", cbWatch));

            card.Children.Add(Sep());

            cbScan = MakeCombo(
                new string[] { "2 分钟", "5 分钟", "15 分钟", "30 分钟", "60 分钟" },
                new int[] { 2, 5, 15, 30, 60 });
            card.Children.Add(MakeRow("共享盘扫描间隔",
                "多久扫一次共享盘根，发现另一台电脑新标的文件夹（带随机抖动；扫得慢的大根自动拉长到 30 分钟）", cbScan));

            StackPanel btns = new StackPanel();
            btns.Orientation = Orientation.Horizontal;
            btns.Margin = new Thickness(0, 20, 0, 0);

            Button save = MakeButton("WinAccentButton", "保存设置");
            save.MinWidth = 104;
            save.Click += delegate { SaveSettings(); };
            btns.Children.Add(save);

            Button reload = MakeButton("WinButton", "重新载入");
            reload.MinWidth = 92;
            reload.Margin = new Thickness(8, 0, 0, 0);
            reload.Click += delegate { LoadSettings(); };
            btns.Children.Add(reload);

            card.Children.Add(btns);

            // 自动规则
            Border sepRules = new Border();
            sepRules.Height = 1;
            sepRules.Background = WinUi.B("DividerStroke");
            sepRules.Margin = new Thickness(0, 22, 0, 16);
            card.Children.Add(sepRules);

            TextBlock rulesTitle = new TextBlock();
            rulesTitle.Text = "自动规则";
            rulesTitle.FontSize = WinUi.SizeBody;
            rulesTitle.Foreground = WinUi.B("TextPrimary");
            card.Children.Add(rulesTitle);

            TextBlock rulesHint = new TextBlock();
            rulesHint.Text = "每行一条，自上而下第一条命中生效。示例：\r\n" +
                             "contains:归档  => gray          路径含「归档」→ 灰色\r\n" +
                             "contains:终版  => green star     含「终版」→ 绿色 + ★\r\n" +
                             "older:30       => orange alert   30 天没改过 → 橙色 + !\r\n" +
                             "只作用于已经在清单里的文件夹，守护进程每分钟会自动套用。";
            rulesHint.FontSize = WinUi.SizeCaption;
            rulesHint.Foreground = WinUi.B("TextSecondary");
            rulesHint.TextWrapping = TextWrapping.Wrap;
            rulesHint.Margin = new Thickness(0, 4, 0, 8);
            card.Children.Add(rulesHint);

            rulesBox = new TextBox();
            Style rbStyle = WinUi.S("WinTextBox");
            if (rbStyle != null) { rulesBox.Style = rbStyle; }
            rulesBox.AcceptsReturn = true;
            rulesBox.TextWrapping = TextWrapping.NoWrap;
            rulesBox.FontFamily = new FontFamily(WinUi.FontMono);
            rulesBox.FontSize = 12;
            rulesBox.Height = 112;
            rulesBox.Padding = new Thickness(10, 8, 10, 8);
            rulesBox.VerticalContentAlignment = VerticalAlignment.Top;
            rulesBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            card.Children.Add(rulesBox);

            StackPanel ruleBtns = new StackPanel();
            ruleBtns.Orientation = Orientation.Horizontal;
            ruleBtns.Margin = new Thickness(0, 8, 0, 0);

            Button saveRules = MakeButton("WinButton", "保存规则");
            saveRules.MinWidth = 96;
            saveRules.Click += delegate { SaveRules(); };
            ruleBtns.Children.Add(saveRules);

            Button applyRules = MakeButton("WinButton", "立即应用");
            applyRules.MinWidth = 96;
            applyRules.Margin = new Thickness(8, 0, 0, 0);
            applyRules.Click += delegate
            {
                SaveRules();
                List<string> changed;
                int n = Program.ApplyRules(ReadMarked(), out changed);
                WinUiDialog.Info(this, "规则已应用", n == 0
                    ? "没有需要调整的文件夹。"
                    : "已按规则更新 " + n.ToString() + " 个文件夹。");
                ReloadMarks();
            };
            ruleBtns.Children.Add(applyRules);

            card.Children.Add(ruleBtns);

            // 危险操作区
            Border sep2 = new Border();
            sep2.Height = 1;
            sep2.Background = WinUi.B("DividerStroke");
            sep2.Margin = new Thickness(0, 22, 0, 16);
            card.Children.Add(sep2);

            Grid danger = new Grid();
            ColumnDefinition d0 = new ColumnDefinition();
            d0.Width = new GridLength(1, GridUnitType.Star);
            danger.ColumnDefinitions.Add(d0);
            ColumnDefinition d1 = new ColumnDefinition();
            d1.Width = GridLength.Auto;
            danger.ColumnDefinitions.Add(d1);

            StackPanel dTexts = new StackPanel();
            dTexts.VerticalAlignment = VerticalAlignment.Center;

            TextBlock dTitle = new TextBlock();
            dTitle.Text = "清除全部标记";
            dTitle.FontSize = WinUi.SizeBody;
            dTitle.Foreground = WinUi.B("TextPrimary");
            dTexts.Children.Add(dTitle);

            TextBlock dDesc = new TextBlock();
            dDesc.Text = "把所有已标记文件夹恢复成原来的图标，这个操作不能撤销";
            dDesc.FontSize = WinUi.SizeCaption;
            dDesc.Foreground = WinUi.B("TextSecondary");
            dDesc.TextWrapping = TextWrapping.Wrap;
            dDesc.Margin = new Thickness(0, 3, 0, 0);
            dTexts.Children.Add(dDesc);

            danger.Children.Add(dTexts);

            Button bClearAll = MakeButton("WinButton", "清除全部标记");
            bClearAll.MinWidth = 116;
            bClearAll.Margin = new Thickness(16, 0, 0, 0);
            bClearAll.VerticalAlignment = VerticalAlignment.Center;
            bClearAll.Foreground = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
            bClearAll.Click += delegate { ClearAllMarks(); };
            Grid.SetColumn(bClearAll, 1);
            danger.Children.Add(bClearAll);

            card.Children.Add(danger);

            page.Children.Add(WinUi.Card(card, new Thickness(0)));

            TextBlock note = new TextBlock();
            note.Text = "设置保存在 %LOCALAPPDATA%\\FolderMarker\\settings.ini；保存后会重启守护进程，让新间隔立即生效。";
            note.Foreground = WinUi.B("TextTertiary");
            note.FontSize = WinUi.SizeCaption;
            note.TextWrapping = TextWrapping.Wrap;
            note.Margin = new Thickness(4, 14, 4, 0);
            page.Children.Add(note);

            return page;
        }

        private static Border Sep()
        {
            Border b = new Border();
            b.Height = 1;
            b.Background = WinUi.B("DividerStroke");
            b.Margin = new Thickness(0, 14, 0, 14);
            return b;
        }

        private static CheckBox MakeToggle()
        {
            CheckBox cb = new CheckBox();
            Style s = WinUi.S("WinToggle");
            if (s != null) { cb.Style = s; }
            cb.VerticalAlignment = VerticalAlignment.Center;
            return cb;
        }

        private static ComboBox MakeCombo(string[] labels, int[] values)
        {
            ComboBox cb = new ComboBox();
            Style s = WinUi.S("WinComboBox");
            if (s != null) { cb.Style = s; }
            cb.Width = 128;
            cb.VerticalAlignment = VerticalAlignment.Center;
            foreach (string l in labels) { cb.Items.Add(l); }
            cb.SelectedIndex = 0;
            cb.Tag = values;
            return cb;
        }

        /// <summary>设置项一行：左侧标题+说明，右侧控件</summary>
        private static Border MakeRow(string title, string desc, UIElement control)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 2, 0, 2);

            ColumnDefinition c0 = new ColumnDefinition();
            c0.Width = new GridLength(1, GridUnitType.Star);
            g.ColumnDefinitions.Add(c0);
            ColumnDefinition c1 = new ColumnDefinition();
            c1.Width = GridLength.Auto;
            g.ColumnDefinitions.Add(c1);

            StackPanel texts = new StackPanel();
            texts.VerticalAlignment = VerticalAlignment.Center;

            TextBlock t = new TextBlock();
            t.Text = title;
            t.FontSize = WinUi.SizeBody;
            t.Foreground = WinUi.B("TextPrimary");
            texts.Children.Add(t);

            TextBlock d = new TextBlock();
            d.Text = desc;
            d.FontSize = WinUi.SizeCaption;
            d.Foreground = WinUi.B("TextSecondary");
            d.TextWrapping = TextWrapping.Wrap;
            d.Margin = new Thickness(0, 3, 0, 0);
            texts.Children.Add(d);

            g.Children.Add(texts);

            FrameworkElement fe = control as FrameworkElement;
            if (fe != null)
            {
                fe.Margin = new Thickness(16, 0, 0, 0);
                fe.VerticalAlignment = VerticalAlignment.Center;
            }
            Grid.SetColumn(control, 1);
            g.Children.Add(control);

            Border host = new Border();
            host.Child = g;
            return host;
        }

        private void LoadSettings()
        {
            if (tgAutoStart != null)
            {
                tgAutoStart.IsChecked = GetAutoStart();
            }
            if (tgAutoFollow != null)
            {
                tgAutoFollow.IsChecked = GetSettingInt("AutoFollow", 1) != 0;
            }
            if (tgAutoUpdate != null)
            {
                tgAutoUpdate.IsChecked = GetSettingInt("AutoUpdate", 1) != 0;
            }
            if (rulesBox != null)
            {
                try
                {
                    rulesBox.Text = File.Exists(Program.RulesPath())
                        ? File.ReadAllText(Program.RulesPath(), System.Text.Encoding.UTF8)
                        : "";
                }
                catch { rulesBox.Text = ""; }
            }
            if (cbTheme != null)
            {
                int v = GetSettingInt("Theme", 0);
                int[] vals = (int[])cbTheme.Tag;
                int idx = Array.IndexOf(vals, v);
                cbTheme.SelectedIndex = idx >= 0 ? idx : 0;
            }
            if (cbWatch != null)
            {
                int v = GetSettingInt("WatchInterval", 60);
                int[] vals = (int[])cbWatch.Tag;
                int idx = Array.IndexOf(vals, v);
                cbWatch.SelectedIndex = idx >= 0 ? idx : 1;
            }
            if (cbScan != null)
            {
                int v = GetSettingInt("RootScanMinutes", Program.DefaultRootScanMinutes);
                int[] vals = (int[])cbScan.Tag;
                int idx = Array.IndexOf(vals, v);
                cbScan.SelectedIndex = idx >= 0 ? idx : 0;
            }
        }

        private void SaveRules()
        {
            try
            {
                string text = (rulesBox.Text ?? "").Replace("\r\n", "\n").TrimEnd();
                string path = Program.RulesPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                WinUiDialog.Info(this, "规则没保存成功", ex.Message);
            }
        }

        private void SaveSettings()
        {
            bool autoStart = tgAutoStart != null && tgAutoStart.IsChecked == true;
            bool autoFollow = tgAutoFollow == null || tgAutoFollow.IsChecked == true;

            int watch = 60;
            if (cbWatch != null && cbWatch.SelectedIndex >= 0)
            {
                int[] vals = (int[])cbWatch.Tag;
                watch = vals[cbWatch.SelectedIndex];
            }

            int theme = 0;
            if (cbTheme != null && cbTheme.SelectedIndex >= 0)
            {
                int[] vals = (int[])cbTheme.Tag;
                theme = vals[cbTheme.SelectedIndex];
            }

            int scan = Program.DefaultRootScanMinutes;
            if (cbScan != null && cbScan.SelectedIndex >= 0)
            {
                int[] vals = (int[])cbScan.Tag;
                scan = vals[cbScan.SelectedIndex];
            }

            SetSetting("WatchInterval", watch.ToString());
            SetSetting("RootScanMinutes", scan.ToString());
            SetSetting("AutoFollow", autoFollow ? "1" : "0");
            SetSetting("AutoUpdate", (tgAutoUpdate != null && tgAutoUpdate.IsChecked == true) ? "1" : "0");
            SetSetting("Theme", theme.ToString());

            bool regOk = SetAutoStart(autoStart);

            // 让新间隔立即生效：重启守护（正在跑才重启）
            bool wasRunning = false;
            try
            {
                using (System.Threading.Mutex.OpenExisting("FolderMarkerWatchSingleton")) { wasRunning = true; }
            }
            catch { }

            if (wasRunning || autoStart)
            {
                StopWatchProcesses();
                System.Threading.Thread.Sleep(400);
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(ExePath(), "watch");
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    Process.Start(psi);
                }
                catch { }
                System.Threading.Thread.Sleep(600);
            }

            UpdateWatchStatus();

            if (!regOk)
            {
                WinUiDialog.Info(this, "部分设置未生效",
                    "开机自启写注册表失败（可能被安全软件拦截），其余设置已保存。");
                return;
            }

            WinUiDialog.Info(this, "设置已保存",
                "巡检间隔 " + watch.ToString() + " 秒，共享盘扫描 " + scan.ToString() + " 分钟。"
                + (autoStart ? "已开启开机自启。" : "已关闭开机自启。")
                + (theme != 0 ? "\r\n\r\n主题改动需要重启管理台后生效。" : ""));
        }

        /// <summary>危险操作：清除所有标记（带数量确认）</summary>
        private void ClearAllMarks()
        {
            List<string> all = new List<string>();
            foreach (string line in ReadMarked())
            {
                string f = Plain(line.Trim());
                if (f.Length > 0) { all.Add(f); }
            }

            if (all.Count == 0)
            {
                WinUiDialog.Info(this, "没有标记", "当前没有任何已标记的文件夹。");
                return;
            }

            if (!WinUiDialog.Confirm(this, "清除全部标记",
                "将把 " + all.Count.ToString() + " 个文件夹恢复成原来的图标。"
                + "这个操作不能撤销，确定继续？", "全部清除"))
            {
                return;
            }

            ApplyTo(all, "none", true);
        }

    }
}
