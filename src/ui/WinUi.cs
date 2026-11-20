// WinUI 3 视觉规格复刻层（WPF 实现）
// 目标：在不引入 Windows App SDK / .NET 8 的前提下，把 WinUI 3（Windows 11 设置页）
// 的色板、控件模板、Mica 云母背景、自绘标题栏、导航选中态、滚动条全部按规格实现。
// 模板用 XamlReader.Parse 加载 —— 比 FrameworkElementFactory 可靠，且支持 Trigger。
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

internal static class WinUi
{
    // ---------------- 字体 ----------------
    // Win11 用 Segoe UI Variable；中文回退到微软雅黑。WPF 会逐字符回退。
    public const string FontUi = "Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI";
    public const string FontDisplay = "Segoe UI Variable Display, Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI";
    public const string FontIcon = "Segoe Fluent Icons, Segoe MDL2 Assets";
    public const string FontMono = "Cascadia Mono, Consolas, Courier New";

    // ---------------- 字号（WinUI type ramp）----------------
    public const double SizeTitle = 28;      // Title
    public const double SizeSubtitle = 14;   // Body
    public const double SizeBody = 14;       // Body
    public const double SizeCaption = 12;    // Caption

    // ---------------- 字形（Segoe Fluent Icons / MDL2）----------------
    public const string GlyphFolder = "\uE8B7";
    public const string GlyphSync = "\uE895";
    public const string GlyphCloud = "\uE753";
    public const string GlyphDocument = "\uE8A5";
    public const string GlyphChevronDown = "\uE70D";
    public const string GlyphChevronRight = "\uE76C";
    public const string GlyphCheck = "\uE73E";
    public const string GlyphMinimize = "\uE921";
    public const string GlyphMaximize = "\uE922";
    public const string GlyphRestore = "\uE923";
    public const string GlyphClose = "\uE8BB";
    public const string GlyphInfo = "\uE946";
    public const string GlyphSettings = "\uE713";
    public const string GlyphRefresh = "\uE72C";
    public const string GlyphFolderOpen = "\uE838";
    public const string GlyphSearch = "\uE721";
    public const string GlyphDelete = "\uE74D";
    public const string GlyphBroom = "\uEA99";

    // ---------------- 资源字典 ----------------
    private static ResourceDictionary _res;

    public static ResourceDictionary Res
    {
        get
        {
            if (_res == null) { _res = Build(); }
            return _res;
        }
    }

    /// <summary>
    /// 色板：键 → (浅色, 深色)。必须在 XamlReader.Parse 之前注入 ——
    /// 样式模板里全是 {StaticResource Xxx}，解析那一刻就把画刷对象定死了，
    /// 解析后再替换字典条目对模板无效（这就是之前深色下按钮/开关仍是浅色的原因）。
    /// </summary>
    private static readonly string[,] Palette =
    {
        { "LayerFill",           "#B3FFFFFF", "#CC1F1F1F" },
        { "CardFill",            "#B3FFFFFF", "#CC2B2B2B" },
        { "CardFillSolid",       "#FFFFFF",   "#FF2B2B2B" },
        { "CardStroke",          "#0D000000", "#15FFFFFF" },
        { "DividerStroke",       "#0F000000", "#14FFFFFF" },

        { "ControlFill",         "#B3FFFFFF", "#14FFFFFF" },
        { "ControlFillHover",    "#F2F9F9F9", "#1FFFFFFF" },
        { "ControlFillPressed",  "#E6F9F9F9", "#0AFFFFFF" },
        { "ControlFillDisabled", "#4DF9F9F9", "#0BFFFFFF" },
        { "ControlStroke",       "#12000000", "#18FFFFFF" },
        { "ControlStrokeBottom", "#1F000000", "#26FFFFFF" },

        { "TextPrimary",         "#E4000000", "#FFFFFFFF" },
        { "TextSecondary",       "#9E000000", "#C5FFFFFF" },
        { "TextTertiary",        "#72000000", "#8AFFFFFF" },
        { "TextDisabled",        "#5C000000", "#5DFFFFFF" },

        { "AccentFill",          "#FF0067C0", "#FF4CC2FF" },
        { "AccentFillHover",     "#FF1877CE", "#FF5BC8FF" },
        { "AccentFillPressed",   "#FF196FAF", "#FF3FB4F2" },
        { "AccentText",          "#FFFFFFFF", "#FF0B1B26" },

        { "SubtleHover",         "#09000000", "#0FFFFFFF" },
        { "SubtlePressed",       "#06000000", "#0AFFFFFF" },
        { "NavSelected",         "#0F000000", "#14FFFFFF" },
        { "RowHover",            "#07000000", "#0AFFFFFF" },
        { "RowSelected",         "#0F000000", "#14FFFFFF" },

        { "ScrollThumb",         "#59000000", "#8AFFFFFF" },
        { "ScrollThumbHover",    "#8A000000", "#B3FFFFFF" },

        { "FlyoutFill",          "#FFFFFFFF", "#FF2C2C2C" },
        { "FlyoutStroke",        "#14000000", "#20FFFFFF" },

        { "DangerFill",          "#FFC42B1C", "#FFC42B1C" },
        { "DangerFillHover",     "#FFD13438", "#FFD13438" },
        { "DangerFillPressed",   "#FFB22B1E", "#FFB22B1E" },
        { "CaptionHover",        "#09000000", "#10FFFFFF" },
        { "CaptionPressed",      "#06000000", "#0CFFFFFF" },
    };

    private static string PaletteXaml()
    {
        int col = DarkMode ? 2 : 1;
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append("  <!-- 运行时注入的主题色板 -->\r\n");
        for (int i = 0; i < Palette.GetLength(0); i++)
        {
            sb.Append("  <SolidColorBrush x:Key='").Append(Palette[i, 0])
              .Append("' Color='").Append(Palette[i, col]).Append("'/>\r\n");
        }
        return sb.ToString();
    }

    public static Style S(string key)
    {
        object o = Res[key];
        return o as Style;
    }

    public static Brush B(string key)
    {
        object o = Res[key];
        return o as Brush;
    }

    // ---------------- DWM / Mica ----------------
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWA_MICA_EFFECT = 1029;   // Win11 21H2 未公开属性

    private const int DWMSBT_MAINWINDOW = 2;      // Mica
    private const int DWMWCP_ROUND = 2;
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

    /// <summary>Mica 是否真的生效。false 时窗口必须用不透明底色，否则会变成透明鬼影。</summary>
    public static bool MicaEnabled { get; private set; }

    /// <summary>
    /// 窗口底色。
    /// 为什么不走 Mica：WPF 要让 DWM 画云母，只能把 WindowChrome.GlassFrameThickness 设成 -1，
    /// 而那样会给窗口套一圈 1px 灰色玻璃边框、并且让 Win11 圆角失效 —— 观感比纯色更「不像」。
    /// 这里用 Win11 在「透明效果关闭」时的官方底色，配合白卡片，形态与设置页一致。
    /// </summary>
    public static Brush ShellFill
    {
        get
        {
            return DarkMode
                ? new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20))
                : new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
        }
    }

    // ---------------- 深浅色主题 ----------------
    private static bool? _dark;

    /// <summary>
    /// 是否深色主题。优先级：settings.ini 的 Theme（1 浅色 / 2 深色）→ 系统 AppsUseLightTheme。
    /// 每个进程只解析一次（Res 里的画刷在启动时定型，改主题需重启窗口进程）。
    /// </summary>
    public static bool DarkMode
    {
        get
        {
            if (!_dark.HasValue)
            {
                int theme = 0;
                try { theme = Program.GetSettingInt("Theme", 0); } catch { }
                _dark = theme == 1 ? false : (theme == 2 ? true : IsSystemDark());
            }
            return _dark.Value;
        }
    }

    /// <summary>
    /// 设置 Win11 窗口形态：圆角 + 跟随主题的深浅色标题栏。
    /// 不启用 backdrop（见 ShellFill 的注释）。
    /// </summary>
    public static bool EnableMica(Window w)
    {
        MicaEnabled = false;

        IntPtr h = new WindowInteropHelper(w).Handle;
        if (h == IntPtr.Zero) { return false; }

        int dark = DarkMode ? 1 : 0;
        DwmSetWindowAttribute(h, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);

        int corner = DWMWCP_ROUND;
        DwmSetWindowAttribute(h, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);

        // 去掉 Win11 在窗口外围画的那圈 1px 深灰边框（WinUI 3 应用都这么做）
        int noBorder = DWMWA_COLOR_NONE;
        DwmSetWindowAttribute(h, DWMWA_BORDER_COLOR, ref noBorder, 4);

        return false;
    }

    private static bool IsSystemDark()
    {
        try
        {
            object v = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1);
            if (v is int) { return ((int)v) == 0; }
        }
        catch { }
        return false;
    }

    // =================================================================
    //  XAML：色板 + 控件模板
    //  注意：C# 里用 @"" 包裹，XAML 属性统一用单引号，避免转义。
    // =================================================================
    private const string Xaml = @"
<ResourceDictionary
    xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

  <!-- ============ ScrollBar（WinUI 细滚动条：2px，悬停 6px，无箭头）============ -->
  <Style x:Key='WinScrollBar' TargetType='ScrollBar'>
    <Setter Property='Width' Value='12'/>
    <Setter Property='MinWidth' Value='12'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollBar'>
          <Grid Background='Transparent'>
            <Track x:Name='PART_Track' Orientation='{TemplateBinding Orientation}' IsDirectionReversed='True'>
              <Track.DecreaseRepeatButton>
                <RepeatButton Command='ScrollBar.PageUpCommand' Focusable='False' Opacity='0'/>
              </Track.DecreaseRepeatButton>
              <Track.Thumb>
                <Thumb x:Name='PART_Thumb' Focusable='False' Background='Transparent'>
                  <Thumb.Template>
                    <ControlTemplate TargetType='Thumb'>
                      <Border x:Name='Tb' Background='{StaticResource ScrollThumb}'
                              CornerRadius='3' Margin='5,2,5,2'/>
                      <ControlTemplate.Triggers>
                        <Trigger Property='IsMouseOver' Value='True'>
                          <Setter TargetName='Tb' Property='Background' Value='{StaticResource ScrollThumbHover}'/>
                          <Setter TargetName='Tb' Property='Margin' Value='2,2,2,2'/>
                        </Trigger>
                        <Trigger Property='IsDragging' Value='True'>
                          <Setter TargetName='Tb' Property='Background' Value='{StaticResource ScrollThumbHover}'/>
                          <Setter TargetName='Tb' Property='Margin' Value='2,2,2,2'/>
                        </Trigger>
                      </ControlTemplate.Triggers>
                    </ControlTemplate>
                  </Thumb.Template>
                </Thumb>
              </Track.Thumb>
              <Track.IncreaseRepeatButton>
                <RepeatButton Command='ScrollBar.PageDownCommand' Focusable='False' Opacity='0'/>
              </Track.IncreaseRepeatButton>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============ ScrollViewer ============ -->
  <ControlTemplate x:Key='WinScrollViewerTemplate' TargetType='ScrollViewer'>
    <Grid>
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width='*'/>
        <ColumnDefinition Width='Auto'/>
      </Grid.ColumnDefinitions>
      <ScrollContentPresenter x:Name='PART_ScrollContentPresenter' Grid.Column='0'
          Margin='{TemplateBinding Padding}'
          CanContentScroll='{TemplateBinding CanContentScroll}'
          Content='{TemplateBinding Content}'
          ContentTemplate='{TemplateBinding ContentTemplate}'/>
      <ScrollBar x:Name='PART_VerticalScrollBar' Grid.Column='1' Orientation='Vertical'
          Style='{StaticResource WinScrollBar}'
          Minimum='0' Maximum='{TemplateBinding ScrollableHeight}'
          ViewportSize='{TemplateBinding ViewportHeight}'
          Value='{TemplateBinding VerticalOffset}'
          Visibility='{TemplateBinding ComputedVerticalScrollBarVisibility}'/>
    </Grid>
  </ControlTemplate>

  <Style x:Key='WinScrollViewer' TargetType='ScrollViewer'>
    <Setter Property='Template' Value='{StaticResource WinScrollViewerTemplate}'/>
    <Setter Property='HorizontalScrollBarVisibility' Value='Disabled'/>
  </Style>

  <!-- ============ Button（WinUI 标准）============ -->
  <Style x:Key='WinButton' TargetType='Button'>
    <Setter Property='Background' Value='{StaticResource ControlFill}'/>
    <Setter Property='Foreground' Value='{StaticResource TextPrimary}'/>
    <Setter Property='BorderBrush' Value='{StaticResource ControlStroke}'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='Padding' Value='12,4,12,5'/>
    <Setter Property='MinHeight' Value='32'/>
    <Setter Property='MinWidth' Value='0'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='FontFamily' Value='Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='SnapsToDevicePixels' Value='True'/>
    <Setter Property='HorizontalContentAlignment' Value='Center'/>
    <Setter Property='VerticalContentAlignment' Value='Center'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Grid x:Name='Root' Background='Transparent' RenderTransformOrigin='0.5,0.5'>
            <Grid.RenderTransform>
              <ScaleTransform x:Name='Sc' ScaleX='1' ScaleY='1'/>
            </Grid.RenderTransform>
            <Border x:Name='Bg' Background='{TemplateBinding Background}'
                    BorderBrush='{TemplateBinding BorderBrush}'
                    BorderThickness='{TemplateBinding BorderThickness}'
                    CornerRadius='4'/>
            <!-- hover / press 用叠加层做透明度过渡：比直接换背景色平滑，
                 也不用动画 Brush（Brush 动画开销大且可能碰到冻结实例） -->
            <Border x:Name='Hv' Background='{StaticResource ControlFillHover}'
                    CornerRadius='4' Opacity='0' IsHitTestVisible='False'/>
            <Border x:Name='Pr' Background='{StaticResource ControlFillPressed}'
                    CornerRadius='4' Opacity='0' IsHitTestVisible='False'/>
            <Border x:Name='Edge' CornerRadius='4' Margin='1'
                    BorderThickness='0,0,0,1' BorderBrush='{StaticResource ControlStrokeBottom}'/>
            <ContentPresenter x:Name='Cp' Margin='{TemplateBinding Padding}'
                    HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}'
                    VerticalAlignment='{TemplateBinding VerticalContentAlignment}'
                    RecognizesAccessKey='True'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Trigger.EnterActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName='Hv' Storyboard.TargetProperty='Opacity'
                                     To='1' Duration='0:0:0.12'/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName='Hv' Storyboard.TargetProperty='Opacity'
                                     To='0' Duration='0:0:0.18'/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.ExitActions>
            </Trigger>
            <Trigger Property='IsPressed' Value='True'>
              <Trigger.EnterActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName='Pr' Storyboard.TargetProperty='Opacity'
                                     To='1' Duration='0:0:0.06'/>
                    <DoubleAnimation Storyboard.TargetName='Sc' Storyboard.TargetProperty='ScaleX'
                                     To='0.97' Duration='0:0:0.06'/>
                    <DoubleAnimation Storyboard.TargetName='Sc' Storyboard.TargetProperty='ScaleY'
                                     To='0.97' Duration='0:0:0.06'/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName='Pr' Storyboard.TargetProperty='Opacity'
                                     To='0' Duration='0:0:0.14'/>
                    <DoubleAnimation Storyboard.TargetName='Sc' Storyboard.TargetProperty='ScaleX'
                                     To='1' Duration='0:0:0.14'/>
                    <DoubleAnimation Storyboard.TargetName='Sc' Storyboard.TargetProperty='ScaleY'
                                     To='1' Duration='0:0:0.14'/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.ExitActions>
              <Setter Property='Foreground' Value='{StaticResource TextSecondary}'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter TargetName='Bg' Property='Background' Value='{StaticResource ControlFillDisabled}'/>
              <Setter Property='Foreground' Value='{StaticResource TextDisabled}'/>
              <Setter TargetName='Edge' Property='Opacity' Value='0'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============ Accent Button（强调色）============ -->
  <Style x:Key='WinAccentButton' TargetType='Button' BasedOn='{StaticResource WinButton}'>
    <Setter Property='Background' Value='{StaticResource AccentFill}'/>
    <Setter Property='Foreground' Value='{StaticResource AccentText}'/>
    <Setter Property='BorderBrush' Value='{StaticResource AccentFill}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Grid x:Name='Root' Background='Transparent' RenderTransformOrigin='0.5,0.5'>
            <Grid.RenderTransform>
              <ScaleTransform x:Name='Sc' ScaleX='1' ScaleY='1'/>
            </Grid.RenderTransform>
            <Border x:Name='Bg' Background='{TemplateBinding Background}' CornerRadius='4'/>
            <Border x:Name='Hv' Background='{StaticResource AccentFillHover}'
                    CornerRadius='4' Opacity='0' IsHitTestVisible='False'/>
            <Border x:Name='Pr' Background='{StaticResource AccentFillPressed}'
                    CornerRadius='4' Opacity='0' IsHitTestVisible='False'/>
            <Border x:Name='Edge' CornerRadius='4' Margin='1'
                    BorderThickness='0,0,0,1' BorderBrush='#26000000'/>
            <ContentPresenter x:Name='Cp' Margin='{TemplateBinding Padding}'
                    HorizontalAlignment='Center' VerticalAlignment='Center'
                    RecognizesAccessKey='True'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Trigger.EnterActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName='Hv' Storyboard.TargetProperty='Opacity'
                                     To='1' Duration='0:0:0.12'/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName='Hv' Storyboard.TargetProperty='Opacity'
                                     To='0' Duration='0:0:0.18'/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.ExitActions>
            </Trigger>
            <Trigger Property='IsPressed' Value='True'>
              <Trigger.EnterActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName='Pr' Storyboard.TargetProperty='Opacity'
                                     To='1' Duration='0:0:0.06'/>
                    <DoubleAnimation Storyboard.TargetName='Sc' Storyboard.TargetProperty='ScaleX'
                                     To='0.97' Duration='0:0:0.06'/>
                    <DoubleAnimation Storyboard.TargetName='Sc' Storyboard.TargetProperty='ScaleY'
                                     To='0.97' Duration='0:0:0.06'/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName='Pr' Storyboard.TargetProperty='Opacity'
                                     To='0' Duration='0:0:0.14'/>
                    <DoubleAnimation Storyboard.TargetName='Sc' Storyboard.TargetProperty='ScaleX'
                                     To='1' Duration='0:0:0.14'/>
                    <DoubleAnimation Storyboard.TargetName='Sc' Storyboard.TargetProperty='ScaleY'
                                     To='1' Duration='0:0:0.14'/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.ExitActions>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter TargetName='Bg' Property='Background' Value='#4D000000'/>
              <Setter Property='Foreground' Value='#5C000000'/>
              <Setter TargetName='Edge' Property='Opacity' Value='0'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============ Danger Button ============ -->
  <Style x:Key='WinDangerButton' TargetType='Button' BasedOn='{StaticResource WinButton}'>
    <Setter Property='Background' Value='{StaticResource DangerFill}'/>
    <Setter Property='Foreground' Value='#FFFFFFFF'/>
    <Setter Property='BorderBrush' Value='{StaticResource DangerFill}'/>
  </Style>

  <!-- ============ Subtle Button（无边框，悬停才显底色）============ -->
  <Style x:Key='WinSubtleButton' TargetType='Button' BasedOn='{StaticResource WinButton}'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='BorderBrush' Value='Transparent'/>
    <Setter Property='BorderThickness' Value='0'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Grid x:Name='Root' Background='Transparent'>
            <Border x:Name='Bg' Background='Transparent' CornerRadius='4'/>
            <Border x:Name='Hv' Background='{StaticResource SubtleHover}'
                    CornerRadius='4' Opacity='0' IsHitTestVisible='False'/>
            <Border x:Name='Pr' Background='{StaticResource SubtlePressed}'
                    CornerRadius='4' Opacity='0' IsHitTestVisible='False'/>
            <ContentPresenter Margin='{TemplateBinding Padding}'
                    HorizontalAlignment='Center' VerticalAlignment='Center'
                    RecognizesAccessKey='True'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Trigger.EnterActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName='Hv' Storyboard.TargetProperty='Opacity'
                                     To='1' Duration='0:0:0.12'/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName='Hv' Storyboard.TargetProperty='Opacity'
                                     To='0' Duration='0:0:0.18'/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.ExitActions>
            </Trigger>
            <Trigger Property='IsPressed' Value='True'>
              <Trigger.EnterActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName='Pr' Storyboard.TargetProperty='Opacity'
                                     To='1' Duration='0:0:0.06'/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName='Pr' Storyboard.TargetProperty='Opacity'
                                     To='0' Duration='0:0:0.14'/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.ExitActions>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Foreground' Value='{StaticResource TextDisabled}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============ TextBox（WinUI：底边框，聚焦转强调色）============ -->
  <Style x:Key='WinTextBox' TargetType='TextBox'>
    <Setter Property='Foreground' Value='{StaticResource TextPrimary}'/>
    <Setter Property='Background' Value='{StaticResource ControlFill}'/>
    <Setter Property='BorderBrush' Value='{StaticResource ControlStroke}'/>
    <Setter Property='CaretBrush' Value='{StaticResource TextPrimary}'/>
    <Setter Property='SelectionBrush' Value='{StaticResource AccentFill}'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='FontFamily' Value='Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI'/>
    <Setter Property='MinHeight' Value='32'/>
    <Setter Property='Padding' Value='10,5,10,6'/>
    <Setter Property='SnapsToDevicePixels' Value='True'/>
    <Setter Property='VerticalContentAlignment' Value='Center'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='TextBox'>
          <Grid>
            <Border x:Name='Bg' Background='{TemplateBinding Background}'
                    BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='4'/>
            <Border x:Name='Under' Height='1' VerticalAlignment='Bottom' Margin='1,0,1,0'
                    Background='{StaticResource ControlStrokeBottom}' CornerRadius='0,0,4,4'/>
            <Border x:Name='FocusLine' Height='2' VerticalAlignment='Bottom' Margin='0,0,0,0'
                    Background='{StaticResource AccentFill}' CornerRadius='0,0,4,4' Visibility='Collapsed'/>
            <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}'
                          VerticalAlignment='Center' Focusable='False'
                          HorizontalScrollBarVisibility='Hidden' VerticalScrollBarVisibility='Hidden'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsKeyboardFocusWithin' Value='True'>
              <Setter TargetName='FocusLine' Property='Visibility' Value='Visible'/>
              <Setter TargetName='Under' Property='Visibility' Value='Collapsed'/>
            </Trigger>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='Bg' Property='Background' Value='{StaticResource ControlFill}'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Foreground' Value='{StaticResource TextDisabled}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============ ComboBox ============ -->
  <Style x:Key='WinComboBoxItem' TargetType='ComboBoxItem'>
    <Setter Property='Foreground' Value='{StaticResource TextPrimary}'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='FontFamily' Value='Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI'/>
    <Setter Property='Padding' Value='11,6,11,7'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ComboBoxItem'>
          <Grid x:Name='Root' Background='Transparent' Margin='4,1,4,1'>
            <Border x:Name='Bg' Background='Transparent' CornerRadius='4'/>
            <ContentPresenter Margin='{TemplateBinding Padding}' VerticalAlignment='Center'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsHighlighted' Value='True'>
              <Setter TargetName='Bg' Property='Background' Value='{StaticResource SubtleHover}'/>
            </Trigger>
            <Trigger Property='IsSelected' Value='True'>
              <Setter TargetName='Bg' Property='Background' Value='{StaticResource RowSelected}'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Foreground' Value='{StaticResource TextDisabled}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='WinComboBox' TargetType='ComboBox'>
    <Setter Property='Foreground' Value='{StaticResource TextPrimary}'/>
    <Setter Property='Background' Value='{StaticResource ControlFill}'/>
    <Setter Property='BorderBrush' Value='{StaticResource ControlStroke}'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='FontFamily' Value='Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI'/>
    <Setter Property='MinHeight' Value='32'/>
    <Setter Property='Padding' Value='11,0,30,0'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='SnapsToDevicePixels' Value='True'/>
    <Setter Property='ItemContainerStyle' Value='{StaticResource WinComboBoxItem}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ComboBox'>
          <Grid>
            <ToggleButton x:Name='Toggle' Focusable='False' ClickMode='Press'
                IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>
              <ToggleButton.Template>
                <ControlTemplate TargetType='ToggleButton'>
                  <Grid Background='Transparent'>
                    <Border x:Name='Bg' Background='{StaticResource ControlFill}'
                            BorderBrush='{StaticResource ControlStroke}' BorderThickness='1' CornerRadius='4'/>
                    <Border x:Name='Edge' CornerRadius='4' Margin='1'
                            BorderThickness='0,0,0,1' BorderBrush='{StaticResource ControlStrokeBottom}'/>
                    <TextBlock Text='&#xE70D;' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets'
                               FontSize='10' Foreground='{StaticResource TextSecondary}'
                               HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,1,12,0'/>
                  </Grid>
                  <ControlTemplate.Triggers>
                    <Trigger Property='IsMouseOver' Value='True'>
                      <Setter TargetName='Bg' Property='Background' Value='{StaticResource ControlFillHover}'/>
                    </Trigger>
                    <Trigger Property='IsChecked' Value='True'>
                      <Setter TargetName='Bg' Property='Background' Value='{StaticResource ControlFillPressed}'/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </ToggleButton.Template>
            </ToggleButton>

            <ContentPresenter x:Name='Sel' IsHitTestVisible='False'
                Content='{TemplateBinding SelectionBoxItem}'
                ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}'
                ContentStringFormat='{TemplateBinding SelectionBoxItemStringFormat}'
                Margin='{TemplateBinding Padding}' VerticalAlignment='Center'
                HorizontalAlignment='Left'/>

            <Popup x:Name='PART_Popup' AllowsTransparency='True' Focusable='False'
                   IsOpen='{TemplateBinding IsDropDownOpen}'
                   Placement='Bottom' VerticalOffset='4'
                   PopupAnimation='Fade'>
              <Border Background='{StaticResource FlyoutFill}'
                      BorderBrush='{StaticResource FlyoutStroke}' BorderThickness='1'
                      CornerRadius='8' Margin='0,0,8,8'
                      MinWidth='{TemplateBinding ActualWidth}'
                      MaxHeight='{TemplateBinding MaxDropDownHeight}'>
                <Border.Effect>
                  <DropShadowEffect BlurRadius='18' ShadowDepth='3' Opacity='0.16' Color='#000000'/>
                </Border.Effect>
                <ScrollViewer Margin='0,4,0,4' Style='{StaticResource WinScrollViewer}'>
                  <ItemsPresenter/>
                </ScrollViewer>
              </Border>
            </Popup>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============ ListViewItem（悬停/选中底色 + 左侧强调 pill）============ -->
  <Style x:Key='WinListViewItem' TargetType='ListViewItem'>
    <Setter Property='Foreground' Value='{StaticResource TextPrimary}'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='FontFamily' Value='Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI'/>
    <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
    <Setter Property='Padding' Value='0'/>
    <Setter Property='Margin' Value='4,1,4,1'/>
    <Setter Property='SnapsToDevicePixels' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ListViewItem'>
          <Grid x:Name='Root' Background='Transparent'>
            <Border x:Name='Bg' Background='Transparent' CornerRadius='4'/>
            <Border x:Name='Hv' Background='{StaticResource RowHover}'
                    CornerRadius='4' Opacity='0' IsHitTestVisible='False'/>
            <Border x:Name='Sel' Background='{StaticResource RowSelected}'
                    CornerRadius='4' Opacity='0' IsHitTestVisible='False'/>
            <Border x:Name='Pill' Width='3' Height='16' CornerRadius='1.5'
                    Background='{StaticResource AccentFill}'
                    HorizontalAlignment='Left' VerticalAlignment='Center' Opacity='0'/>
            <ContentPresenter x:Name='Cp' Margin='0' VerticalAlignment='Center'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Trigger.EnterActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName='Hv' Storyboard.TargetProperty='Opacity'
                                     To='1' Duration='0:0:0.11'/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName='Hv' Storyboard.TargetProperty='Opacity'
                                     To='0' Duration='0:0:0.16'/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.ExitActions>
            </Trigger>
            <Trigger Property='IsSelected' Value='True'>
              <Setter TargetName='Sel' Property='Opacity' Value='1'/>
              <Setter TargetName='Pill' Property='Opacity' Value='1'/>
              <Trigger.EnterActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName='Sel' Storyboard.TargetProperty='Opacity'
                                     To='1' Duration='0:0:0.14'/>
                    <DoubleAnimation Storyboard.TargetName='Pill' Storyboard.TargetProperty='Opacity'
                                     To='1' Duration='0:0:0.14'/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName='Sel' Storyboard.TargetProperty='Opacity'
                                     To='0' Duration='0:0:0.14'/>
                    <DoubleAnimation Storyboard.TargetName='Pill' Storyboard.TargetProperty='Opacity'
                                     To='0' Duration='0:0:0.14'/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.ExitActions>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='WinListView' TargetType='ListView'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='BorderThickness' Value='0'/>
    <Setter Property='Padding' Value='0'/>
    <Setter Property='ItemContainerStyle' Value='{StaticResource WinListViewItem}'/>
    <!-- CanContentScroll 必须为 True：它同时是「UI 虚拟化」的开关。
         设 False 走像素滚动，VirtualizingStackPanel 会退化成普通 StackPanel，
         清单几百项时全部实例化 —— 这是之前管理台卡顿的主因。 -->
    <Setter Property='ScrollViewer.CanContentScroll' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ListView'>
          <ScrollViewer Focusable='False' CanContentScroll='True'
                        HorizontalScrollBarVisibility='Disabled'
                        VerticalScrollBarVisibility='Auto'
                        Template='{StaticResource WinScrollViewerTemplate}'>
            <ItemsPresenter/>
          </ScrollViewer>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============ ListBoxItem / ListBox ============ -->
  <Style x:Key='WinListBoxItem' TargetType='ListBoxItem'>
    <Setter Property='Foreground' Value='{StaticResource TextPrimary}'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='FontFamily' Value='Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI'/>
    <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
    <Setter Property='Padding' Value='0'/>
    <Setter Property='Margin' Value='4,1,4,1'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ListBoxItem'>
          <Grid x:Name='Root' Background='Transparent'>
            <Border x:Name='Bg' Background='Transparent' CornerRadius='4'/>
            <Border x:Name='Pill' Width='3' Height='16' CornerRadius='1.5'
                    Background='{StaticResource AccentFill}'
                    HorizontalAlignment='Left' VerticalAlignment='Center' Opacity='0'/>
            <ContentPresenter Margin='0' VerticalAlignment='Center'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='Bg' Property='Background' Value='{StaticResource RowHover}'/>
            </Trigger>
            <Trigger Property='IsSelected' Value='True'>
              <Setter TargetName='Bg' Property='Background' Value='{StaticResource RowSelected}'/>
              <Setter TargetName='Pill' Property='Opacity' Value='1'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='WinListBox' TargetType='ListBox'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='BorderThickness' Value='0'/>
    <Setter Property='Padding' Value='0'/>
    <Setter Property='ItemContainerStyle' Value='{StaticResource WinListBoxItem}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ListBox'>
          <ScrollViewer Focusable='False' CanContentScroll='False'
                        HorizontalScrollBarVisibility='Disabled'
                        VerticalScrollBarVisibility='Auto'
                        Template='{StaticResource WinScrollViewerTemplate}'>
            <ItemsPresenter/>
          </ScrollViewer>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============ ToggleSwitch（WinUI 胶囊开关）============ -->
  <Style x:Key='WinToggle' TargetType='CheckBox'>
    <Setter Property='Foreground' Value='{StaticResource TextPrimary}'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='FontFamily' Value='Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <Grid Background='Transparent'>
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width='Auto'/>
              <ColumnDefinition Width='*'/>
            </Grid.ColumnDefinitions>
            <Grid Width='40' Height='20' VerticalAlignment='Center'>
              <Border x:Name='Rail' CornerRadius='10' Background='Transparent'
                      BorderBrush='{StaticResource ControlStrokeBottom}' BorderThickness='1'/>
              <Border x:Name='Fill' CornerRadius='10' Background='{StaticResource AccentFill}' Opacity='0'/>
              <Border x:Name='Knob' Width='12' Height='12' CornerRadius='6'
                      Background='{StaticResource TextTertiary}'
                      HorizontalAlignment='Left' VerticalAlignment='Center' Margin='4,0,0,0'/>
            </Grid>
            <ContentPresenter Grid.Column='1' Margin='12,0,0,0' VerticalAlignment='Center'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <!-- 用 Margin 动画（ThicknessAnimation）而不是 RenderTransform：
                   Setter 的 TargetName 只能指向 FrameworkElement，指 Freezable（如
                   TranslateTransform）会让整个模板解析失败，所有样式静默丢失。
                   Setter 兜底「初始就是开」的情况，动画负责状态切换的过渡。 -->
              <Setter TargetName='Knob' Property='Margin' Value='24,0,0,0'/>
              <Setter TargetName='Fill' Property='Opacity' Value='1'/>
              <Setter TargetName='Knob' Property='Background' Value='#FFFFFFFF'/>
              <Trigger.EnterActions>
                <BeginStoryboard>
                  <Storyboard>
                    <ThicknessAnimation Storyboard.TargetName='Knob' Storyboard.TargetProperty='Margin'
                                        To='24,0,0,0' Duration='0:0:0.16'>
                      <ThicknessAnimation.EasingFunction>
                        <CubicEase EasingMode='EaseOut'/>
                      </ThicknessAnimation.EasingFunction>
                    </ThicknessAnimation>
                    <DoubleAnimation Storyboard.TargetName='Fill' Storyboard.TargetProperty='Opacity'
                                     To='1' Duration='0:0:0.14'/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                <BeginStoryboard>
                  <Storyboard>
                    <ThicknessAnimation Storyboard.TargetName='Knob' Storyboard.TargetProperty='Margin'
                                        To='4,0,0,0' Duration='0:0:0.16'>
                      <ThicknessAnimation.EasingFunction>
                        <CubicEase EasingMode='EaseOut'/>
                      </ThicknessAnimation.EasingFunction>
                    </ThicknessAnimation>
                    <DoubleAnimation Storyboard.TargetName='Fill' Storyboard.TargetProperty='Opacity'
                                     To='0' Duration='0:0:0.14'/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.ExitActions>
            </Trigger>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='Rail' Property='BorderBrush' Value='{StaticResource TextTertiary}'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.45'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============ ToolTip ============ -->
  <Style x:Key='WinToolTip' TargetType='ToolTip'>
    <Setter Property='Foreground' Value='#FFFFFFFF'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='FontFamily' Value='Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ToolTip'>
          <Border Background='#E6303030' CornerRadius='4' Padding='9,5,9,6'
                  BorderBrush='#1AFFFFFF' BorderThickness='1'>
            <ContentPresenter/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

</ResourceDictionary>";

    private static ResourceDictionary Build()
    {
        try
        {
            // 色板在解析前注入（模板里的 StaticResource 才会拿到对应主题的画刷）
            string xaml = Xaml;
            int rootEnd = xaml.IndexOf('>');
            if (rootEnd > 0)
            {
                xaml = xaml.Substring(0, rootEnd + 1) + "\r\n" + PaletteXaml() + xaml.Substring(rootEnd + 1);
            }
            return (ResourceDictionary)XamlReader.Parse(xaml);
        }
        catch (Exception ex)
        {
            // 资源加载失败不能让窗口打不开：返回空字典，控件退回系统默认外观。
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + " WInUi xaml: " + ex.Message + "\r\n");
            }
            catch { }
            return new ResourceDictionary();
        }
    }

    // ---------------- 控件工厂 ----------------
    public static Button Btn(string styleKey, string text)
    {
        Button b = new Button();
        b.Content = text;
        Style s = S(styleKey);
        if (s != null) { b.Style = s; }
        b.FontSize = SizeBody;
        return b;
    }

    public static TextBlock Text(string text, double size, Brush color)
    {
        TextBlock t = new TextBlock();
        t.Text = text;
        t.FontSize = size;
        t.Foreground = color;
        t.TextWrapping = TextWrapping.NoWrap;
        t.VerticalAlignment = VerticalAlignment.Center;
        return t;
    }

    public static TextBlock Glyph(string glyph, double size, Brush color)
    {
        TextBlock t = new TextBlock();
        t.Text = glyph;
        t.FontFamily = new FontFamily(FontIcon);
        t.FontSize = size;
        t.Foreground = color;
        t.VerticalAlignment = VerticalAlignment.Center;
        t.HorizontalAlignment = HorizontalAlignment.Center;
        return t;
    }

    /// <summary>WinUI 卡片：圆角 8、半透明白、1px 细描边、无投影。</summary>
    public static Border Card(UIElement inner, Thickness pad)
    {
        Border b = new Border();
        b.Background = B("CardFill");
        b.BorderBrush = B("CardStroke");
        b.BorderThickness = new Thickness(1);
        b.CornerRadius = new CornerRadius(8);
        b.Padding = pad;
        b.SnapsToDevicePixels = true;
        if (inner != null) { b.Child = inner; }
        return b;
    }

    /// <summary>
    /// 读 ico 里像素最大的一帧。
    /// WPF 的 BitmapImage 默认只取第一帧（通常 16px），放大看就是糊的；
    /// 这里用 BitmapDecoder 遍历所有帧取最大，用于「图标预览」。
    /// </summary>
    public static BitmapSource LoadIcoLargest(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { return null; }

            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                BitmapDecoder dec = BitmapDecoder.Create(
                    fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

                BitmapFrame best = null;
                foreach (BitmapFrame f in dec.Frames)
                {
                    if (best == null || f.PixelWidth > best.PixelWidth) { best = f; }
                }
                if (best == null) { return null; }
                return best;
            }
        }
        catch { return null; }
    }

    /// <summary>ico 里有几档尺寸（诊断用）。</summary>
    public static int CountIcoFrames(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { return 0; }
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                BitmapDecoder dec = BitmapDecoder.Create(
                    fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                return dec.Frames.Count;
            }
        }
        catch { return 0; }
    }

    /// <summary>目录占用的字节数与前缀匹配的文件数（不递归）。</summary>
    public static void MeasureDir(string dir, out int files, out long bytes)
    {
        files = 0;
        bytes = 0;
        try
        {
            if (!Directory.Exists(dir)) { return; }
            foreach (string f in Directory.GetFiles(dir))
            {
                try
                {
                    files++;
                    bytes += new FileInfo(f).Length;
                }
                catch { }
            }
        }
        catch { }
    }
}

// =====================================================================
//  ContentDialog 风格的对话框
//  不用系统 MessageBox —— 那个是 Win32 老式对话框，一弹出来就把整窗的
//  WinUI 观感破掉。这里自绘：烟雾遮罩 + 圆角卡片 + 底部对齐按钮。
// =====================================================================
internal static class WinUiDialog
{
    public static bool Confirm(Window owner, string title, string message)
    {
        DialogWindow d = new DialogWindow(owner, title, message, "确定", "取消");
        return d.ShowDialog() == true;
    }

    public static bool Confirm(Window owner, string title, string message, string primary)
    {
        DialogWindow d = new DialogWindow(owner, title, message, primary, "取消");
        return d.ShowDialog() == true;
    }

    public static void Info(Window owner, string title, string message)
    {
        new DialogWindow(owner, title, message, "确定", null).ShowDialog();
    }
}

internal sealed class DialogWindow : Window
{
    public DialogWindow(Window owner, string title, string message, string primary, string secondary)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily(WinUi.FontUi);
        FontSize = WinUi.SizeBody;
        if (owner != null)
        {
            Owner = owner;
            // 铺满宿主窗口，好让遮罩盖住整窗
            Left = owner.Left;
            Top = owner.Top;
            Width = owner.ActualWidth;
            Height = owner.ActualHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
        }
        else
        {
            Width = 460;
            Height = 320;
        }

        Grid root = new Grid();

        Border scrim = new Border();
        scrim.Background = new SolidColorBrush(Color.FromArgb(0x66, 0x00, 0x00, 0x00));
        root.Children.Add(scrim);

        Border card = new Border();
        card.Width = 448;
        card.Background = new SolidColorBrush(Colors.White);
        card.CornerRadius = new CornerRadius(8);
        card.Padding = new Thickness(24, 22, 24, 20);
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.VerticalAlignment = VerticalAlignment.Center;
        card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x14, 0, 0, 0));
        card.BorderThickness = new Thickness(1);

        DropShadowEffect sh = new DropShadowEffect();
        sh.BlurRadius = 32;
        sh.ShadowDepth = 8;
        sh.Opacity = 0.22;
        sh.Color = Colors.Black;
        card.Effect = sh;

        StackPanel body = new StackPanel();

        TextBlock t = new TextBlock();
        t.Text = title;
        t.FontSize = 20;
        t.FontWeight = FontWeights.SemiBold;
        t.Foreground = WinUi.B("TextPrimary");
        t.TextWrapping = TextWrapping.Wrap;
        body.Children.Add(t);

        TextBlock m = new TextBlock();
        m.Text = message;
        m.FontSize = WinUi.SizeBody;
        m.Foreground = WinUi.B("TextSecondary");
        m.TextWrapping = TextWrapping.Wrap;
        m.LineHeight = 21;
        m.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        m.Margin = new Thickness(0, 12, 0, 0);
        body.Children.Add(m);

        StackPanel btns = new StackPanel();
        btns.Orientation = Orientation.Horizontal;
        btns.HorizontalAlignment = HorizontalAlignment.Right;
        btns.Margin = new Thickness(0, 24, 0, 0);

        if (!string.IsNullOrEmpty(secondary))
        {
            Button b2 = WinUi.Btn("WinButton", secondary);
            b2.MinWidth = 92;
            b2.Margin = new Thickness(0, 0, 8, 0);
            b2.Click += delegate { DialogResult = false; };
            btns.Children.Add(b2);
        }

        Button b1 = WinUi.Btn("WinAccentButton", primary);
        b1.MinWidth = 92;
        b1.Click += delegate { DialogResult = true; };
        btns.Children.Add(b1);

        body.Children.Add(btns);
        card.Child = body;
        root.Children.Add(card);

        scrim.MouseLeftButtonUp += delegate
        {
            if (!string.IsNullOrEmpty(secondary)) { DialogResult = false; }
        };

        Content = root;

        KeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape && !string.IsNullOrEmpty(secondary))
            {
                DialogResult = false;
            }
            else if (e.Key == System.Windows.Input.Key.Enter)
            {
                DialogResult = true;
            }
        };

        Loaded += delegate
        {
            try { b1.Focus(); }
            catch { }
        };
    }
}

/// <summary>
/// 统一的动效工具：给用户操作一点「有生命」的反馈，但保持 WinUI 的克制。
/// 全部用 WPF Storyboard（不依赖 Windows App SDK），动画时长短、缓动柔和，
/// 且尊重系统的「减少动画」设置（关掉时立即呈现最终状态）。
/// </summary>
internal static class WinMotion
{
    private static bool Reduced
    {
        get
        {
            try
            {
                return System.Windows.SystemParameters.ClientAreaAnimation == false;
            }
            catch { return false; }
        }
    }

    private static System.Windows.Media.Animation.EasingFunctionBase Ease()
    {
        System.Windows.Media.Animation.CubicEase e = new System.Windows.Media.Animation.CubicEase();
        e.EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut;
        return e;
    }

    /// <summary>淡入 + 从下往上轻微滑入。页面切换、内容出现用。</summary>
    public static void SlideFadeIn(UIElement el, double fromY, int durationMs)
    {
        if (el == null) { return; }
        try
        {
            // 先清掉上一次动画对属性的持有，否则重复切换页时新动画不生效（WPF 常见坑）
            el.BeginAnimation(UIElement.OpacityProperty, null);
            el.Opacity = 0;
            el.RenderTransform = new System.Windows.Media.TranslateTransform(0, fromY);

            if (Reduced)
            {
                el.Opacity = 1;
                el.RenderTransform = null;
                return;
            }

            System.Windows.Media.Animation.Storyboard sb = new System.Windows.Media.Animation.Storyboard();
            System.Windows.Media.Animation.DoubleAnimation fade =
                new System.Windows.Media.Animation.DoubleAnimation(0, 1,
                    System.TimeSpan.FromMilliseconds(durationMs));
            fade.EasingFunction = Ease();
            System.Windows.Media.Animation.Storyboard.SetTarget(fade, el);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(fade,
                new System.Windows.PropertyPath(UIElement.OpacityProperty));
            sb.Children.Add(fade);

            System.Windows.Media.Animation.DoubleAnimation move =
                new System.Windows.Media.Animation.DoubleAnimation(fromY, 0,
                    System.TimeSpan.FromMilliseconds(durationMs));
            move.EasingFunction = Ease();
            System.Windows.Media.Animation.Storyboard.SetTarget(move, el);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(move,
                new System.Windows.PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
            sb.Children.Add(move);

            sb.Begin((System.Windows.FrameworkElement)el);
        }
        catch
        {
            try { el.Opacity = 1; el.RenderTransform = null; } catch { }
        }
    }

    /// <summary>纯淡入。轻量元素出现用。</summary>
    public static void FadeIn(UIElement el, int durationMs)
    {
        if (el == null) { return; }
        try
        {
            if (Reduced) { el.Opacity = 1; return; }
            el.BeginAnimation(UIElement.OpacityProperty, null);
            el.Opacity = 0;
            System.Windows.Media.Animation.DoubleAnimation fade =
                new System.Windows.Media.Animation.DoubleAnimation(0, 1,
                    System.TimeSpan.FromMilliseconds(durationMs));
            fade.EasingFunction = Ease();
            el.BeginAnimation(UIElement.OpacityProperty, fade);
        }
        catch { try { el.Opacity = 1; } catch { } }
    }

    /// <summary>窗口打开：整体轻微放大淡入。</summary>
    public static void WindowOpen(Window win)
    {
        if (win == null) { return; }
        try
        {
            if (Reduced) { return; }
            win.Opacity = 0;
            System.Windows.Media.ScaleTransform sc = new System.Windows.Media.ScaleTransform(0.96, 0.96);
            sc.CenterX = win.Width / 2;
            sc.CenterY = win.Height / 2;
            win.RenderTransform = sc;

            System.Windows.Media.Animation.Storyboard sb = new System.Windows.Media.Animation.Storyboard();
            System.Windows.Media.Animation.DoubleAnimation fade =
                new System.Windows.Media.Animation.DoubleAnimation(0, 1,
                    System.TimeSpan.FromMilliseconds(180));
            fade.EasingFunction = Ease();
            System.Windows.Media.Animation.Storyboard.SetTarget(fade, win);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(fade,
                new System.Windows.PropertyPath(Window.OpacityProperty));
            sb.Children.Add(fade);

            System.Windows.Media.Animation.DoubleAnimation sx =
                new System.Windows.Media.Animation.DoubleAnimation(0.96, 1,
                    System.TimeSpan.FromMilliseconds(180));
            sx.EasingFunction = Ease();
            System.Windows.Media.Animation.Storyboard.SetTarget(sx, win);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(sx,
                new System.Windows.PropertyPath("(Window.RenderTransform).(ScaleTransform.ScaleX)"));
            sb.Children.Add(sx);

            System.Windows.Media.Animation.DoubleAnimation sy =
                new System.Windows.Media.Animation.DoubleAnimation(0.96, 1,
                    System.TimeSpan.FromMilliseconds(180));
            sy.EasingFunction = Ease();
            System.Windows.Media.Animation.Storyboard.SetTarget(sy, win);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(sy,
                new System.Windows.PropertyPath("(Window.RenderTransform).(ScaleTransform.ScaleY)"));
            sb.Children.Add(sy);

            sb.Begin(win);
        }
        catch { try { win.Opacity = 1; win.RenderTransform = null; } catch { } }
    }

    /// <summary>按钮按下/悬停的轻微缩放反馈，绑在 Border 上。</summary>
    public static void PressFeedback(Border target)
    {
        if (target == null) { return; }
        try
        {
            target.MouseLeftButtonDown += delegate
            {
                if (Reduced) { return; }
                System.Windows.Media.ScaleTransform sc = new System.Windows.Media.ScaleTransform(1, 1);
                target.RenderTransform = sc;
                target.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
                System.Windows.Media.Animation.DoubleAnimation a =
                    new System.Windows.Media.Animation.DoubleAnimation(1, 0.96,
                        System.TimeSpan.FromMilliseconds(80));
                a.EasingFunction = Ease();
                sc.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, a);
                sc.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, a);
            };
            target.MouseLeftButtonUp += delegate
            {
                if (Reduced) { return; }
                System.Windows.Media.ScaleTransform sc = target.RenderTransform as System.Windows.Media.ScaleTransform;
                if (sc == null) { return; }
                System.Windows.Media.Animation.DoubleAnimation a =
                    new System.Windows.Media.Animation.DoubleAnimation(0.96, 1,
                        System.TimeSpan.FromMilliseconds(120));
                a.EasingFunction = Ease();
                sc.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, a);
                sc.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, a);
            };
        }
        catch { }
    }
}

/// <summary>
/// 平滑滚动：把滚轮的「瞬时跳 3 行」换成 160ms 缓动滑动。
/// 窗口级 PreviewMouseWheel 统一处理 —— 从事件源向上找第一个能滚的 ScrollViewer，
/// 这样列表、日志、设置页、对话框全都自动生效，不用逐个挂。
/// 注意 CanContentScroll=true（虚拟化列表）时 VerticalOffset 的单位是「项」，
/// 步长要按项算，否则会跳得离谱。
/// </summary>
internal static class SmoothScroll
{
    // 参与动画的「当前偏移」：动画每帧推进它，回调里真正滚动
    private static readonly DependencyProperty OffsetProperty =
        DependencyProperty.RegisterAttached("Offset", typeof(double), typeof(SmoothScroll),
            new PropertyMetadata(0.0, OnOffsetChanged));

    // 上一次的目标偏移（不参与动画）：连续滚轮时从它继续累加，不会被打断后跳回
    private static readonly DependencyProperty GoalProperty =
        DependencyProperty.RegisterAttached("Goal", typeof(double), typeof(SmoothScroll),
            new PropertyMetadata(0.0));

    private static void OnOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ScrollViewer sv = d as ScrollViewer;
        if (sv == null) { return; }
        try { sv.ScrollToVerticalOffset((double)e.NewValue); }
        catch { }
    }

    /// <summary>挂到窗口/根元素上一次即可，内部所有滚动区域都生效</summary>
    public static void Enable(UIElement root)
    {
        if (root == null) { return; }
        try
        {
            root.PreviewMouseWheel += delegate(object sender, System.Windows.Input.MouseWheelEventArgs e)
            {
                try
                {
                    if (e.Handled) { return; }
                    if (System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.None)
                    { return; }

                    ScrollViewer sv = FindScrollViewer(e.OriginalSource as DependencyObject);
                    if (sv == null) { return; }
                    if (sv.ScrollableHeight <= 0) { return; }

                    // 内容不足一屏、或已经滚到边界且继续往外滚 → 交给默认行为
                    bool canUp = sv.VerticalOffset > 0.5;
                    bool canDown = sv.VerticalOffset < sv.ScrollableHeight - 0.5;
                    if (e.Delta > 0 && !canUp) { return; }
                    if (e.Delta < 0 && !canDown) { return; }

                    // 步长统一按「像素」算。列表已设 ScrollUnit.Pixel（见 ManagerWindow），
                    // 所以 VirtualizingStackPanel 也能逐像素滚动 —— 若按项滚动，动画必然一顿一顿。
                    int lines = System.Windows.SystemParameters.WheelScrollLines;
                    if (lines <= 0) { lines = 3; }
                    double step = lines * 36.0;

                    // 连续滚轮：从「上一次的目标」继续累加；距实际位置太远说明换了滚动区，重新取当前
                    double goal = (double)sv.GetValue(GoalProperty);
                    double actual = sv.VerticalOffset;
                    if (double.IsNaN(goal) || Math.Abs(goal - actual) > step * 6)
                    {
                        goal = actual;
                    }

                    double target = goal + (e.Delta > 0 ? -step : step);
                    if (target < 0) { target = 0; }
                    if (target > sv.ScrollableHeight) { target = sv.ScrollableHeight; }
                    if (Math.Abs(target - actual) < 0.5 && Math.Abs(target - goal) < 0.5) { return; }

                    e.Handled = true;
                    sv.SetValue(GoalProperty, target);

                    // 关键：From 必须显式给「当前实际位置」。
                    // 之前只给 To，而基础值又被设成 To，动画等于从 To 到 To —— 完全没动，
                    // 每次滚轮都是瞬移一步，看起来就是「一顿一顿」。
                    System.Windows.Media.Animation.DoubleAnimation a =
                        new System.Windows.Media.Animation.DoubleAnimation(
                            actual, target, System.TimeSpan.FromMilliseconds(220));
                    System.Windows.Media.Animation.CubicEase ease =
                        new System.Windows.Media.Animation.CubicEase();
                    ease.EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut;
                    a.EasingFunction = ease;
                    a.FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop;
                    // 基础值也设成目标：动画结束（FillBehavior.Stop）时不会回弹到 0
                    sv.SetValue(OffsetProperty, target);
                    sv.BeginAnimation(OffsetProperty, a);
                }
                catch { }
            };
        }
        catch { }
    }

    private static ScrollViewer FindScrollViewer(DependencyObject node)
    {
        int guard = 0;
        while (node != null && guard++ < 64)
        {
            ScrollViewer sv = node as ScrollViewer;
            if (sv != null) { return sv; }
            node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        }
        return null;
    }
}

/// <summary>
/// 更新进度窗口：下载安装包时实时显示进度条与状态文字。
/// 样式沿用 DialogWindow 的遮罩 + 白卡片，居中盖在宿主窗口上。
/// 用 Show() 非模态显示，下载线程通过 SetProgress 回 UI 线程更新。
/// </summary>
internal sealed class UpdateProgressWindow : Window
{
    private readonly TextBlock statusText;
    private readonly TextBlock percentText;
    private readonly System.Windows.Controls.ProgressBar bar;

    public UpdateProgressWindow(Window owner, string version)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        FontFamily = new FontFamily(WinUi.FontUi);
        FontSize = WinUi.SizeBody;

        if (owner != null)
        {
            Owner = owner;
            Left = owner.Left;
            Top = owner.Top;
            Width = owner.ActualWidth;
            Height = owner.ActualHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
        }
        else
        {
            Width = 460;
            Height = 320;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        Grid root = new Grid();
        Border scrim = new Border();
        scrim.Background = new SolidColorBrush(Color.FromArgb(0x66, 0x00, 0x00, 0x00));
        root.Children.Add(scrim);

        Border card = new Border();
        card.Width = 448;
        card.Background = new SolidColorBrush(Colors.White);
        card.CornerRadius = new CornerRadius(8);
        card.Padding = new Thickness(24, 22, 24, 22);
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.VerticalAlignment = VerticalAlignment.Center;
        card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x14, 0, 0, 0));
        card.BorderThickness = new Thickness(1);
        DropShadowEffect sh = new DropShadowEffect();
        sh.BlurRadius = 32;
        sh.ShadowDepth = 8;
        sh.Opacity = 0.22;
        sh.Color = Colors.Black;
        card.Effect = sh;

        StackPanel body = new StackPanel();

        TextBlock t = new TextBlock();
        t.Text = "正在更新到 " + version;
        t.FontSize = 20;
        t.FontWeight = FontWeights.SemiBold;
        t.Foreground = WinUi.B("TextPrimary");
        body.Children.Add(t);

        statusText = new TextBlock();
        statusText.Text = "正在连接 GitHub…";
        statusText.FontSize = WinUi.SizeBody;
        statusText.Foreground = WinUi.B("TextSecondary");
        statusText.Margin = new Thickness(0, 14, 0, 0);
        statusText.TextWrapping = TextWrapping.Wrap;
        body.Children.Add(statusText);

        bar = new System.Windows.Controls.ProgressBar();
        bar.Height = 6;
        bar.Minimum = 0;
        bar.Maximum = 100;
        bar.Value = 0;
        bar.IsIndeterminate = false;
        bar.Foreground = WinUi.B("AccentFill");
        bar.Background = WinUi.B("DividerStroke");
        bar.Margin = new Thickness(0, 18, 0, 0);
        body.Children.Add(bar);

        percentText = new TextBlock();
        percentText.Text = "0%";
        percentText.FontSize = WinUi.SizeCaption;
        percentText.Foreground = WinUi.B("TextTertiary");
        percentText.HorizontalAlignment = HorizontalAlignment.Right;
        percentText.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(percentText);

        TextBlock note = new TextBlock();
        note.Text = "下载完成后会自动安装并重启程序，期间窗口可能关闭。";
        note.FontSize = WinUi.SizeCaption;
        note.Foreground = WinUi.B("TextTertiary");
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(0, 16, 0, 0);
        body.Children.Add(note);

        card.Child = body;
        root.Children.Add(card);
        Content = root;

        WinMotion.FadeIn(card, 160);
    }

    /// <summary>由下载线程调用，内部切回 UI 线程更新进度条与文字</summary>
    public void SetProgress(int percent, string status)
    {
        try
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(delegate { SetProgress(percent, status); }));
                return;
            }
            bar.Value = percent;
            percentText.Text = percent + "%";
            if (!string.IsNullOrEmpty(status)) { statusText.Text = status; }
        }
        catch { }
    }
}
