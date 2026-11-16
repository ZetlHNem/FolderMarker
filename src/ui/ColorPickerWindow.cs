// 现代化取色器（WinUI 3 风格复刻）
//
// 替换旧的 WinForms ColorDialog（XP 那种经典对话框）。形态对齐 Win11 设置页：
//   · HSV 色域面板（拖动取色，圆角裁切，白色圆环游标）
//   · 全色相渐变条（点击/拖动换色相）
//   · HEX / RGB 双输入（HEX 支持粘贴 #RRGGBB / 大小写 / 缺 #）
//   · 预设色板（和 7 个预设颜色完全一致）+ 最近使用色板（跨次记忆）
//   · 实时文件夹预览：走真实染色管线（LoadShell + TintBitmapHsv），
//     预览里看到的壳色就是应用后的文件夹颜色
//   · 打开时自动预选当前文件夹已有的颜色
//
// 键盘：Enter 应用、Esc 取消。返回 c<RRGGBB> token。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using DrawingColor = System.Drawing.Color;
using MediaColor = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;
using FontFamily = System.Windows.Media.FontFamily;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Brush = System.Windows.Media.Brush;

internal sealed class ColorPickerWindow : Window
{
    private const double TitleBarHeight = 48;
    private const double Pad = 24;
    private const double SvWidth = 320;
    private const double SvHeight = 188;

    // 当前 HSV（H 0..360, S/V 0..1）
    private double _hue = 210;
    private double _sat = 0.80;
    private double _val = 0.95;

    private bool _syncing;

    private SvPanel _sv;
    private HueBar _hueBar;
    private Image _previewImage;
    private Border _chip;
    private TextBox _hexBox;
    private TextBox _rBox, _gBox, _bBox;
    private TextBlock _effectiveText;
    private WrapPanel _recentHost;
    private TextBlock _recentEmpty;

    /// <summary>确定时选中的 token：c + 6 位小写 hex</summary>
    public string SelectedToken { get; private set; }

    // ================= 入口 =================

    /// <summary>弹取色器。返回 c&lt;hex6&gt; token；用户取消返回 null。</summary>
    public static string Pick(string folder)
    {
        System.Windows.Application app = System.Windows.Application.Current;
        bool own = false;
        if (app == null)
        {
            app = new System.Windows.Application();
            own = true;
        }

        try
        {
            MediaColor initial;
            if (!TryInitialColor(folder, out initial))
            {
                initial = MediaColor.FromRgb(0x00, 0x67, 0xC0);
            }

            ColorPickerWindow w = new ColorPickerWindow(initial, folder);
            bool? ok = w.ShowDialog();
            return ok == true ? w.SelectedToken : null;
        }
        catch
        {
            throw;
        }
        finally
        {
            if (own) { app.Shutdown(); }
        }
    }

    /// <summary>
    /// 初始色优先级：文件夹当前颜色 → 最近用过的第一个 → 默认蓝。
    /// </summary>
    private static bool TryInitialColor(string folder, out MediaColor color)
    {
        color = MediaColor.FromRgb(0x00, 0x67, 0xC0);

        try
        {
            string token = Program.CurrentFolderColor(folder);
            int rgb;
            if (TokenToRgb(token, out rgb))
            {
                color = FromRgbInt(rgb);
                return true;
            }
        }
        catch { }

        try
        {
            string[] recent = Program.ReadRecentColors();
            int rgb;
            if (recent.Length > 0 && TokenToRgb("c" + recent[0], out rgb))
            {
                color = FromRgbInt(rgb);
                return true;
            }
        }
        catch { }

        return false;
    }

    /// <summary>c&lt;hex6&gt; / 预设名 → RGB。预设名走 FolderMarker 的色板语义。</summary>
    private static bool TokenToRgb(string token, out int rgb)
    {
        rgb = 0;
        if (string.IsNullOrEmpty(token)) { return false; }

        string t = token.Trim().ToLowerInvariant();
        if (t.Length == 7 && t[0] == 'c')
        {
            return int.TryParse(t.Substring(1), NumberStyles.HexNumber, null, out rgb);
        }

        // 预设色（红橙黄绿蓝紫灰）→ 用同一套 TargetHue/TargetSat 换成显示色
        double[] hues = { 357, 30, 45, 145, 215, 272, 0 };
        double[] sats = { 0.95, 1.00, 0.94, 0.82, 0.90, 0.75, 0.00 };
        string[] names = { "red", "orange", "yellow", "green", "blue", "purple", "gray" };
        int idx = Array.IndexOf(names, t);
        if (idx < 0) { return false; }

        double r, g, b;
        HsvToRgb(hues[idx], sats[idx], idx == 6 ? 0.72 : 0.95, out r, out g, out b);
        rgb = ((int)Math.Round(r * 255) << 16) | ((int)Math.Round(g * 255) << 8) | (int)Math.Round(b * 255);
        return true;
    }

    // ================= 构造 =================

    public ColorPickerWindow(MediaColor initial, string folder)
    {
        Title = "自定义颜色";
        Width = 620;
        Height = 524;
        MinWidth = 620;
        MaxWidth = 620;
        MinHeight = 524;
        MaxHeight = 524;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;

        WindowStyle = WindowStyle.None;
        Background = WinUi.ShellFill;
        AllowsTransparency = false;

        WindowChrome chrome = new WindowChrome();
        chrome.CaptionHeight = TitleBarHeight;
        chrome.ResizeBorderThickness = new Thickness(0);
        chrome.GlassFrameThickness = new Thickness(0);
        chrome.CornerRadius = new CornerRadius(0);
        chrome.UseAeroCaptionButtons = false;
        WindowChrome.SetWindowChrome(this, chrome);

        FontFamily = new FontFamily(WinUi.FontUi);
        FontSize = WinUi.SizeBody;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

        if (WinUi.Res.Count > 0) { Resources.MergedDictionaries.Add(WinUi.Res); }

        double h, s, v;
        RgbToHsv(initial.R / 255.0, initial.G / 255.0, initial.B / 255.0, out h, out s, out v);
        _hue = h;   // RgbToHsv 返回的 h 已是角度
        _sat = s;
        _val = v;

        Grid root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TitleBarHeight) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        UIElement bar = BuildTitleBar();
        Grid.SetRow(bar, 0);
        root.Children.Add(bar);

        UIElement body = BuildBody();
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        Content = root;

        KeyDown += delegate (object s2, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { DialogResult = false; }
            else if (e.Key == Key.Enter) { Apply(); }
        };

        SyncAll();

        SourceInitialized += delegate
        {
            try
            {
                WinUi.EnableMica(this);
                Topmost = true;
                Activate();
                Topmost = false;
                Focus();
            }
            catch { }
        };
    }

    private UIElement BuildTitleBar()
    {
        Grid g = new Grid();
        g.Background = Brushes.Transparent;

        StackPanel left = new StackPanel();
        left.Orientation = Orientation.Horizontal;
        left.VerticalAlignment = VerticalAlignment.Center;
        left.Margin = new Thickness(16, 0, 0, 0);

        // 用一个小色轮图标代替 app 图标，和「颜色」语义呼应
        Border dot = new Border();
        dot.Width = 16;
        dot.Height = 16;
        dot.CornerRadius = new CornerRadius(8);
        dot.Background = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(MediaColor.FromRgb(0xFF, 0x5A, 0x6A), 0.00),
                new GradientStop(MediaColor.FromRgb(0xFF, 0xC4, 0x3C), 0.18),
                new GradientStop(MediaColor.FromRgb(0x8C, 0xE0, 0x5C), 0.38),
                new GradientStop(MediaColor.FromRgb(0x3C, 0xC8, 0xD8), 0.58),
                new GradientStop(MediaColor.FromRgb(0x4A, 0x7C, 0xF0), 0.78),
                new GradientStop(MediaColor.FromRgb(0xA0, 0x6C, 0xF0), 1.00),
            }, new Point(0, 0), new Point(1, 1));
        left.Children.Add(dot);

        TextBlock name = WinUi.Text("自定义颜色", WinUi.SizeCaption, WinUi.B("TextSecondary"));
        name.Margin = new Thickness(10, 0, 0, 0);
        left.Children.Add(name);
        g.Children.Add(left);

        // 只有一个关闭按钮：46x32 命中区，和系统 caption 按钮同规格
        Border close = new Border();
        close.Width = 46;
        close.Height = 32;
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Background = Brushes.Transparent;
        close.CornerRadius = new CornerRadius(4);
        close.Margin = new Thickness(0, 4, 4, 0);
        close.Child = WinUi.Glyph(WinUi.GlyphClose, 10, WinUi.B("TextPrimary"));
        close.MouseEnter += delegate { close.Background = WinUi.B("CaptionHover"); };
        close.MouseLeave += delegate { close.Background = Brushes.Transparent; };
        close.MouseLeftButtonUp += delegate { DialogResult = false; };
        g.Children.Add(close);

        return g;
    }

    private UIElement BuildBody()
    {
        Grid outer = new Grid();
        outer.Margin = new Thickness(Pad, 4, Pad, Pad);

        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SvWidth) });
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 主区
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 色板
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 弹簧
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 按钮

        // ---- 左列：色域面板 + 色相条 ----
        StackPanel leftCol = new StackPanel();

        _sv = new SvPanel();
        _sv.Width = SvWidth;
        _sv.Height = SvHeight;
        _sv.Hue = _hue;
        _sv.Sat = _sat;
        _sv.Val = _val;
        _sv.Changed += delegate
        {
            if (_syncing) { return; }
            _hue = _sv.Hue;
            _sat = _sv.Sat;
            _val = _sv.Val;
            SyncAll();
        };
        leftCol.Children.Add(_sv);

        _hueBar = new HueBar();
        _hueBar.Width = SvWidth;
        _hueBar.Height = 20;
        _hueBar.Margin = new Thickness(0, 12, 0, 0);
        _hueBar.Hue = _hue;
        _hueBar.Changed += delegate
        {
            if (_syncing) { return; }
            _hue = _hueBar.Hue;
            _sv.Hue = _hue;
            SyncAll();
        };
        leftCol.Children.Add(_hueBar);

        Grid.SetRow(leftCol, 0);
        Grid.SetColumn(leftCol, 0);
        outer.Children.Add(leftCol);

        // ---- 右列：预览 + 输入 ----
        StackPanel rightCol = new StackPanel();

        Border previewCard = WinUi.Card(null, new Thickness(10));
        previewCard.HorizontalAlignment = HorizontalAlignment.Stretch;

        StackPanel previewInner = new StackPanel();
        previewInner.HorizontalAlignment = HorizontalAlignment.Center;

        _previewImage = new Image();
        _previewImage.Width = 88;
        _previewImage.Height = 88;
        RenderOptions.SetBitmapScalingMode(_previewImage, BitmapScalingMode.HighQuality);
        previewInner.Children.Add(_previewImage);

        TextBlock pvLabel = WinUi.Text("预览", WinUi.SizeCaption, WinUi.B("TextTertiary"));
        pvLabel.HorizontalAlignment = HorizontalAlignment.Center;
        pvLabel.Margin = new Thickness(0, 6, 0, 0);
        previewInner.Children.Add(pvLabel);

        _effectiveText = WinUi.Text("", 11, WinUi.B("TextTertiary"));
        _effectiveText.HorizontalAlignment = HorizontalAlignment.Center;
        _effectiveText.Margin = new Thickness(0, 2, 0, 0);
        previewInner.Children.Add(_effectiveText);

        previewCard.Child = previewInner;
        rightCol.Children.Add(previewCard);

        // 当前色块 + HEX
        Grid chipRow = new Grid();
        chipRow.Margin = new Thickness(0, 14, 0, 0);
        chipRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        chipRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _chip = new Border();
        _chip.Width = 30;
        _chip.Height = 30;
        _chip.CornerRadius = new CornerRadius(6);
        _chip.BorderThickness = new Thickness(1);
        _chip.BorderBrush = WinUi.B("ControlStroke");
        _chip.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_chip, 0);
        chipRow.Children.Add(_chip);

        _hexBox = new TextBox();
        ApplyInputStyle(_hexBox);
        _hexBox.Margin = new Thickness(8, 0, 0, 0);
        _hexBox.Height = 32;
        _hexBox.Padding = new Thickness(8, 4, 8, 5);
        _hexBox.FontFamily = new FontFamily(WinUi.FontMono);
        _hexBox.TextChanged += delegate { OnHexChanged(); };
        Grid.SetColumn(_hexBox, 1);
        chipRow.Children.Add(_hexBox);

        rightCol.Children.Add(chipRow);

        // RGB 三个小框
        Grid rgbRow = new Grid();
        rgbRow.Margin = new Thickness(0, 8, 0, 0);
        for (int i = 0; i < 3; i++)
        {
            rgbRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        _rBox = MakeRgbBox("R", 0, rgbRow);
        _gBox = MakeRgbBox("G", 1, rgbRow);
        _bBox = MakeRgbBox("B", 2, rgbRow);
        rightCol.Children.Add(rgbRow);

        Grid.SetRow(rightCol, 0);
        Grid.SetColumn(rightCol, 2);
        outer.Children.Add(rightCol);

        // ---- 色板区 ----
        StackPanel paletteCol = new StackPanel();
        paletteCol.Margin = new Thickness(0, 22, 0, 0);

        paletteCol.Children.Add(SectionLabel("预设"));
        WrapPanel presetHost = new WrapPanel();
        presetHost.Margin = new Thickness(0, 8, 0, 0);
        string[] presetNames = { "red", "orange", "yellow", "green", "blue", "purple", "gray" };
        string[] presetTitles = { "红", "橙", "黄", "绿", "蓝", "紫", "灰" };
        for (int i = 0; i < presetNames.Length; i++)
        {
            int rgb;
            TokenToRgb(presetNames[i], out rgb);
            presetHost.Children.Add(Swatch(FromRgbInt(rgb), presetTitles[i], delegate { SetFromRgb(rgb); }));
        }
        paletteCol.Children.Add(presetHost);

        TextBlock recentLabel = SectionLabel("最近使用");
        recentLabel.Margin = new Thickness(0, 16, 0, 0);
        paletteCol.Children.Add(recentLabel);

        _recentHost = new WrapPanel();
        _recentHost.Margin = new Thickness(0, 8, 0, 0);
        paletteCol.Children.Add(_recentHost);

        _recentEmpty = WinUi.Text("选过的自定义色会记在这里，下次一点即用", WinUi.SizeCaption, WinUi.B("TextTertiary"));
        _recentEmpty.Margin = new Thickness(0, 8, 0, 0);
        paletteCol.Children.Add(_recentEmpty);

        Grid.SetRow(paletteCol, 1);
        Grid.SetColumn(paletteCol, 0);
        Grid.SetColumnSpan(paletteCol, 3);
        outer.Children.Add(paletteCol);

        // ---- 底部按钮 ----
        StackPanel btns = new StackPanel();
        btns.Orientation = Orientation.Horizontal;
        btns.HorizontalAlignment = HorizontalAlignment.Right;
        btns.Margin = new Thickness(0, 12, 0, 0);

        Button cancel = WinUi.Btn("WinButton", "取消");
        cancel.MinWidth = 92;
        cancel.Height = 32;
        cancel.Margin = new Thickness(0, 0, 8, 0);
        cancel.Click += delegate { DialogResult = false; };
        btns.Children.Add(cancel);

        Button ok = WinUi.Btn("WinAccentButton", "应用");
        ok.MinWidth = 92;
        ok.Height = 32;
        ok.IsDefault = true;
        ok.Click += delegate { Apply(); };
        btns.Children.Add(ok);

        Grid.SetRow(btns, 3);
        Grid.SetColumn(btns, 0);
        Grid.SetColumnSpan(btns, 3);
        outer.Children.Add(btns);

        LoadRecent();

        return outer;
    }

    private static TextBlock SectionLabel(string text)
    {
        return WinUi.Text(text, WinUi.SizeCaption, WinUi.B("TextSecondary"));
    }

    private void ApplyInputStyle(TextBox box)
    {
        Style st = WinUi.S("WinTextBox");
        if (st != null) { box.Style = st; }
        box.FontSize = WinUi.SizeCaption + 1;
        box.VerticalContentAlignment = VerticalAlignment.Center;
    }

    private TextBox MakeRgbBox(string tag, int col, Grid host)
    {
        // 标签 + 输入框横向铺满整格，输入框吃掉剩余宽度
        Grid cell = new Grid();
        cell.Margin = new Thickness(col == 0 ? 0 : 8, 0, 0, 0);
        cell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        TextBlock lb = WinUi.Text(tag, WinUi.SizeCaption, WinUi.B("TextTertiary"));
        lb.Margin = new Thickness(0, 0, 6, 0);
        Grid.SetColumn(lb, 0);
        cell.Children.Add(lb);

        TextBox box = new TextBox();
        ApplyInputStyle(box);
        box.Height = 32;
        box.Padding = new Thickness(6, 4, 6, 5);
        box.HorizontalAlignment = HorizontalAlignment.Stretch;
        box.TextChanged += delegate { OnRgbChanged(); };
        Grid.SetColumn(box, 1);
        cell.Children.Add(box);

        Grid.SetColumn(cell, col);
        host.Children.Add(cell);
        return box;
    }

    private Border Swatch(MediaColor c, string tip, Action onClick)
    {
        Border outer = new Border();
        outer.Width = 28;
        outer.Height = 28;
        outer.Margin = new Thickness(0, 0, 8, 0);
        outer.CornerRadius = new CornerRadius(6);
        outer.Background = new SolidColorBrush(c);
        outer.BorderThickness = new Thickness(1);
        outer.BorderBrush = WinUi.B("ControlStroke");
        outer.Cursor = Cursors.Hand;
        outer.ToolTip = tip;
        outer.MouseEnter += delegate { outer.Opacity = 0.82; };
        outer.MouseLeave += delegate { outer.Opacity = 1.0; };
        outer.MouseLeftButtonUp += delegate { onClick(); };
        return outer;
    }

    // ================= 状态同步 =================

    private MediaColor CurrentColor
    {
        get
        {
            double r, g, b;
            HsvToRgb(_hue, _sat, _val, out r, out g, out b);
            return MediaColor.FromRgb(
                (byte)Math.Round(Clamp01(r) * 255),
                (byte)Math.Round(Clamp01(g) * 255),
                (byte)Math.Round(Clamp01(b) * 255));
        }
    }

    /// <summary>把当前 HSV 同步到所有控件（输入框/色块/预览/游标）</summary>
    private void SyncAll()
    {
        if (_sv == null || _hexBox == null) { return; }
        _syncing = true;
        try
        {
            MediaColor c = CurrentColor;
            int rgb = (c.R << 16) | (c.G << 8) | c.B;

            _sv.Hue = _hue;
            _sv.Sat = _sat;
            _sv.Val = _val;
            _sv.Refresh();

            _hueBar.Hue = _hue;
            _hueBar.Refresh();

            _chip.Background = new SolidColorBrush(c);
            _hexBox.Text = "#" + rgb.ToString("X6");
            _rBox.Text = c.R.ToString();
            _gBox.Text = c.G.ToString();
            _bBox.Text = c.B.ToString();

            UpdatePreview(rgb);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void UpdatePreview(int rgb)
    {
        try
        {
            double eh, es;
            Program.RgbToEffectiveHueSat(rgb, out eh, out es);

            using (Bitmap bmp = Program.PreviewFolder(88, eh, es))
            {
                if (bmp != null)
                {
                    IntPtr h = bmp.GetHbitmap();
                    try
                    {
                        _previewImage.Source = Imaging.CreateBitmapSourceFromHBitmap(
                            h, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    }
                    finally { DeleteObject(h); }
                }
            }

            _effectiveText.Text = es <= 0.03 ? "灰阶" : ("色相 " + Math.Round(eh) + "° · 饱和 " + Math.Round(es * 100) + "%");
        }
        catch { }
    }

    private void OnHexChanged()
    {
        if (_syncing) { return; }

        string t = (_hexBox.Text ?? "").Trim().TrimStart('#');
        if (t.Length != 6 || !IsHex6(t)) { return; }

        int rgb;
        if (!int.TryParse(t, NumberStyles.HexNumber, null, out rgb)) { return; }

        SetFromRgb(rgb);
    }

    private void OnRgbChanged()
    {
        if (_syncing) { return; }

        int r, g, b;
        if (!int.TryParse((_rBox.Text ?? "").Trim(), out r)) { return; }
        if (!int.TryParse((_gBox.Text ?? "").Trim(), out g)) { return; }
        if (!int.TryParse((_bBox.Text ?? "").Trim(), out b)) { return; }
        if (r < 0 || r > 255 || g < 0 || g > 255 || b < 0 || b > 255) { return; }

        SetFromRgb((r << 16) | (g << 8) | b);
    }

    /// <summary>从 RGB 反推 HSV 并全量刷新（保留色相，避免灰/黑时游标乱跳）</summary>
    private void SetFromRgb(int rgb)
    {
        double h, s, v;
        RgbToHsv(((rgb >> 16) & 255) / 255.0, ((rgb >> 8) & 255) / 255.0, (rgb & 255) / 255.0,
                 out h, out s, out v);
        if (s > 0.001) { _hue = h; }
        _sat = s;
        _val = v;
        SyncAll();
    }

    // ================= 最近色板 =================

    private void LoadRecent()
    {
        _recentHost.Children.Clear();
        try
        {
            string[] recent = Program.ReadRecentColors();
            foreach (string hex in recent)
            {
                int rgb;
                if (!int.TryParse(hex, NumberStyles.HexNumber, null, out rgb)) { continue; }
                string capturedHex = hex;
                _recentHost.Children.Add(Swatch(FromRgbInt(rgb), "#" + hex.ToUpperInvariant(),
                    delegate { SetFromRgb(RgbOf(capturedHex)); }));
            }
            _recentEmpty.Visibility = recent.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch { _recentEmpty.Visibility = Visibility.Visible; }
    }

    private static int RgbOf(string hex6)
    {
        int rgb;
        int.TryParse(hex6, NumberStyles.HexNumber, null, out rgb);
        return rgb;
    }

    // ================= 应用 =================

    private void Apply()
    {
        MediaColor c = CurrentColor;
        string hex = ((c.R << 16) | (c.G << 8) | c.B).ToString("X6").ToLowerInvariant();
        SelectedToken = "c" + hex;
        try { Program.PushRecentColor(hex); } catch { }
        DialogResult = true;
    }

    // ================= 颜色数学 =================

    private static double Clamp01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

    private static bool IsHex6(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!hex) { return false; }
        }
        return true;
    }

    private static MediaColor FromRgbInt(int rgb)
    {
        return MediaColor.FromRgb((byte)((rgb >> 16) & 255), (byte)((rgb >> 8) & 255), (byte)(rgb & 255));
    }

    private static void HsvToRgb(double h, double s, double v, out double r, out double g, out double b)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double m = v - c;
        double r1 = 0, g1 = 0, b1 = 0;

        if (h < 60) { r1 = c; g1 = x; }
        else if (h < 120) { r1 = x; g1 = c; }
        else if (h < 180) { g1 = c; b1 = x; }
        else if (h < 240) { g1 = x; b1 = c; }
        else if (h < 300) { r1 = x; b1 = c; }
        else { r1 = c; b1 = x; }

        r = r1 + m; g = g1 + m; b = b1 + m;
    }

    private static void RgbToHsv(double r, double g, double b, out double h, out double s, out double v)
    {
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double d = max - min;

        v = max;
        s = max <= 0 ? 0 : d / max;

        if (d <= 0) { h = 0; return; }

        if (max == r) { h = ((g - b) / d) % 6; }
        else if (max == g) { h = (b - r) / d + 2; }
        else { h = (r - g) / d + 4; }

        h *= 60;
        if (h < 0) { h += 360; }
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}

// ================= 自绘：HSV 色域面板 =================

internal sealed class SvPanel : FrameworkElement
{
    public double Hue { get; set; }
    public double Sat { get; set; }
    public double Val { get; set; }

    public event Action Changed;

    private const double Radius = 8;
    private const double ThumbR = 7;

    public void Refresh() { InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) { return; }

        Rect rect = new Rect(0, 0, w, h);
        RectangleGeometry clip = new RectangleGeometry(rect, Radius, Radius);
        dc.PushClip(clip);

        // 底色：当前纯色相
        double r0, g0, b0;
        HsvToRgbLocal(Hue, 1.0, 1.0, out r0, out g0, out b0);
        dc.DrawRectangle(new SolidColorBrush(MediaColor.FromRgb(
            (byte)Math.Round(r0 * 255), (byte)Math.Round(g0 * 255), (byte)Math.Round(b0 * 255))), null, rect);

        // 白 → 透明（横向：饱和度）
        LinearGradientBrush white = new LinearGradientBrush(
            MediaColor.FromArgb(255, 255, 255, 255),
            MediaColor.FromArgb(0, 255, 255, 255),
            new Point(0, 0.5), new Point(1, 0.5));
        dc.DrawRectangle(white, null, rect);

        // 透明 → 黑（纵向：明度）
        LinearGradientBrush black = new LinearGradientBrush(
            MediaColor.FromArgb(0, 0, 0, 0),
            MediaColor.FromArgb(255, 0, 0, 0),
            new Point(0.5, 0), new Point(0.5, 1));
        dc.DrawRectangle(black, null, rect);

        dc.Pop();

        // 1px 描边
        MediaColor edge = WinUi.DarkMode ? MediaColor.FromArgb(0x24, 255, 255, 255) : MediaColor.FromArgb(0x14, 0, 0, 0);
        dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(edge), 1),
            new Rect(0.5, 0.5, w - 1, h - 1), Radius, Radius);

        // 游标（钳制在面板内，避免贴边时被裁掉一半）
        double x = ThumbR + 1 + Sat * (w - 2 * (ThumbR + 1));
        double y = ThumbR + 1 + (1 - Val) * (h - 2 * (ThumbR + 1));
        Point p = new Point(x, y);

        MediaColor halo = WinUi.DarkMode ? MediaColor.FromArgb(0x66, 0, 0, 0) : MediaColor.FromArgb(0x40, 0, 0, 0);
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(halo), 1), p, ThumbR + 2, ThumbR + 2);
        dc.DrawEllipse(null, new Pen(Brushes.White, 2), p, ThumbR, ThumbR);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        CaptureMouse();
        Pick(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            Pick(e.GetPosition(this));
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
    }

    private void Pick(Point p)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) { return; }

        // 和 OnRender 的游标映射保持一致（各边内缩一个游标半径）
        double inset = ThumbR + 1;
        Sat = Clamp01((p.X - inset) / Math.Max(1.0, w - 2 * inset));
        Val = Clamp01(1 - (p.Y - inset) / Math.Max(1.0, h - 2 * inset));
        InvalidateVisual();
        Action ch = Changed;
        if (ch != null) { ch(); }
    }

    private static double Clamp01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

    private static void HsvToRgbLocal(double h, double s, double v, out double r, out double g, out double b)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double m = v - c;
        double r1 = 0, g1 = 0, b1 = 0;

        if (h < 60) { r1 = c; g1 = x; }
        else if (h < 120) { r1 = x; g1 = c; }
        else if (h < 180) { g1 = c; b1 = x; }
        else if (h < 240) { g1 = x; b1 = c; }
        else if (h < 300) { r1 = x; b1 = c; }
        else { r1 = c; b1 = x; }

        r = r1 + m; g = g1 + m; b = b1 + m;
    }
}

// ================= 自绘：全色相滑条 =================

internal sealed class HueBar : FrameworkElement
{
    public double Hue { get; set; }
    public event Action Changed;

    public void Refresh() { InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) { return; }

        double barH = 14;
        double top = (h - barH) / 2;
        Rect rect = new Rect(0, top, w, barH);

        GradientStopCollection stops = new GradientStopCollection();
        for (int i = 0; i <= 6; i++)
        {
            double hh = i * 60.0;
            double r, g, b;
            HsvToRgbLocal(hh, 1, 1, out r, out g, out b);
            stops.Add(new GradientStop(MediaColor.FromRgb(
                (byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255)),
                i / 6.0));
        }
        LinearGradientBrush brush = new LinearGradientBrush(stops, new Point(0, 0.5), new Point(1, 0.5));

        RectangleGeometry clip = new RectangleGeometry(rect, barH / 2, barH / 2);
        dc.PushClip(clip);
        dc.DrawRectangle(brush, null, rect);
        dc.Pop();
        MediaColor edge2 = WinUi.DarkMode ? MediaColor.FromArgb(0x24, 255, 255, 255) : MediaColor.FromArgb(0x14, 0, 0, 0);
        dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(edge2), 1),
            new Rect(0.5, top + 0.5, w - 1, barH - 1), barH / 2, barH / 2);

        // 游标：竖向胶囊（左右各留半个宽度，避免贴边被裁）
        double cw = 10, chh = barH + 8;
        double x = cw / 2 + Hue / 360.0 * (w - cw);
        Rect thumb = new Rect(x - cw / 2, top - 4, cw, chh);
        dc.DrawRoundedRectangle(Brushes.White,
            new Pen(new SolidColorBrush(MediaColor.FromArgb(0x30, 0, 0, 0)), 1), thumb, 5, 5);   // 白色胶囊自带描边，深浅色都适用
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        CaptureMouse();
        Pick(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            Pick(e.GetPosition(this));
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
    }

    private void Pick(Point p)
    {
        double w = ActualWidth;
        if (w <= 0) { return; }

        double cw2 = 10.0 / 2;   // 与 OnRender 的胶囊宽度一致
        double t = (p.X - cw2) / Math.Max(1.0, w - 2 * cw2);
        t = t < 0 ? 0 : (t > 1 ? 1 : t);
        Hue = t * 360.0;
        InvalidateVisual();
        Action ch = Changed;
        if (ch != null) { ch(); }
    }

    private static void HsvToRgbLocal(double h, double s, double v, out double r, out double g, out double b)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double m = v - c;
        double r1 = 0, g1 = 0, b1 = 0;

        if (h < 60) { r1 = c; g1 = x; }
        else if (h < 120) { r1 = x; g1 = c; }
        else if (h < 180) { g1 = c; b1 = x; }
        else if (h < 240) { g1 = x; b1 = c; }
        else if (h < 300) { r1 = x; b1 = c; }
        else { r1 = c; b1 = x; }

        r = r1 + m; g = g1 + m; b = b1 + m;
    }
}
