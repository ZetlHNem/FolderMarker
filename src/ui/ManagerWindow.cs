// 管理台（WinUI 3 视觉规格）—— 侧边导航 + Mica 云母背景 + 自绘标题栏。
// 作为 partial Program 的嵌套类，可直接复用主程序里的 private static 函数。
// 不写 XAML 文件：模板集中在 WinUi.cs（XamlReader 加载），这里只用代码组装布局，
// 这样现有 CodeDom 构建链（C# 5）无需 XamlCompiler 也能编译。
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

internal static partial class Program
{
    internal sealed partial class ManagerWindow : Window
    {
        // ---------------- 常量 ----------------
        private const double NavWidth = 296;
        private const double TitleBarHeight = 48;
        private const double ContentPadX = 40;
        private const double RowHeight = 44;

        // 列表列宽（表头与数据行必须一致）
        private const double ColColor = 138;   // 要能放下「自定义 #F27900」
        private const double ColCache = 132;
        private const double ColDir = 84;

        private static readonly string[] Titles = { "标记清单", "守护进程", "共享盘同步", "日志", "设置", "关于" };
        private static readonly string[] Subs =
        {
            "右键任意文件夹 → 文件夹标记 → 选一个颜色，图标立即变色",
            "后台巡检缓存：内容变了自动重画，重启也不会缺图",
            "添加共享盘根，另一台电脑新标的文件夹会自动同步过来",
            "最近的巡检与重建记录",
            "启动方式、巡检频率与内容跟随",
            "版本、数据目录与诊断工具"
        };
        private static readonly string[] NavGlyphs =
        {
            WinUi.GlyphFolder,
            WinUi.GlyphSync,
            WinUi.GlyphCloud,
            WinUi.GlyphDocument,
            WinUi.GlyphSettings,
            WinUi.GlyphInfo
        };
        private static readonly string[] ColorTitles =
            { "红色", "橙色", "黄色", "绿色", "蓝色", "紫色", "灰色" };

        // ---------------- 框架控件 ----------------
        private readonly List<Border> navItems = new List<Border>();
        private readonly List<Border> navBars = new List<Border>();
        private readonly List<Border> navHovers = new List<Border>();
        private readonly List<TextBlock> navIcons = new List<TextBlock>();
        private readonly List<TextBlock> navTexts = new List<TextBlock>();
        private readonly List<Panel> pages = new List<Panel>();
        private readonly TextBlock pageTitle = new TextBlock();
        private readonly TextBlock pageSub = new TextBlock();
        private Border shell;
        private Border busyBar;
        private TextBlock maxGlyph;

        // ---------------- 页面控件 ----------------
        private ListView list;
        private TextBlock countText;
        private StackPanel emptyState;
        private TextBlock emptyTitle;
        private TextBlock emptyHint;
        private ComboBox colorBox;
        private TextBox searchBox;
        private ComboBox filterBox;
        private Border detailPanel;
        private Border detailIconBox;
        private ComboBox detailSizeBox;
        private Image detailIcon;
        private TextBlock detailTitle;
        private TextBlock detailMeta;
        private readonly List<MarkRow> allRows = new List<MarkRow>();

        private Border watchDot;
        private TextBlock watchState;
        private TextBlock watchHint;
        private TextBlock watchRowInterval;
        private TextBlock watchRowScan;
        private Button btnStart;
        private Button btnStop;
        private ListBox rootsList;
        private TextBlock rootsEmpty;
        private TextBox rootInput;
        private TextBlock logText;
        private ScrollViewer logScroll;

        // 设置页
        private CheckBox tgAutoStart;
        private CheckBox tgAutoFollow;
        private CheckBox tgAutoUpdate;
        private ComboBox cbWatch;
        private ComboBox cbScan;
        private ComboBox cbTheme;
        private TextBox rulesBox;

        // 关于页
        private TextBlock aboutStats;

        // 清单表头（文件夹/颜色/缓存图标/目录），切页时逐列淡入
        private Grid tableHeader;

        private readonly DispatcherTimer timer;
        private bool busy;

        // 列表行数据
        internal sealed class MarkRow
        {
            public string Path { get; set; }
            /// <summary>行内显示用：只放文件夹名（完整路径在抽屉组头里）</summary>
            public string Name { get; set; }
            public string ColorTitle { get; set; }
            public string Cache { get; set; }
            public string CacheBrush { get; set; }
            public string Dir { get; set; }
            /// <summary>分组用：所在目录（清单按目录收纳成抽屉）</summary>
            public string GroupDir { get; set; }
            public Brush Dot { get; set; }
            /// <summary>用来在详情条里预览的 ico 路径（缓存优先，否则内置纯色）</summary>
            public string IconPath { get; set; }
        }

        public ManagerWindow()
        {
            Title = "文件夹标记";
            MinWidth = 900;
            MinHeight = 600;

            int w = GetSettingInt("WindowWidth", 1040);
            int h = GetSettingInt("WindowHeight", 720);
            Width = Math.Max(MinWidth, Math.Min(4000, w));
            Height = Math.Max(MinHeight, Math.Min(4000, h));

            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // 自绘标题栏：WindowStyle=None 去掉系统标题栏，WindowChrome 负责缩放与拖拽
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            Background = WinUi.ShellFill;
            AllowsTransparency = false;

            WindowChrome chrome = new WindowChrome();
            chrome.CaptionHeight = TitleBarHeight;
            chrome.ResizeBorderThickness = new Thickness(6);
            // 0 = 不要玻璃层。设 -1 虽然能让 Mica 透出来，但会给窗口加一圈 1px 灰边、
            // 并且让 Win11 圆角失效；纯色底 + 白卡片是更干净的形态。
            chrome.GlassFrameThickness = new Thickness(0);
            chrome.CornerRadius = new CornerRadius(0);
            chrome.UseAeroCaptionButtons = false;
            WindowChrome.SetWindowChrome(this, chrome);

            FontFamily = new FontFamily(WinUi.FontUi);
            FontSize = WinUi.SizeBody;
            UseLayoutRounding = true;
            // Display 模式按整数像素对齐字形，比 Ideal 快很多（大量文本/滚动时尤其明显），
            // 代价是高 DPI 下字距略有差异，UI 文本完全可接受。
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

            if (WinUi.Res.Count > 0) { Resources.MergedDictionaries.Add(WinUi.Res); }
            try { Icon = LoadAppIcon(); }
            catch { }

            shell = new Border();
            shell.Background = WinUi.B("LayerFill");

            Grid rootGrid = new Grid();
            RowDefinition r0 = new RowDefinition();
            r0.Height = new GridLength(TitleBarHeight);
            rootGrid.RowDefinitions.Add(r0);
            RowDefinition r1 = new RowDefinition();
            r1.Height = new GridLength(1, GridUnitType.Star);
            rootGrid.RowDefinitions.Add(r1);

            UIElement titleBar = BuildTitleBar();
            Grid.SetRow(titleBar, 0);
            rootGrid.Children.Add(titleBar);

            Grid body = new Grid();
            ColumnDefinition c0 = new ColumnDefinition();
            c0.Width = new GridLength(NavWidth);
            body.ColumnDefinitions.Add(c0);
            ColumnDefinition c1 = new ColumnDefinition();
            c1.Width = new GridLength(1, GridUnitType.Star);
            body.ColumnDefinitions.Add(c1);

            UIElement nav = BuildNav();
            Grid.SetColumn(nav, 0);
            body.Children.Add(nav);

            UIElement content = BuildContent();
            Grid.SetColumn(content, 1);
            body.Children.Add(content);

            Grid.SetRow(body, 1);
            rootGrid.Children.Add(body);

            // 忙碌指示条（WinUI 顶部细进度条）
            busyBar = new Border();
            busyBar.Height = 2;
            busyBar.Background = WinUi.B("AccentFill");
            busyBar.VerticalAlignment = VerticalAlignment.Top;
            busyBar.HorizontalAlignment = HorizontalAlignment.Stretch;
            busyBar.Visibility = Visibility.Collapsed;
            busyBar.Opacity = 0.35;
            Grid.SetRow(busyBar, 1);
            rootGrid.Children.Add(busyBar);

            shell.Child = rootGrid;
            Content = shell;

            SourceInitialized += delegate
            {
                WinUi.EnableMica(this);
                if (shell != null) { shell.Background = WinUi.ShellFill; }
                UpdateMaxGlyph();

                // WindowChrome 最大化时会把窗口往四周各撑 7px（为了显示阴影），
                // 结果是标题栏顶部、左侧导航、底部按钮都被裁掉一条。自己接管
                // WM_GETMINMAXINFO，把最大化尺寸限制到工作区。
                HwndSource src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                if (src != null) { src.AddHook(WndProc); }
            };
            StateChanged += delegate { UpdateMaxGlyph(); };

            SelectNavPages(0);

            // 把文件夹拖进窗口就能标色（用底部颜色下拉框当前选中的颜色）
            AllowDrop = true;
            DragOver += delegate(object sender, DragEventArgs e)
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                    ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };
            Drop += delegate(object sender, DragEventArgs e) { OnFolderDrop(e); };

            // 记住窗口大小（不保存最大化状态，下次照样按普通大小开）
            Closing += delegate
            {
                try
                {
                    if (WindowState == WindowState.Normal)
                    {
                        SetSetting("WindowWidth", ((int)Math.Round(Width)).ToString());
                        SetSetting("WindowHeight", ((int)Math.Round(Height)).ToString());
                    }
                }
                catch { }
            };

            // 快捷键：Ctrl+F 聚焦搜索，F5 刷新
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                if (ctrl && e.Key == Key.F && searchBox != null)
                {
                    SelectNavPages(0);
                    searchBox.Focus();
                    searchBox.SelectAll();
                    e.Handled = true;
                }
                else if (e.Key == Key.F5)
                {
                    ReloadMarks();
                    ReloadLog();
                    ReloadRoots();
                    UpdateWatchStatus();
                    e.Handled = true;
                }
            };

            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(3);
            timer.Tick += delegate
            {
                if (pages[1].Visibility == Visibility.Visible) { UpdateWatchStatus(); }
            };
            timer.Start();

            // 窗口打开动效：轻微放大淡入
            Loaded += delegate { WinMotion.WindowOpen(this); };

            // 全窗口平滑滚动（列表/日志/设置页等所有滚动区域自动生效）
            SmoothScroll.Enable(this);

            // 启动自动检查更新：开相关设置且在 UI 空闲时才跑，失败静默
            if (Program.GetSettingInt("AutoUpdate", 1) != 0)
            {
                Dispatcher.BeginInvoke(new Action(delegate { AutoCheckUpdate(); }),
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }

        /// <summary>后台检查新版本，发现新版弹窗询问是否更新（手动/自动都走这里）</summary>
        private void AutoCheckUpdate()
        {
            BackgroundWorkerCheck(delegate(UpdateInfo info, string err)
            {
                if (err != null) { return; }   // 自动检查失败不打扰用户
                if (!IsNewerVersion(info.Version, AppVersion)) { return; }
                string msg = "发现新版本 " + info.Version + "\r\n当前版本 " + AppVersion +
                    "\r\n\r\n是否现在下载并更新？更新包从 GitHub 下载，约 3 MB。";
                if (WinUiDialog.Confirm(this, "有可用更新", msg, "更新"))
                {
                    DoUpdate(info);
                }
            }, false);
        }

        /// <summary>手动检查：无论有没有新版都给结果反馈</summary>
        private void ManualCheckUpdate()
        {
            BackgroundWorkerCheck(delegate(UpdateInfo info, string err)
            {
                if (err != null)
                {
                    WinUiDialog.Info(this, "检查失败", err + "\r\n\r\n请检查网络后重试。");
                    return;
                }
                if (!IsNewerVersion(info.Version, AppVersion))
                {
                    WinUiDialog.Info(this, "已是最新", "当前版本 " + AppVersion + " 已是最新版本。");
                    return;
                }
                string msg = "发现新版本 " + info.Version + "\r\n当前版本 " + AppVersion +
                    "\r\n\r\n是否现在下载并更新？更新包从 GitHub 下载，约 3 MB。";
                if (WinUiDialog.Confirm(this, "有可用更新", msg, "更新"))
                {
                    DoUpdate(info);
                }
            }, true);
        }

        /// <summary>后台线程跑网络检查，完成后回到 UI 线程回调</summary>
        private void BackgroundWorkerCheck(Action<UpdateInfo, string> done, bool manual)
        {
            System.Threading.Thread t = new System.Threading.Thread(delegate()
            {
                UpdateInfo info = CheckLatestVersion();
                string err = info.Ok ? null : info.Error;
                Dispatcher.BeginInvoke(new Action(delegate { done(info, err); }));
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>后台下载 + 静默安装，带实时进度窗口</summary>
        private void DoUpdate(UpdateInfo info)
        {
            UpdateProgressWindow prog = null;
            try { prog = new UpdateProgressWindow(this, info.Version); prog.Show(); }
            catch { prog = null; }

            System.Threading.Thread t = new System.Threading.Thread(delegate()
            {
                string err = DownloadAndInstall(info, delegate(int pct, string status)
                {
                    if (prog != null) { prog.SetProgress(pct, status); }
                });
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (prog != null) { try { prog.Close(); } catch { } }
                    if (err != null)
                    {
                        WinUiDialog.Info(this, "更新失败", err + "\r\n\r\n你可以到 GitHub Releases 手动下载安装包。");
                    }
                    // 成功时安装器会接管（静默安装会替换 exe 并重启托盘）
                }));
            });
            t.IsBackground = true;
            t.Start();
        }

        private void UpdateMaxGlyph()
        {
            if (maxGlyph == null) { return; }
            maxGlyph.Text = WindowState == WindowState.Maximized
                ? WinUi.GlyphRestore : WinUi.GlyphMaximize;
        }

        // ---------------- 最大化边界修正 ----------------
        private const int WM_GETMINMAXINFO = 0x0024;
        private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT32
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT32 rcMonitor;
            public RECT32 rcWork;
            public int dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_GETMINMAXINFO)
            {
                try
                {
                    MONITORINFO mi = new MONITORINFO();
                    mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                    if (GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref mi))
                    {
                        MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));
                        mmi.ptMaxPosition.X = mi.rcWork.Left - mi.rcMonitor.Left;
                        mmi.ptMaxPosition.Y = mi.rcWork.Top - mi.rcMonitor.Top;
                        mmi.ptMaxSize.X = mi.rcWork.Right - mi.rcWork.Left;
                        mmi.ptMaxSize.Y = mi.rcWork.Bottom - mi.rcWork.Top;
                        mmi.ptMaxTrackSize.X = mmi.ptMaxSize.X;
                        mmi.ptMaxTrackSize.Y = mmi.ptMaxSize.Y;
                        Marshal.StructureToPtr(mmi, lParam, true);
                        handled = true;
                    }
                }
                catch { }
            }
            return IntPtr.Zero;
        }

        private static ImageSource LoadAppIcon()
        {
            string p = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icons", "app.ico");
            if (!File.Exists(p)) { return null; }
            // 必须取最大帧：BitmapImage 默认只读第一帧（16px），放大到 56 就是糊的
            return WinUi.LoadIcoLargest(p);
        }

        // =================================================================
        //  自绘标题栏
        // =================================================================
        private UIElement BuildTitleBar()
        {
            Grid g = new Grid();
            g.Background = Brushes.Transparent;

            ColumnDefinition c0 = new ColumnDefinition();
            c0.Width = new GridLength(1, GridUnitType.Star);
            g.ColumnDefinitions.Add(c0);
            ColumnDefinition c1 = new ColumnDefinition();
            c1.Width = GridLength.Auto;
            g.ColumnDefinitions.Add(c1);

            StackPanel left = new StackPanel();
            left.Orientation = Orientation.Horizontal;
            left.VerticalAlignment = VerticalAlignment.Center;
            left.Margin = new Thickness(16, 0, 0, 0);
            AddTitleIcon(left);

            TextBlock name = WinUi.Text("文件夹标记", WinUi.SizeCaption, WinUi.B("TextSecondary"));
            name.Margin = new Thickness(10, 0, 0, 0);
            left.Children.Add(name);
            g.Children.Add(left);

            // 窗口按钮：用 Grid 三列固定 46px 定位，比 StackPanel 更不易受 Auto 宽度计算影响
            Grid caps = new Grid();
            caps.HorizontalAlignment = HorizontalAlignment.Right;
            caps.VerticalAlignment = VerticalAlignment.Top;
            AddFixed(caps, 46);
            AddFixed(caps, 46);
            AddFixed(caps, 46);

            Border bMin = CaptionButton(WinUi.GlyphMinimize, false,
                delegate { WindowState = WindowState.Minimized; });
            Grid.SetColumn(bMin, 0);
            caps.Children.Add(bMin);

            maxGlyph = WinUi.Glyph(WinUi.GlyphMaximize, 10, WinUi.B("TextPrimary"));
            Border bMax = CaptionHost(maxGlyph, false,
                delegate
                {
                    WindowState = WindowState == WindowState.Maximized
                        ? WindowState.Normal : WindowState.Maximized;
                });
            Grid.SetColumn(bMax, 1);
            caps.Children.Add(bMax);

            Border bClose = CaptionButton(WinUi.GlyphClose, true,
                delegate { Close(); });
            Grid.SetColumn(bClose, 2);
            caps.Children.Add(bClose);

            Grid.SetColumn(caps, 1);
            g.Children.Add(caps);

            return g;
        }

        private void AddTitleIcon(Panel host)
        {
            try
            {
                ImageSource src = LoadAppIcon();
                if (src == null) { return; }
                Image img = new Image();
                img.Source = src;
                img.Width = 16;
                img.Height = 16;
                img.VerticalAlignment = VerticalAlignment.Center;
                host.Children.Add(img);
            }
            catch { }
        }

        private Border CaptionButton(string glyph, bool close, Action onClick)
        {
            return CaptionHost(WinUi.Glyph(glyph, 10, WinUi.B("TextPrimary")), close, onClick);
        }

        private Border CaptionHost(TextBlock glyph, bool close, Action onClick)
        {
            Border b = new Border();
            b.Width = 46;
            b.Height = 32;
            b.Background = Brushes.Transparent;
            b.Child = glyph;
            WindowChrome.SetIsHitTestVisibleInChrome(b, true);

            Brush closeHover = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
            Brush closePressed = new SolidColorBrush(Color.FromRgb(0xB2, 0x2B, 0x1E));

            b.MouseEnter += delegate
            {
                b.Background = close ? closeHover : WinUi.B("CaptionHover");
                if (close) { glyph.Foreground = Brushes.White; }
            };
            b.MouseLeave += delegate
            {
                b.Background = Brushes.Transparent;
                glyph.Foreground = WinUi.B("TextPrimary");
            };
            b.MouseLeftButtonDown += delegate
            {
                if (close) { b.Background = closePressed; }
            };
            b.MouseLeftButtonUp += delegate
            {
                if (onClick != null) { onClick(); }
            };
            return b;
        }

        // =================================================================
        //  左侧导航（NavigationView）
        // =================================================================
        private UIElement BuildNav()
        {
            // 上面是功能页，最后一项（关于）固定在左下角 —— Win11 应用的习惯做法
            Grid root = new Grid();
            root.Margin = new Thickness(8, 4, 8, 8);

            RowDefinition r0 = new RowDefinition();
            r0.Height = new GridLength(1, GridUnitType.Star);
            root.RowDefinitions.Add(r0);
            RowDefinition r1 = new RowDefinition();
            r1.Height = GridLength.Auto;
            root.RowDefinitions.Add(r1);

            StackPanel top = new StackPanel();
            top.Background = Brushes.Transparent;
            for (int i = 0; i < Titles.Length - 1; i++)
            {
                top.Children.Add(NavItem(i, Titles[i], NavGlyphs[i]));
            }
            Grid.SetRow(top, 0);
            root.Children.Add(top);

            int last = Titles.Length - 1;
            StackPanel bottom = new StackPanel();
            bottom.Background = Brushes.Transparent;
            bottom.Margin = new Thickness(0, 6, 0, 0);
            bottom.Children.Add(NavItem(last, Titles[last], NavGlyphs[last]));
            Grid.SetRow(bottom, 1);
            root.Children.Add(bottom);

            return root;
        }

        private Border NavItem(int index, string text, string glyph)
        {
            Grid g = new Grid();
            g.Height = 40;

            Border bg = new Border();
            bg.CornerRadius = new CornerRadius(4);
            bg.Background = Brushes.Transparent;
            g.Children.Add(bg);

            // hover 底色单独一层，用透明度做过渡（直接改 bg.Background 是硬切）
            Border hv = new Border();
            hv.CornerRadius = new CornerRadius(4);
            hv.Background = WinUi.B("RowHover");
            hv.Opacity = 0;
            hv.IsHitTestVisible = false;
            g.Children.Add(hv);
            navHovers.Add(hv);

            Border bar = new Border();
            bar.Width = 3;
            bar.Height = 16;
            bar.CornerRadius = new CornerRadius(1.5);
            bar.Background = WinUi.B("AccentFill");
            bar.HorizontalAlignment = HorizontalAlignment.Left;
            bar.VerticalAlignment = VerticalAlignment.Center;
            bar.Opacity = 0;
            g.Children.Add(bar);
            navBars.Add(bar);

            TextBlock icon = WinUi.Glyph(glyph, 15, WinUi.B("TextSecondary"));
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            icon.Margin = new Thickness(13, 0, 0, 0);
            g.Children.Add(icon);
            navIcons.Add(icon);

            TextBlock t = WinUi.Text(text, WinUi.SizeBody, WinUi.B("TextPrimary"));
            t.Margin = new Thickness(40, 0, 0, 0);
            g.Children.Add(t);
            navTexts.Add(t);

            Border item = new Border();
            item.CornerRadius = new CornerRadius(4);
            item.Background = Brushes.Transparent;
            item.Margin = new Thickness(0, 1, 0, 1);
            item.Cursor = Cursors.Hand;
            item.Child = g;
            item.Tag = bg;

            item.MouseEnter += delegate
            {
                // 选中项不加 hover 底色（否则和选中色叠在一起发脏）
                if (navBars[index].Opacity < 1) { AnimateOpacity(hv, 1, 120); }
            };
            item.MouseLeave += delegate
            {
                AnimateOpacity(hv, 0, 180);
            };
            item.MouseLeftButtonUp += delegate { SelectNavPages(index); };

            navItems.Add(item);
            return item;
        }

        private void SelectNavPages(int index)
        {
            SelectNav(index);
            if (index == 0) { ReloadMarks(); AnimateVisibleRows(); }
            else if (index == 1) { UpdateWatchStatus(); }
            else if (index == 2) { ReloadRoots(); }
            else if (index == 3) { ReloadLog(); }
            else if (index == 4) { LoadSettings(); }
            else { ReloadAbout(); }
        }

        private void SelectNav(int index)
        {
            for (int k = 0; k < navItems.Count; k++)
            {
                bool on = (k == index);
                Border bg = navItems[k].Tag as Border;
                if (bg != null) { bg.Background = on ? WinUi.B("NavSelected") : Brushes.Transparent; }
                // 导航指示条：淡入淡出过渡，不再是硬切换
                AnimateOpacity(navBars[k], on ? 1 : 0, 160);
                // 选中项清掉残留 hover 底色，避免和选中色叠加发脏
                if (on && k < navHovers.Count) { AnimateOpacity(navHovers[k], 0, 120); }
                navIcons[k].Foreground = on ? WinUi.B("TextPrimary") : WinUi.B("TextSecondary");
                navTexts[k].Foreground = on ? WinUi.B("TextPrimary") : WinUi.B("TextSecondary");
                navTexts[k].FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
                pages[k].Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            }
            // 当前页内容：淡入 + 轻微上移，给切换一点节奏感（CubicEase 收尾柔和）
            WinMotion.SlideFadeIn(pages[index], 12, 200);
            WinMotion.FadeIn(pageTitle, 180);
            pageTitle.Text = Titles[index];
            pageSub.Text = Subs[index];
        }

        /// <summary>
        /// 清单可见行做「逐条滑入」：切到清单页 / 刷新时，行依次淡入并轻微上移。
        /// 只处理已实例化的行（虚拟化下约十几行）；基础值先设成最终值，
        /// 动画只负责过渡 —— 这样回收容器被复用时不会残留「半透明/偏移」的错误状态。
        /// </summary>
        private void AnimateVisibleRows()
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                try
                {
                    // 表头先逐列淡入（4 个列标题依次出现），行再跟上
                    if (tableHeader != null)
                    {
                        for (int c = 0; c < tableHeader.Children.Count; c++)
                        {
                            UIElement cell = tableHeader.Children[c] as UIElement;
                            if (cell == null) { continue; }
                            System.Windows.Media.TranslateTransform ct =
                                new System.Windows.Media.TranslateTransform(0, 0);
                            cell.RenderTransform = ct;
                            cell.Opacity = 1;

                            System.TimeSpan cd = System.TimeSpan.FromMilliseconds(c * 45);
                            DoubleAnimation cf = new DoubleAnimation(0, 1,
                                System.TimeSpan.FromMilliseconds(160));
                            cf.BeginTime = cd;
                            cf.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                            cell.BeginAnimation(UIElement.OpacityProperty, cf);

                            DoubleAnimation cm = new DoubleAnimation(6, 0,
                                System.TimeSpan.FromMilliseconds(180));
                            cm.BeginTime = cd;
                            cm.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                            ct.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, cm);
                        }
                    }

                    List<System.Windows.Controls.ListViewItem> rows =
                        new List<System.Windows.Controls.ListViewItem>();
                    CollectRows(list, rows, 18);
                    int n = rows.Count;
                    for (int i = 0; i < n; i++)
                    {
                        System.Windows.Controls.ListViewItem it = rows[i];
                        System.Windows.Media.TranslateTransform tt =
                            new System.Windows.Media.TranslateTransform(0, 0);
                        it.RenderTransform = tt;
                        it.Opacity = 1;   // 基础值 = 最终值

                        // 表头先铺开（约 180ms），行再逐条跟上
                        System.TimeSpan delay = System.TimeSpan.FromMilliseconds(170 + i * 22);

                        DoubleAnimation fade = new DoubleAnimation(0, 1,
                            System.TimeSpan.FromMilliseconds(170));
                        fade.BeginTime = delay;
                        fade.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                        it.BeginAnimation(UIElement.OpacityProperty, fade);

                        DoubleAnimation move = new DoubleAnimation(9, 0,
                            System.TimeSpan.FromMilliseconds(190));
                        move.BeginTime = delay;
                        move.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                        tt.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, move);
                    }
                }
                catch { }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>找到列表内部的 ScrollViewer（诊断用）</summary>
        private static ScrollViewer FindInnerScroll(DependencyObject root)
        {
            if (root == null) { return null; }
            try
            {
                int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
                for (int i = 0; i < count; i++)
                {
                    DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                    ScrollViewer sv = child as ScrollViewer;
                    if (sv != null) { return sv; }
                    ScrollViewer deep = FindInnerScroll(child);
                    if (deep != null) { return deep; }
                }
            }
            catch { }
            return null;
        }

        /// <summary>按视觉顺序收集已实例化的行容器（虚拟化下只会有可见的那几行）</summary>
        private static void CollectRows(DependencyObject root,
            List<System.Windows.Controls.ListViewItem> into, int max)
        {
            if (root == null || into.Count >= max) { return; }
            try
            {
                int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
                for (int i = 0; i < count && into.Count < max; i++)
                {
                    DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                    System.Windows.Controls.ListViewItem item =
                        child as System.Windows.Controls.ListViewItem;
                    if (item != null) { into.Add(item); }
                    CollectRows(child, into, max);
                }
            }
            catch { }
        }

        /// <summary>诊断用：清单条目数 vs 实际实例化的行容器数（验证虚拟化是否生效）</summary>
        internal string ProbeListStats()
        {
            try
            {
                int total = list.Items.Count;
                int realized = CountVisuals(list, typeof(System.Windows.Controls.ListViewItem));
                bool ok = total > 12 && realized > 0 && realized < total / 2;

                // 像素滚动验证：ScrollableHeight 若远大于条目数，说明按像素而非按项
                string unit = "?";
                try
                {
                    ScrollUnit su = VirtualizingPanel.GetScrollUnit(list);
                    ScrollViewer inner = FindInnerScroll(list);
                    unit = su.ToString() +
                        (inner != null
                            ? "，可滚 " + Math.Round(inner.ScrollableHeight) + "px，视口 "
                              + Math.Round(inner.ViewportHeight) + "px"
                            : "，未找到内部 ScrollViewer");
                }
                catch { }

                return "总条目 " + total + "，已实例化行容器 " + realized +
                       (ok ? "  → 虚拟化生效" : (total <= 12 ? "  → 条目太少" : "  → 虚拟化未生效")) +
                       "；滚动单位 " + unit;
            }
            catch (Exception ex) { return "统计失败: " + ex.Message; }
        }

        /// <summary>把元素的 Opacity 平滑过渡到目标值</summary>
        private static void AnimateOpacity(UIElement el, double to, int ms)
        {
            if (el == null) { return; }
            try
            {
                DoubleAnimation a = new DoubleAnimation(to, System.TimeSpan.FromMilliseconds(ms));
                a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                el.BeginAnimation(UIElement.OpacityProperty, a);
            }
            catch { try { el.Opacity = to; } catch { } }
        }

        // =================================================================
        //  右侧内容区
        // =================================================================
        private UIElement BuildContent()
        {
            Grid right = new Grid();
            RowDefinition r0 = new RowDefinition();
            r0.Height = GridLength.Auto;
            right.RowDefinitions.Add(r0);
            RowDefinition r1 = new RowDefinition();
            r1.Height = new GridLength(1, GridUnitType.Star);
            right.RowDefinitions.Add(r1);

            StackPanel head = new StackPanel();
            head.Margin = new Thickness(ContentPadX, 6, ContentPadX, 0);

            pageTitle.FontFamily = new FontFamily(WinUi.FontDisplay);
            pageTitle.FontSize = WinUi.SizeTitle;
            pageTitle.FontWeight = FontWeights.SemiBold;
            pageTitle.Foreground = WinUi.B("TextPrimary");
            pageTitle.Text = Titles[0];
            head.Children.Add(pageTitle);

            pageSub.FontSize = WinUi.SizeSubtitle;
            pageSub.Foreground = WinUi.B("TextSecondary");
            pageSub.Margin = new Thickness(0, 4, 0, 18);
            pageSub.Text = Subs[0];
            head.Children.Add(pageSub);

            Grid.SetRow(head, 0);
            right.Children.Add(head);

            Grid host = new Grid();
            host.Margin = new Thickness(ContentPadX, 0, ContentPadX, 24);
            pages.Add(BuildMarksPage());                 // 自带虚拟化列表，不套滚动
            pages.Add(WrapScroll(BuildWatchPage()));
            pages.Add(WrapScroll(BuildRootsPage()));
            pages.Add(BuildLogPage());                   // 内部已有 ScrollViewer
            pages.Add(WrapScroll(BuildSettingsPage()));
            pages.Add(WrapScroll(BuildAboutPage()));
            foreach (Panel p in pages)
            {
                p.Visibility = Visibility.Collapsed;
                host.Children.Add(p);
            }
            pages[0].Visibility = Visibility.Visible;
            Grid.SetRow(host, 1);
            right.Children.Add(host);

            return right;
        }

        /// <summary>
        /// 把内容页包进 ScrollViewer。静态内容页用的是 StackPanel，窗口一矮就会
        /// 直接被裁掉（滚轮也没反应）—— 设置页加了规则编辑器之后尤其明显。
        /// 列表页（自带虚拟化）和日志页（内部已有 ScrollViewer）不套这一层。
        /// </summary>
        private Panel WrapScroll(Panel inner)
        {
            ScrollViewer sv = new ScrollViewer();
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Style st = WinUi.S("WinScrollViewer");
            if (st != null) { sv.Style = st; }
            sv.Padding = new Thickness(0, 0, 0, 0);
            sv.Content = inner;

            Grid host = new Grid();
            host.Children.Add(sv);
            return host;
        }

        // ---------------- 第 1 页：标记清单 ----------------
        private Panel BuildMarksPage()
        {
            Grid page = new Grid();
            RowDefinition r0 = new RowDefinition();
            r0.Height = GridLength.Auto;                       // 工具条
            page.RowDefinitions.Add(r0);
            RowDefinition r1 = new RowDefinition();
            r1.Height = new GridLength(1, GridUnitType.Star);  // 列表卡片
            page.RowDefinitions.Add(r1);
            RowDefinition r2 = new RowDefinition();
            r2.Height = GridLength.Auto;                       // 详情条
            page.RowDefinitions.Add(r2);
            RowDefinition r3 = new RowDefinition();
            r3.Height = GridLength.Auto;                       // 底栏
            page.RowDefinitions.Add(r3);

            UIElement toolbar = BuildMarksToolbar();
            Grid.SetRow(toolbar, 0);
            page.Children.Add(toolbar);

            // 卡片：表头 + 分隔线 + 列表（空状态叠在列表上）
            Grid cardInner = new Grid();
            RowDefinition h0 = new RowDefinition();
            h0.Height = new GridLength(40);
            cardInner.RowDefinitions.Add(h0);
            RowDefinition h1 = new RowDefinition();
            h1.Height = GridLength.Auto;
            cardInner.RowDefinitions.Add(h1);
            RowDefinition h2 = new RowDefinition();
            h2.Height = new GridLength(1, GridUnitType.Star);
            cardInner.RowDefinitions.Add(h2);

            Grid header = BuildTableHeader();
            Grid.SetRow(header, 0);
            cardInner.Children.Add(header);

            Border line = new Border();
            line.Height = 1;
            line.Background = WinUi.B("DividerStroke");
            line.Margin = new Thickness(16, 0, 16, 0);
            Grid.SetRow(line, 1);
            cardInner.Children.Add(line);

            list = new ListView();
            list.SelectionMode = SelectionMode.Extended;
            Style lvStyle = WinUi.S("WinListView");
            if (lvStyle != null) { list.Style = lvStyle; }
            // 开启回收式虚拟化：清单几百项时只实例化可见行，滚动才不卡。
            // 分组场景必须显式打开 IsVirtualizingWhenGrouping，否则组内行不实例化（白屏）。
            // Recycling 模式复用容器，比 Standard 少大量 GC 与模板展开。
            VirtualizingPanel.SetIsVirtualizing(list, true);
            VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
            VirtualizingPanel.SetIsVirtualizingWhenGrouping(list, true);
            VirtualizingPanel.SetCacheLength(list, new VirtualizationCacheLength(1, 1));
            VirtualizingPanel.SetCacheLengthUnit(list, VirtualizationCacheLengthUnit.Page);
            ScrollViewer.SetCanContentScroll(list, true);
            // 像素级滚动：既保留虚拟化（CanContentScroll=true），又让滚动是连续的像素移动。
            // 默认的「按项」滚动是离散的，配平滑动画会一顿一顿。
            VirtualizingPanel.SetScrollUnit(list, ScrollUnit.Pixel);
            list.ItemTemplate = BuildRowTemplate();
            GroupStyle gs = BuildGroupStyle();
            if (gs != null) { list.GroupStyle.Add(gs); }
            list.SelectionChanged += delegate { UpdateDetail(); };
            // 双击一行 → 在资源管理器里打开这个文件夹
            list.MouseDoubleClick += delegate
            {
                MarkRow r = list.SelectedItem as MarkRow;
                if (r != null) { OpenPath(r.Path); }
            };
            list.Margin = new Thickness(0, 4, 0, 4);
            Grid.SetRow(list, 2);
            cardInner.Children.Add(list);

            emptyState = BuildEmptyState();
            emptyState.Visibility = Visibility.Collapsed;
            Grid.SetRow(emptyState, 2);
            cardInner.Children.Add(emptyState);
            System.Windows.Controls.Panel.SetZIndex(emptyState, 1);

            Border card = WinUi.Card(cardInner, new Thickness(0));
            Grid.SetRow(card, 1);
            page.Children.Add(card);

            UIElement detail = BuildDetailPanel();
            Grid.SetRow(detail, 2);
            page.Children.Add(detail);

            UIElement foot = BuildMarksFooter();
            Grid.SetRow(foot, 3);
            page.Children.Add(foot);

            return page;
        }

        // ---------------- 工具条：搜索 + 颜色筛选 + 清理失效 ----------------
        private UIElement BuildMarksToolbar()
        {
            Grid bar = new Grid();
            bar.Margin = new Thickness(2, 0, 2, 12);

            ColumnDefinition c0 = new ColumnDefinition();
            c0.Width = new GridLength(264);
            bar.ColumnDefinitions.Add(c0);
            ColumnDefinition c1 = new ColumnDefinition();
            c1.Width = GridLength.Auto;
            bar.ColumnDefinitions.Add(c1);
            ColumnDefinition c2 = new ColumnDefinition();
            c2.Width = new GridLength(1, GridUnitType.Star);
            bar.ColumnDefinitions.Add(c2);
            ColumnDefinition c3 = new ColumnDefinition();
            c3.Width = GridLength.Auto;
            bar.ColumnDefinitions.Add(c3);

            searchBox = new TextBox();
            Style tbStyle = WinUi.S("WinTextBox");
            if (tbStyle != null) { searchBox.Style = tbStyle; }
            searchBox.Height = 32;
            searchBox.Padding = new Thickness(32, 2, 10, 2);
            searchBox.VerticalContentAlignment = VerticalAlignment.Center;

            TextBlock mag = WinUi.Glyph(WinUi.GlyphSearch, 12.5, WinUi.B("TextTertiary"));
            mag.HorizontalAlignment = HorizontalAlignment.Left;
            mag.Margin = new Thickness(11, 0, 0, 0);

            TextBlock holder = new TextBlock();
            holder.Text = "搜索文件夹";
            holder.FontSize = 13;
            holder.Foreground = WinUi.B("TextTertiary");
            holder.VerticalAlignment = VerticalAlignment.Center;
            holder.Margin = new Thickness(32, 0, 0, 0);
            holder.IsHitTestVisible = false;

            searchBox.TextChanged += delegate
            {
                holder.Visibility = searchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                ApplyFilter();
            };

            Grid searchHost = new Grid();
            searchHost.Children.Add(searchBox);
            searchHost.Children.Add(mag);
            searchHost.Children.Add(holder);
            Grid.SetColumn(searchHost, 0);
            bar.Children.Add(searchHost);

            filterBox = new ComboBox();
            Style cbStyle = WinUi.S("WinComboBox");
            if (cbStyle != null) { filterBox.Style = cbStyle; }
            filterBox.Width = 120;
            filterBox.Margin = new Thickness(10, 0, 0, 0);
            filterBox.VerticalAlignment = VerticalAlignment.Center;
            filterBox.Items.Add("全部颜色");
            foreach (string t in ColorTitles) { filterBox.Items.Add(t); }
            filterBox.SelectedIndex = 0;
            filterBox.SelectionChanged += delegate { ApplyFilter(); };
            Grid.SetColumn(filterBox, 1);
            bar.Children.Add(filterBox);

            Button bClean = MakeButton("WinSubtleButton", "清理失效条目");
            bClean.MinWidth = 116;
            bClean.HorizontalAlignment = HorizontalAlignment.Right;
            bClean.VerticalAlignment = VerticalAlignment.Center;
            bClean.Click += delegate { CleanDeadEntries(); };
            Grid.SetColumn(bClean, 3);
            bar.Children.Add(bClean);

            return bar;
        }

        // ---------------- 详情条：选中一行时显示图标大图 ----------------
        private UIElement BuildDetailPanel()
        {
            Grid inner = new Grid();

            ColumnDefinition c0 = new ColumnDefinition();
            c0.Width = GridLength.Auto;
            inner.ColumnDefinitions.Add(c0);
            ColumnDefinition c1 = new ColumnDefinition();
            c1.Width = new GridLength(1, GridUnitType.Star);
            inner.ColumnDefinitions.Add(c1);
            ColumnDefinition c2 = new ColumnDefinition();
            c2.Width = GridLength.Auto;
            inner.ColumnDefinitions.Add(c2);

            Border iconBox = new Border();
            iconBox.Width = 64;
            iconBox.Height = 64;
            iconBox.CornerRadius = new CornerRadius(4);
            iconBox.Background = WinUi.B("SubtleHover");
            iconBox.VerticalAlignment = VerticalAlignment.Center;
            detailIconBox = iconBox;

            detailIcon = new Image();
            detailIcon.Stretch = Stretch.Uniform;
            detailIcon.Margin = new Thickness(4);
            iconBox.Child = detailIcon;
            Grid.SetColumn(iconBox, 0);
            inner.Children.Add(iconBox);

            StackPanel texts = new StackPanel();
            texts.VerticalAlignment = VerticalAlignment.Center;
            texts.Margin = new Thickness(16, 0, 16, 0);

            detailTitle = new TextBlock();
            detailTitle.FontSize = 13.5;
            detailTitle.Foreground = WinUi.B("TextPrimary");
            detailTitle.TextTrimming = TextTrimming.CharacterEllipsis;
            texts.Children.Add(detailTitle);

            detailMeta = new TextBlock();
            detailMeta.FontSize = WinUi.SizeCaption;
            detailMeta.Foreground = WinUi.B("TextSecondary");
            detailMeta.Margin = new Thickness(0, 4, 0, 0);
            texts.Children.Add(detailMeta);

            Grid.SetColumn(texts, 1);
            inner.Children.Add(texts);

            // 预览尺寸：检查图标在不同尺寸下清不清晰
            detailSizeBox = new ComboBox();
            Style szStyle = WinUi.S("WinComboBox");
            if (szStyle != null) { detailSizeBox.Style = szStyle; }
            detailSizeBox.Width = 96;
            detailSizeBox.VerticalAlignment = VerticalAlignment.Center;
            detailSizeBox.Items.Add("预览 64");
            detailSizeBox.Items.Add("预览 128");
            detailSizeBox.Items.Add("预览 256");
            detailSizeBox.SelectedIndex = 0;
            detailSizeBox.SelectionChanged += delegate { ApplyPreviewSize(); };
            Grid.SetColumn(detailSizeBox, 2);
            inner.Children.Add(detailSizeBox);

            detailPanel = WinUi.Card(inner, new Thickness(16, 12, 16, 12));
            detailPanel.Margin = new Thickness(0, 12, 0, 0);
            detailPanel.Visibility = Visibility.Collapsed;
            return detailPanel;
        }

        private void ApplyPreviewSize()
        {
            if (detailIconBox == null || detailSizeBox == null) { return; }

            double s = 64;
            if (detailSizeBox.SelectedIndex == 1) { s = 128; }
            else if (detailSizeBox.SelectedIndex == 2) { s = 256; }

            detailIconBox.Width = s;
            detailIconBox.Height = s;
        }

        private void UpdateDetail()
        {
            if (detailPanel == null) { return; }

            MarkRow r = null;
            if (list != null && list.SelectedItems != null && list.SelectedItems.Count > 0)
            {
                r = list.SelectedItems[0] as MarkRow;
            }

            if (r == null)
            {
                detailPanel.Visibility = Visibility.Collapsed;
                detailIcon.Source = null;
                return;
            }

            detailPanel.Visibility = Visibility.Visible;
            detailTitle.Text = r.Path;
            detailMeta.Text = r.ColorTitle + " · 缓存" + r.Cache + " · 目录" + r.Dir;
            detailIcon.Source = WinUi.LoadIcoLargest(r.IconPath);
        }

        // ---------------- 搜索 / 筛选 ----------------
        private void ApplyFilter()
        {
            if (list == null) { return; }

            string q = searchBox == null ? "" : searchBox.Text.Trim();
            int ci = filterBox == null ? 0 : filterBox.SelectedIndex;

            List<MarkRow> shown = new List<MarkRow>();
            foreach (MarkRow r in allRows)
            {
                if (q.Length > 0 &&
                    r.Path.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                if (ci > 0 && !string.Equals(r.ColorTitle, ColorTitles[ci - 1], StringComparison.Ordinal))
                {
                    continue;
                }
                shown.Add(r);
            }

            shown.Sort(delegate (MarkRow a, MarkRow b)
            {
                int c = string.Compare(a.GroupDir, b.GroupDir, StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
            });
            ListCollectionView view = new ListCollectionView(shown);
            view.GroupDescriptions.Add(new PropertyGroupDescription("GroupDir"));
            list.ItemsSource = view;
            list.UnselectAll();
            // 分组默认全部展开：模板里的 IsExpanded='True' 会被 ToggleButton 的双向绑定
            // 在个别分组上覆盖成 False，这里在布局完成后统一纠正一次。
            Dispatcher.BeginInvoke(new Action(delegate { ExpandAllGroups(); }),
                System.Windows.Threading.DispatcherPriority.Loaded);
            UpdateDetail();

            if (countText != null)
            {
                countText.Text = (shown.Count == allRows.Count)
                    ? "共 " + allRows.Count.ToString() + " 个"
                    : "筛选出 " + shown.Count.ToString() + " / 共 " + allRows.Count.ToString() + " 个";
            }

            if (emptyState != null)
            {
                emptyState.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            if (emptyTitle != null)
            {
                bool filtering = allRows.Count > 0;
                emptyTitle.Text = filtering ? "没有匹配的文件夹" : "还没有标记任何文件夹";
                emptyHint.Text = filtering
                    ? "换个关键词，或把颜色筛选改回「全部颜色」"
                    : "在资源管理器里右键文件夹 → 文件夹标记 → 选颜色";
            }
        }

        // ---------------- 清理失效条目 ----------------
        private void CleanDeadEntries()
        {
            List<string> dead = new List<string>();
            List<string> offline = new List<string>();
            Dictionary<string, bool> reach = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            foreach (string line in ReadMarked())
            {
                string f = Plain(line.Trim());
                if (f.Length == 0) { continue; }
                if (Directory.Exists(Io(f))) { continue; }

                // 共享盘断线 / NAS 休眠时 Directory.Exists 也返回 false。
                // 这种不能当「已被删除」，否则会把有效记录和缓存一起清掉。
                if (IsUncPath(f) && !PathReachable(f, reach))
                {
                    offline.Add(f);
                    continue;
                }

                dead.Add(f);
            }

            if (dead.Count == 0)
            {
                if (offline.Count > 0)
                {
                    WinUiDialog.Info(this, "暂时无法判断",
                        "有 " + offline.Count.ToString() + " 个文件夹当前访问不到（共享盘可能离线或休眠）。"
                        + "为了不误删，这次没有清理它们；等共享盘恢复后再试。");
                }
                else
                {
                    WinUiDialog.Info(this, "没有失效条目", "所有已标记的文件夹都还在。");
                }
                return;
            }

            string extra = offline.Count > 0
                ? "\r\n\r\n另有 " + offline.Count.ToString() + " 个当前访问不到（共享盘离线），这次不动它们。"
                : "";

            if (!WinUiDialog.Confirm(this, "清理失效条目",
                "有 " + dead.Count.ToString() + " 个已标记的文件夹找不到了（可能被删除或移动）。"
                + "清理后从列表移除它们，不影响其他文件夹。" + extra, "清理"))
            {
                return;
            }

            foreach (string f in dead)
            {
                try { RemoveMarked(f); } catch { }
                try { DeleteCacheIcons(f); } catch { }
            }

            ReloadMarks();
            WinUiDialog.Info(this, "已清理", "移除了 " + dead.Count.ToString() + " 个失效条目。");
        }

        private Grid BuildTableHeader()
        {
            Grid h = new Grid();
            h.Margin = new Thickness(16, 0, 16, 0);
            tableHeader = h;

            ColumnDefinition c0 = new ColumnDefinition();
            c0.Width = new GridLength(1, GridUnitType.Star);
            c0.MinWidth = 240;
            h.ColumnDefinitions.Add(c0);
            AddFixed(h, ColColor);
            AddFixed(h, ColCache);
            AddFixed(h, ColDir);

            h.Children.Add(HeaderCell("文件夹", 0));
            h.Children.Add(HeaderCell("颜色", 1));
            h.Children.Add(HeaderCell("缓存图标", 2));
            h.Children.Add(HeaderCell("目录", 3));
            return h;
        }

        private static void AddFixed(Grid g, double w)
        {
            ColumnDefinition c = new ColumnDefinition();
            c.Width = new GridLength(w);
            g.ColumnDefinitions.Add(c);
        }

        private static TextBlock HeaderCell(string text, int col)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = WinUi.SizeCaption;
            t.Foreground = WinUi.B("TextTertiary");
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(t, col);
            return t;
        }

        private DataTemplate BuildRowTemplate()
        {
            const string xaml = @"
<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
  <Grid Height='44' Margin='16,0,16,0'>
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width='*' MinWidth='240'/>
      <ColumnDefinition Width='138'/>
      <ColumnDefinition Width='132'/>
      <ColumnDefinition Width='84'/>
    </Grid.ColumnDefinitions>
    <TextBlock Grid.Column='0' Text='{Binding Name}' FontSize='13' Foreground='{DynamicResource TextPrimary}'
               TextTrimming='CharacterEllipsis' VerticalAlignment='Center' Margin='0,0,14,0'/>
    <StackPanel Grid.Column='1' Orientation='Horizontal' VerticalAlignment='Center'>
      <Ellipse Width='10' Height='10' Fill='{Binding Dot}' Margin='0,0,8,0' VerticalAlignment='Center'/>
      <TextBlock Text='{Binding ColorTitle}' FontSize='13' Foreground='{DynamicResource TextSecondary}' VerticalAlignment='Center'
                 TextTrimming='CharacterEllipsis'/>
    </StackPanel>
    <TextBlock Grid.Column='2' Text='{Binding Cache}' FontSize='13'
               Foreground='{Binding CacheBrush}' TextTrimming='CharacterEllipsis'
               VerticalAlignment='Center' Margin='0,0,12,0'/>
    <TextBlock Grid.Column='3' Text='{Binding Dir}' FontSize='13' Foreground='{DynamicResource TextTertiary}'
               VerticalAlignment='Center'/>
  </Grid>
</DataTemplate>";
            try
            {
                return (DataTemplate)XamlReader.Parse(xaml);
            }
            catch { return null; }
        }

        /// <summary>
        /// 清单的「目录抽屉」样式：每个目标目录一个可折叠组头，
        /// 组头 = 折叠箭头 + 文件夹图标 + 目录全路径 + 条目数徽标。
        /// </summary>
        private GroupStyle BuildGroupStyle()
        {
            const string xaml = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
       TargetType='{x:Type GroupItem}'>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='{x:Type GroupItem}'>
        <Expander IsExpanded='True'>
          <Expander.Template>
            <ControlTemplate TargetType='{x:Type Expander}'>
              <StackPanel>
                <ToggleButton x:Name='Head' Focusable='False' Cursor='Hand'
                              IsChecked='{Binding IsExpanded, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
                  <ToggleButton.Template>
                    <ControlTemplate TargetType='{x:Type ToggleButton}'>
                      <Border x:Name='Bd' Background='Transparent' CornerRadius='6'
                              Height='34' Margin='8,5,8,1' Padding='9,0,10,0'>
                        <Grid>
                          <Grid.ColumnDefinitions>
                            <ColumnDefinition Width='Auto'/>
                            <ColumnDefinition Width='Auto'/>
                            <ColumnDefinition Width='*'/>
                            <ColumnDefinition Width='Auto'/>
                          </Grid.ColumnDefinitions>
                          <TextBlock x:Name='ChevR' Grid.Column='0' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets'
                                     Text='&#xE76C;' FontSize='10' VerticalAlignment='Center'
                                     Foreground='{DynamicResource TextSecondary}'/>
                          <TextBlock x:Name='ChevD' Grid.Column='0' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets'
                                     Text='&#xE70D;' FontSize='10' VerticalAlignment='Center'
                                     Visibility='Collapsed' Foreground='{DynamicResource TextSecondary}'/>
                          <TextBlock Grid.Column='1' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' Text='&#xE838;'
                                     FontSize='14' Margin='9,0,8,0' VerticalAlignment='Center'
                                     Foreground='{DynamicResource TextSecondary}'/>
                          <TextBlock Grid.Column='2' Text='{Binding Name}' FontSize='12.5' FontWeight='SemiBold'
                                     VerticalAlignment='Center' TextTrimming='CharacterEllipsis'
                                     Foreground='{DynamicResource TextPrimary}'/>
                          <Border Grid.Column='3' Background='{DynamicResource ControlFill}'
                                  CornerRadius='9' Padding='8,2' Margin='10,0,0,0' VerticalAlignment='Center'>
                            <TextBlock Text='{Binding ItemCount}' FontSize='11'
                                       Foreground='{DynamicResource TextSecondary}'/>
                          </Border>
                        </Grid>
                      </Border>
                      <ControlTemplate.Triggers>
                        <Trigger Property='IsChecked' Value='True'>
                          <Setter TargetName='ChevR' Property='Visibility' Value='Collapsed'/>
                          <Setter TargetName='ChevD' Property='Visibility' Value='Visible'/>
                        </Trigger>
                        <Trigger Property='IsMouseOver' Value='True'>
                          <Setter TargetName='Bd' Property='Background' Value='{DynamicResource SubtleHover}'/>
                        </Trigger>
                      </ControlTemplate.Triggers>
                    </ControlTemplate>
                  </ToggleButton.Template>
                </ToggleButton>
                <ContentPresenter x:Name='ExpandSite' Visibility='Collapsed'/>
              </StackPanel>
              <ControlTemplate.Triggers>
                <Trigger Property='IsExpanded' Value='True'>
                  <Setter TargetName='ExpandSite' Property='Visibility' Value='Visible'/>
                </Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
          </Expander.Template>
          <ItemsPresenter/>
        </Expander>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";
            try
            {
                Style st = (Style)XamlReader.Parse(xaml);
                GroupStyle gs = new GroupStyle();
                gs.ContainerStyle = st;
                return gs;
            }
            catch (Exception ex)
            {
                DebugLog("group style: " + ex.Message);
                return null;
            }
        }

        /// <summary>把清单里所有目录抽屉展开（分组容器可能还没生成，遍历到的才设置）</summary>
        private void ExpandAllGroups()
        {
            if (list == null) { return; }
            ExpandExpanders(list);
        }

        private static void ExpandExpanders(DependencyObject node)
        {
            if (node == null) { return; }
            int n = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < n; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(node, i);
                Expander ex = child as Expander;
                if (ex != null && !ex.IsExpanded) { ex.IsExpanded = true; }
                ExpandExpanders(child);
            }
        }

        private StackPanel BuildEmptyState()
        {
            StackPanel sp = new StackPanel();
            sp.HorizontalAlignment = HorizontalAlignment.Center;
            sp.VerticalAlignment = VerticalAlignment.Center;

            TextBlock icon = WinUi.Glyph(WinUi.GlyphFolderOpen, 30, WinUi.B("TextTertiary"));
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.Margin = new Thickness(0, 0, 0, 10);
            sp.Children.Add(icon);

            emptyTitle = WinUi.Text("还没有标记任何文件夹", 15, WinUi.B("TextPrimary"));
            emptyTitle.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(emptyTitle);

            emptyHint = WinUi.Text("在资源管理器里右键文件夹 → 文件夹标记 → 选颜色", WinUi.SizeCaption,
                WinUi.B("TextTertiary"));
            emptyHint.HorizontalAlignment = HorizontalAlignment.Center;
            emptyHint.Margin = new Thickness(0, 6, 0, 0);
            sp.Children.Add(emptyHint);

            return sp;
        }

        private UIElement BuildMarksFooter()
        {
            Grid foot = new Grid();
            foot.Margin = new Thickness(2, 12, 2, 0);

            ColumnDefinition c0 = new ColumnDefinition();
            c0.Width = new GridLength(1, GridUnitType.Star);
            foot.ColumnDefinitions.Add(c0);
            ColumnDefinition c1 = new ColumnDefinition();
            c1.Width = GridLength.Auto;
            foot.ColumnDefinitions.Add(c1);

            countText = WinUi.Text("共 0 个", WinUi.SizeCaption, WinUi.B("TextSecondary"));
            countText.VerticalAlignment = VerticalAlignment.Center;
            foot.Children.Add(countText);

            StackPanel right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.HorizontalAlignment = HorizontalAlignment.Right;

            Button bAll = MakeButton("WinSubtleButton", "全选");
            bAll.Margin = new Thickness(0, 0, 4, 0);
            bAll.Click += delegate { list.SelectAll(); };
            right.Children.Add(bAll);

            Button bInv = MakeButton("WinSubtleButton", "清除选择");
            bInv.Margin = new Thickness(0, 0, 8, 0);
            bInv.Click += delegate { list.UnselectAll(); };
            right.Children.Add(bInv);

            Button bReload = MakeButton("WinSubtleButton", "刷新");
            bReload.Margin = new Thickness(0, 0, 10, 0);
            bReload.Click += delegate { ReloadMarks(); };
            right.Children.Add(bReload);

            colorBox = new ComboBox();
            colorBox.Width = 104;
            colorBox.VerticalAlignment = VerticalAlignment.Center;
            Style cbStyle = WinUi.S("WinComboBox");
            if (cbStyle != null) { colorBox.Style = cbStyle; }
            foreach (string t in ColorTitles) { colorBox.Items.Add(t); }
            colorBox.Items.Add("自定义…");   // 打开取色器，给批量选中项统一上自定义色
            colorBox.SelectedIndex = 0;
            colorBox.Margin = new Thickness(0, 0, 8, 0);
            right.Children.Add(colorBox);

            Button bApply = MakeButton("WinAccentButton", "应用到选中");
            bApply.MinWidth = 104;
            bApply.Click += delegate { Batch(false); };
            right.Children.Add(bApply);

            Button bClear = MakeButton("WinButton", "清除标记");
            bClear.Margin = new Thickness(8, 0, 0, 0);
            bClear.Foreground = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
            bClear.Click += delegate { Batch(true); };
            right.Children.Add(bClear);

            Grid.SetColumn(right, 1);
            foot.Children.Add(right);

            return foot;
        }

        private static Button MakeButton(string styleKey, string text)
        {
            Button b = WinUi.Btn(styleKey, text);
            b.MinWidth = 72;
            return b;
        }

        // ---------------- 数据加载 ----------------
        private void ReloadMarks()
        {
            if (list == null) { return; }

            allRows.Clear();
            foreach (string line in ReadMarked())
            {
                string f = Plain(line.Trim());
                if (f.Length == 0) { continue; }

                string ini = System.IO.Path.Combine(Io(f), "desktop.ini");
                string colorName, cache;
                string colorTitle = Describe(ini, out colorName, out cache);

                MarkRow r = new MarkRow();
                r.Path = f;
                try
                {
                    string trimmed = f.TrimEnd('\\');
                    r.Name = System.IO.Path.GetFileName(trimmed);
                    if (string.IsNullOrEmpty(r.Name)) { r.Name = trimmed; }
                }
                catch { r.Name = f; }
                r.ColorTitle = colorTitle;
                r.Cache = cache;
                r.CacheBrush = cache.StartsWith("缺失", StringComparison.Ordinal)
                    ? (WinUi.DarkMode ? "#FFFFB86B" : "#FF9A5B00")
                    : (WinUi.DarkMode ? "#8AFFFFFF" : "#72000000");
                r.Dir = Directory.Exists(Io(f)) ? "正常" : "已失效";
                try
                {
                    string parent = System.IO.Path.GetDirectoryName(f);
                    r.GroupDir = string.IsNullOrEmpty(parent) ? "（根目录）" : parent;
                }
                catch { r.GroupDir = "（其他）"; }
                r.Dot = DotBrush(colorName);

                // 详情条预览用：缓存 ico 优先，没有就退回安装包里的纯色 ico
                string iconName, colorIgnored;
                string iconPath = null;
                if (TryReadCacheMark(ini, out iconName, out colorIgnored))
                {
                    string cp = System.IO.Path.Combine(CacheIconDir(), iconName);
                    if (File.Exists(Io(cp))) { iconPath = cp; }
                }
                if (iconPath == null && !string.IsNullOrEmpty(colorName))
                {
                    iconPath = System.IO.Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory, "icons", colorName + ".ico");
                }
                r.IconPath = iconPath;

                allRows.Add(r);
            }

            ApplyFilter();
        }

        private static string Describe(string ini, out string colorName, out string cacheText)
        {
            colorName = null;
            cacheText = "—";

            string iconName, color;
            if (TryReadCacheMark(ini, out iconName, out color))
            {
                colorName = color;
                string p = System.IO.Path.Combine(CacheIconDir(), iconName);
                cacheText = File.Exists(Io(p)) ? "正常" : "缺失（会自动补）";
                return ColorTitle(color);
            }

            string raw = ReadIconResourceRaw(ini);
            if (string.IsNullOrEmpty(raw)) { return "(未标记)"; }

            for (int i = 0; i < ValidColors.Length; i++)
            {
                if (raw.IndexOf("icons\\" + ValidColors[i] + ".ico", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    raw.IndexOf("icons/" + ValidColors[i] + ".ico", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    colorName = ValidColors[i];
                    cacheText = "纯色（无需缓存）";
                    return ColorTitle(ValidColors[i]);
                }
            }
            return "(自定义图标)";
        }

        private static string ColorTitle(string color)
        {
            int i = Array.IndexOf(ValidColors, color);
            if (i >= 0 && i < ColorTitles.Length) { return ColorTitles[i]; }

            // c<RRGGBB> 自定义色：显示成「自定义 #F27900」，比一个「—」有用得多
            if (!string.IsNullOrEmpty(color) && color.Length == 7 && color[0] == 'c')
            {
                return "自定义 #" + color.Substring(1).ToUpperInvariant();
            }
            return "—";
        }

        private static Brush DotBrush(string color)
        {
            // 自定义色直接画成它本身的颜色
            if (!string.IsNullOrEmpty(color) && color.Length == 7 && color[0] == 'c')
            {
                int rgb;
                if (int.TryParse(color.Substring(1), System.Globalization.NumberStyles.HexNumber, null, out rgb))
                {
                    return new SolidColorBrush(Color.FromRgb(
                        (byte)((rgb >> 16) & 255), (byte)((rgb >> 8) & 255), (byte)(rgb & 255)));
                }
            }

            if (color == "red") { return new SolidColorBrush(Color.FromRgb(0xB0, 0x2A, 0x2A)); }
            if (color == "orange") { return new SolidColorBrush(Color.FromRgb(0xD1, 0x6B, 0x00)); }
            if (color == "yellow") { return new SolidColorBrush(Color.FromRgb(0xC9, 0xA2, 0x00)); }
            if (color == "green") { return new SolidColorBrush(Color.FromRgb(0x1E, 0x8E, 0x3E)); }
            if (color == "blue") { return new SolidColorBrush(Color.FromRgb(0x2A, 0x53, 0x8B)); }
            if (color == "purple") { return new SolidColorBrush(Color.FromRgb(0x6B, 0x3F, 0xA0)); }
            if (color == "gray") { return new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)); }
            return new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));
        }

        private void Batch(bool clear)
        {
            if (busy) { return; }

            List<string> paths = new List<string>();
            if (list.SelectedItems != null)
            {
                foreach (object o in list.SelectedItems)
                {
                    MarkRow r = o as MarkRow;
                    if (r != null) { paths.Add(r.Path); }
                }
            }
            if (paths.Count == 0)
            {
                WinUiDialog.Info(this, "还没选文件夹",
                    "先在列表里选中要处理的文件夹（可按住 Ctrl 多选，或点「全选」）。");
                return;
            }
            if (clear && !WinUiDialog.Confirm(this, "清除标记",
                "将清除选中的 " + paths.Count.ToString() + " 个文件夹的标记，恢复成原来的图标。", "清除"))
            {
                return;
            }

            string color = "none";
            if (!clear)
            {
                if (colorBox.SelectedIndex == ColorTitles.Length)
                {
                    // 「自定义…」：用和右键菜单同一个取色器
                    string token = ColorPickerWindow.Pick(paths[0]);
                    if (string.IsNullOrEmpty(token)) { return; }
                    color = token;
                }
                else
                {
                    color = ValidColors[Math.Max(0, colorBox.SelectedIndex)];
                }
            }

            ApplyTo(paths, color, clear);
        }

        /// <summary>对一批文件夹执行标记/清除（批量按钮和拖放共用）</summary>
        private void ApplyTo(List<string> paths, string color, bool clear)
        {
            if (busy || paths == null || paths.Count == 0) { return; }

            int total = paths.Count;
            Busy(true, "处理中 0/" + total.ToString());

            System.Threading.Tasks.Task.Run(delegate
            {
                int ok = 0;
                for (int k = 0; k < paths.Count; k++)
                {
                    int rc = RunSelf(clear ? "clear" : "set", color, paths[k]);
                    if (rc == 0) { ok++; }
                    int done = k + 1;
                    try
                    {
                        Dispatcher.BeginInvoke((Action)delegate
                        {
                            countText.Text = "处理中 " + done.ToString() + "/" + total.ToString();
                        });
                    }
                    catch { }
                }
                int success = ok;
                try
                {
                    Dispatcher.BeginInvoke((Action)delegate
                    {
                        Busy(false, "");
                        ReloadMarks();
                        ReloadLog();
                        if (clear)
                        {
                            WinUiDialog.Info(this, "处理完成",
                                "已清除 " + success.ToString() + " 个，共 " + total.ToString() + " 个。");
                        }
                        else
                        {
                            WinUiDialog.Info(this, "处理完成",
                                "成功 " + success.ToString() + " 个，共 " + total.ToString() + " 个。");
                        }
                    });
                }
                catch { }
            });
        }

        // ---------------- 关于页 ----------------
        private Panel BuildAboutPage()
        {
            StackPanel page = new StackPanel();

            StackPanel card = new StackPanel();
            card.Margin = new Thickness(24, 22, 24, 22);

            Grid head = new Grid();
            ColumnDefinition c0 = new ColumnDefinition();
            c0.Width = GridLength.Auto;
            head.ColumnDefinitions.Add(c0);
            ColumnDefinition c1 = new ColumnDefinition();
            c1.Width = new GridLength(1, GridUnitType.Star);
            head.ColumnDefinitions.Add(c1);

            Border iconBox = new Border();
            iconBox.Width = 56;
            iconBox.Height = 56;
            iconBox.CornerRadius = new CornerRadius(6);
            Image ico = new Image();
            ico.Stretch = Stretch.Uniform;
            ico.Margin = new Thickness(4);
            try { ico.Source = LoadAppIcon(); }
            catch { }
            iconBox.Child = ico;
            head.Children.Add(iconBox);

            StackPanel titles = new StackPanel();
            titles.VerticalAlignment = VerticalAlignment.Center;
            titles.Margin = new Thickness(16, 0, 0, 0);

            TextBlock name = new TextBlock();
            name.Text = "文件夹标记";
            name.FontSize = 18;
            name.FontWeight = FontWeights.SemiBold;
            name.Foreground = WinUi.B("TextPrimary");
            titles.Children.Add(name);

            TextBlock ver = new TextBlock();
            ver.Text = "版本 " + AppVersion;
            ver.FontSize = WinUi.SizeCaption;
            ver.Foreground = WinUi.B("TextSecondary");
            ver.Margin = new Thickness(0, 2, 0, 0);
            titles.Children.Add(ver);

            Grid.SetColumn(titles, 1);
            head.Children.Add(titles);
            card.Children.Add(head);

            card.Children.Add(Sep());

            aboutStats = new TextBlock();
            aboutStats.FontSize = 13;
            aboutStats.Foreground = WinUi.B("TextSecondary");
            aboutStats.LineHeight = 22;
            aboutStats.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            aboutStats.TextWrapping = TextWrapping.Wrap;
            card.Children.Add(aboutStats);

            StackPanel btns = new StackPanel();
            btns.Orientation = Orientation.Horizontal;
            btns.Margin = new Thickness(0, 18, 0, 0);

            Button bData = MakeButton("WinButton", "打开数据目录");
            bData.MinWidth = 116;
            bData.Click += delegate { OpenPath(DataDirPath()); };
            btns.Children.Add(bData);

            Button bCache = MakeButton("WinButton", "重建图标缓存");
            bCache.MinWidth = 116;
            bCache.Margin = new Thickness(8, 0, 0, 0);
            bCache.Click += delegate { RebuildIconCache(); };
            btns.Children.Add(bCache);

            Button bErr = MakeButton("WinSubtleButton", "查看错误日志");
            bErr.MinWidth = 112;
            bErr.Margin = new Thickness(8, 0, 0, 0);
            bErr.Click += delegate
            {
                string p = Io(System.IO.Path.Combine(LogDir(), "error.log"));
                if (!File.Exists(p))
                {
                    WinUiDialog.Info(this, "没有错误日志", "error.log 不存在，说明没有记录到异常。");
                    return;
                }
                try { Process.Start("notepad.exe", p); }
                catch { }
            };
            btns.Children.Add(bErr);

            Button bUpdate = MakeButton("WinAccentButton", "检查更新");
            bUpdate.MinWidth = 104;
            bUpdate.Margin = new Thickness(8, 0, 0, 0);
            bUpdate.Click += delegate { ManualCheckUpdate(); };
            btns.Children.Add(bUpdate);

            card.Children.Add(btns);
            page.Children.Add(WinUi.Card(card, new Thickness(0)));

            TextBlock note = new TextBlock();
            note.Text = "标记方式：desktop.ini 写入指向本机缓存的图标路径；含图片/视频的文件夹会按 Windows 的三层结构"
                + "（背板 + 内容预览 + 前盖）合成彩色图标。清理标记时会把文件夹恢复原样。";
            note.Foreground = WinUi.B("TextTertiary");
            note.FontSize = WinUi.SizeCaption;
            note.TextWrapping = TextWrapping.Wrap;
            note.LineHeight = 19;
            note.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            note.Margin = new Thickness(4, 14, 4, 0);
            page.Children.Add(note);

            return page;
        }

        private void ReloadAbout()
        {
            if (aboutStats == null) { return; }

            int cacheFiles;
            long cacheBytes;
            WinUi.MeasureDir(CacheIconDir(), out cacheFiles, out cacheBytes);

            int marked = ReadMarked().Count;
            int roots = ReadRoots().Count;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("程序目录　" + AppDomain.CurrentDomain.BaseDirectory);
            sb.AppendLine("数据目录　" + DataDirPath());
            sb.AppendLine("已标记　　" + marked.ToString() + " 个文件夹　　共享盘根　" + roots.ToString() + " 个");
            sb.AppendLine("图标缓存　" + cacheFiles.ToString() + " 个文件，"
                + (cacheBytes / 1024.0).ToString("0.0") + " KB");

            int frames = WinUi.CountIcoFrames(
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icons", "app.ico"));
            sb.AppendLine("图标资源　app.ico " + frames.ToString() + " 档尺寸");
            sb.Append("开机自启　" + (GetAutoStart() ? "已开启" : "未开启"));

            aboutStats.Text = sb.ToString();
        }

        private void OpenPath(string path)
        {
            try { Process.Start("explorer.exe", "\"" + path + "\""); }
            catch (Exception ex) { WinUiDialog.Info(this, "打开失败", ex.Message); }
        }

        private void RebuildIconCache()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("ie4uinit.exe", "-show");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi);
                WinUiDialog.Info(this, "已重建图标缓存",
                    "Windows 会重新读取所有文件夹图标，资源管理器里可能要几秒才刷新。");
            }
            catch (Exception ex)
            {
                WinUiDialog.Info(this, "重建失败", ex.Message);
            }
        }

        // ---------------- 通用 ----------------
        private void Busy(bool on, string text)
        {
            busy = on;
            if (busyBar != null)
            {
                busyBar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                if (on)
                {
                    DoubleAnimation a = new DoubleAnimation();
                    a.From = 0.25;
                    a.To = 1.0;
                    a.Duration = new Duration(TimeSpan.FromMilliseconds(620));
                    a.AutoReverse = true;
                    a.RepeatBehavior = RepeatBehavior.Forever;
                    busyBar.BeginAnimation(UIElement.OpacityProperty, a);
                }
                else
                {
                    busyBar.BeginAnimation(UIElement.OpacityProperty, null);
                }
            }
            if (on && countText != null) { countText.Text = text; }
        }

        private void RunAsync(string args, string text)
        {
            if (busy) { return; }
            Busy(true, text);
            System.Threading.Tasks.Task.Run(delegate
            {
                RunSelfRaw(args);
                try
                {
                    Dispatcher.BeginInvoke((Action)delegate
                    {
                        Busy(false, "");
                        ReloadMarks();
                        ReloadLog();
                        UpdateWatchStatus();
                    });
                }
                catch { }
            });
        }

        private static string ExePath()
        {
            try { return Process.GetCurrentProcess().MainModule.FileName; }
            catch
            {
                try { return System.Reflection.Assembly.GetEntryAssembly().Location; }
                catch { return "FolderMarker.exe"; }
            }
        }

        private static int RunSelf(string action, string color, string folder)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(ExePath());
                // 路径以 '\' 结尾时（盘符根 D:/、带尾斜杠的 UNC），
                // Windows 命令行会把 \" 当成转义引号，实参变成 D:" 而失败。
                string tail = folder.EndsWith("\\", StringComparison.Ordinal) ? "\\" : "";
                psi.Arguments = action + " " + color + " \"" + folder + tail + "\"";
                int rc = RunProcess(psi);
                if (rc != 0)
                {
                    DebugLog("ui run " + action + " rc=" + rc.ToString() + " exe=" + ExePath() + " " + folder);
                }
                return rc;
            }
            catch (Exception ex)
            {
                DebugLog("ui run EX " + ex.GetType().Name + ": " + ex.Message);
                return -1;
            }
        }

        private static void RunSelfRaw(string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(ExePath(), args);
                RunProcess(psi);
            }
            catch { }
        }

        private static int RunProcess(ProcessStartInfo psi)
        {
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            try { psi.EnvironmentVariables["FM_QUIET"] = "1"; }
            catch { }
            Process p = Process.Start(psi);
            p.WaitForExit(120000);
            return p.HasExited ? p.ExitCode : -1;
        }
    }
}
