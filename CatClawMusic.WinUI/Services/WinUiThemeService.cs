using CatClawMusic.Core.Interfaces;
using CatClawMusic.WinUI.Infrastructure;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace CatClawMusic.WinUI.Services;

/// <summary>
/// WinUI 端主题服务。对照 MAUI 的 <c>Services/ThemeService.cs</c>：
/// 现行主题是单一「樱粉」（与 MAUI 的 <c>ThemeMap</c> 一致），深浅两套令牌由
/// <c>Themes/Tokens.xaml</c> 定义，这里只改颜色值与窗口背景，不重建视图。
/// </summary>
public sealed class WinUiThemeService : IThemeService
{
    private const string KeyTheme = "theme_index";
    private const string KeyDarkMode = "dark_mode";
    private const string KeyCustomBgPath = "custom_bg_path";
    private const string KeyCustomBgOpacity = "custom_bg_opacity";
    private const string KeyFrostedBg = "frosted_bg_enabled";
    private const string KeyMonetBg = "monet_bg_enabled";
    private const string KeyCoverBg = "cover_bg_enabled";

    /// <summary>
    /// 主调色板。2026-11-20 与 Android 端对齐（core/CatClawTheme.kt）：
    /// PINK_PRIMARY=#8FA3CC、PINK_LIGHT=#DDE4F2、PINK_DARK=#9FB4D9。
    /// 名字保留 Pink 只是为了不改动别处的引用；实际是冷灰蓝。
    /// </summary>
    private static readonly ThemePalette Pink = new("#8FA3CC", "#DDE4F2", "#9FB4D9");

    private static readonly Color[] DarkBackgroundStops =
    [
        ParseColor("#10141A"),
        ParseColor("#151A23"),
        ParseColor("#0E1217"),
    ];

    private static readonly Color[] LightBackgroundStops =
    [
        ParseColor("#F7F8FC"),
        ParseColor("#EFF2F9"),
        ParseColor("#E6EAF4"),
    ];

    private readonly JsonSettingsStore _settings;

    private AppTheme _currentTheme;
    private DarkModeSetting _darkModeSetting;
    private string? _customBackgroundPath;
    private double _customBackgroundOpacity = 0.5;
    private bool _frostedBackgroundEnabled = true;
    private bool _monetBackgroundEnabled;
    private bool _coverBackgroundEnabled;
    private FrameworkElement? _root;

    public WinUiThemeService(JsonSettingsStore settings)
    {
        _settings = settings;
        _currentTheme = (AppTheme)_settings.Get(KeyTheme, (int)AppTheme.Pink);
        _darkModeSetting = (DarkModeSetting)_settings.Get(KeyDarkMode, (int)DarkModeSetting.FollowSystem);
        _customBackgroundPath = _settings.Get<string?>(KeyCustomBgPath, null);
        _customBackgroundOpacity = _settings.Get(KeyCustomBgOpacity, 0.5);
        _frostedBackgroundEnabled = _settings.Get(KeyFrostedBg, true);
        _monetBackgroundEnabled = _settings.Get(KeyMonetBg, false);
        _coverBackgroundEnabled = _settings.Get(KeyCoverBg, false);
    }

    public AppTheme CurrentTheme => _currentTheme;

    public DarkModeSetting DarkModeSetting => _darkModeSetting;

    public List<AppTheme> AvailableThemes => [AppTheme.Pink];

    public string? CustomBackgroundPath => _customBackgroundPath;

    public double CustomBackgroundOpacity => _customBackgroundOpacity;

    public bool HasCustomBackground => !string.IsNullOrWhiteSpace(_customBackgroundPath);

    public bool FrostedBackgroundEnabled => _frostedBackgroundEnabled;

    public bool MonetBackgroundEnabled => _monetBackgroundEnabled;

    public bool CoverBackgroundEnabled => _coverBackgroundEnabled;

    public event Action? Applied;

    /// <summary>当前实际是否为深色（壳层用它同步标题栏按钮颜色）</summary>
    public bool IsEffectivelyDarkNow => IsEffectivelyDark();

    /// <summary>把主题应用到窗口根元素（窗口创建后调用一次）</summary>
    public void Attach(FrameworkElement root)
    {
        _root = root;
        ApplyTheme();
    }

    public void SetTheme(AppTheme theme)
    {
        if (_currentTheme == theme)
            return;

        _currentTheme = theme;
        _settings.Set(KeyTheme, (int)theme);
        ApplyTheme();
    }

    public void SetDarkModeSetting(DarkModeSetting setting)
    {
        if (_darkModeSetting == setting)
            return;

        _darkModeSetting = setting;
        _settings.Set(KeyDarkMode, (int)setting);
        ApplyTheme();
    }

    public void ApplyTheme()
    {
        var dark = IsEffectivelyDark();

        if (_root is not null)
        {
            // WinUI 3 运行期不能改 Application.RequestedTheme，只能改根元素的 RequestedTheme。
            _root.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;

            if (_root is Panel panel)
                panel.Background = CreateBackgroundBrush(dark);
        }

        ApplyTokens(dark);
        Applied?.Invoke();
    }

    public bool IsSystemDarkMode()
    {
        try
        {
            var background = new UISettings().GetColorValue(UIColorType.Background);
            return background.R + background.G + background.B < 3 * 128;
        }
        catch
        {
            return false;
        }
    }

    public bool IsEffectivelyDark() => _darkModeSetting switch
    {
        DarkModeSetting.Light => false,
        DarkModeSetting.Dark => true,
        _ => IsSystemDarkMode(),
    };

    public void SetCustomBackground(string? imagePath, double opacity = 0.5)
    {
        _customBackgroundPath = imagePath;
        _customBackgroundOpacity = Math.Clamp(opacity, 0, 1);
        _settings.Set(KeyCustomBgPath, imagePath);
        _settings.Set(KeyCustomBgOpacity, _customBackgroundOpacity);
        ApplyTheme();
    }

    public void SetCustomBackgroundOpacity(double opacity)
    {
        _customBackgroundOpacity = Math.Clamp(opacity, 0, 1);
        _settings.Set(KeyCustomBgOpacity, _customBackgroundOpacity);
        ApplyTheme();
    }

    public void ClearCustomBackground()
    {
        _customBackgroundPath = null;
        _settings.Set<string?>(KeyCustomBgPath, null);
        ApplyTheme();
    }

    public void SetFrostedBackgroundEnabled(bool enabled)
    {
        _frostedBackgroundEnabled = enabled;
        _settings.Set(KeyFrostedBg, enabled);

        if (!enabled && _coverBackgroundEnabled)
        {
            // 与 MAUI 一致：雾面关闭时级联关闭动态封面背景。
            _coverBackgroundEnabled = false;
            _settings.Set(KeyCoverBg, false);
        }

        ApplyTheme();
    }

    public void SetMonetBackgroundEnabled(bool enabled)
    {
        _monetBackgroundEnabled = enabled;
        _settings.Set(KeyMonetBg, enabled);

        if (enabled && _coverBackgroundEnabled)
        {
            _coverBackgroundEnabled = false;
            _settings.Set(KeyCoverBg, false);
        }

        ApplyTheme();
    }

    public void SetCoverBackgroundEnabled(bool enabled)
    {
        // 依赖雾面背景：雾面关闭时拒绝开启（与 MAUI 规则一致）。
        if (enabled && !_frostedBackgroundEnabled)
            return;

        _coverBackgroundEnabled = enabled;
        _settings.Set(KeyCoverBg, enabled);

        if (enabled && _monetBackgroundEnabled)
        {
            _monetBackgroundEnabled = false;
            _settings.Set(KeyMonetBg, false);
        }

        ApplyTheme();
    }

    public void UpdateCoverPalette(int primaryRgb, int secondaryRgb)
    {
        // P2（播放页动态封面背景）接入后再消费；当前只保留契约。
    }

    /// <summary>
    /// 左上/右下两团冷光晕（对应 Android `catClawBackground()` 的两团 radial glow）。
    /// WinUI 的 Panel.Background 只能挂一个 Brush，所以光晕由 MainWindow 叠一个 Border 承载，
    /// 这里只负责产出那一层的画刷。
    /// </summary>
    public static RadialGradientBrush CreateGlowBrush(bool dark)
    {
        // 深色：右上冷蓝 + 左下薄荷；浅色整体减淡，否则会把卡片洗白。
        var blue = dark ? Color.FromArgb(0x1F, 0x8F, 0xA3, 0xCC) : Color.FromArgb(0x14, 0x8F, 0xA3, 0xCC);
        var mint = dark ? Color.FromArgb(0x14, 0x7E, 0xD8, 0xC3) : Color.FromArgb(0x0D, 0x7E, 0xD8, 0xC3);

        var brush = new RadialGradientBrush
        {
            Center = new Windows.Foundation.Point(0.78, 0.0),
            GradientOrigin = new Windows.Foundation.Point(0.78, 0.0),
            RadiusX = 0.72,
            RadiusY = 0.86,
        };
        brush.GradientStops.Add(new GradientStop { Color = blue, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = Microsoft.UI.Colors.Transparent, Offset = 1 });
        return brush;
    }

    /// <summary>左下角第二团（薄荷）——单独一层，避免和蓝色层抢同一个中心。</summary>
    public static RadialGradientBrush CreateGlowBrushSecondary(bool dark)
    {
        var mint = dark ? Color.FromArgb(0x14, 0x7E, 0xD8, 0xC3) : Color.FromArgb(0x0A, 0x7E, 0xD8, 0xC3);
        var brush = new RadialGradientBrush
        {
            Center = new Windows.Foundation.Point(0.06, 0.96),
            GradientOrigin = new Windows.Foundation.Point(0.06, 0.96),
            RadiusX = 0.58,
            RadiusY = 0.72,
        };
        brush.GradientStops.Add(new GradientStop { Color = mint, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = Microsoft.UI.Colors.Transparent, Offset = 1 });
        return brush;
    }

    private static LinearGradientBrush CreateBackgroundBrush(bool dark)
    {
        var stops = dark ? DarkBackgroundStops : LightBackgroundStops;
        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(0, 1),
        };

        brush.GradientStops.Add(new GradientStop { Color = stops[0], Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = stops[1], Offset = 0.55 });
        brush.GradientStops.Add(new GradientStop { Color = stops[2], Offset = 1 });
        return brush;
    }

    /// <summary>
    /// 把一套令牌写进 Application.Resources（原地改 SolidColorBrush.Color，不重建视图）。
    /// 2026-11-20 与 Android 端对齐：墨色底 + 冷灰蓝强调色，去掉了所有樱粉。
    /// 深浅两套的差别只在「面有多白」与「字有多黑」——强调色始终是冷灰蓝。
    /// </summary>
    private static void ApplyTokens(bool dark)
    {
        var accent = ParseColor(dark ? Pink.Primary : Pink.Dark);

        SetBrush("AccentBrush", accent);
        SetBrush("AccentInkBrush", ParseColor(Pink.Light));
        SetBrush("AccentStrongBrush", ParseColor(Pink.Dark));
        SetBrush("AccentSoftBrush", Color.FromArgb(0x26, accent.R, accent.G, accent.B));
        SetBrush("MintBrush", ParseColor("#7ED8C3"));

        // 四个入口色：深色下用饱和度高的那组，浅色下压暗一档以免在浅底上发飘。
        SetBrush("QuickBlueBrush", ParseColor(dark ? "#7C9BE8" : "#5A7CD0"));
        SetBrush("QuickTealBrush", ParseColor(dark ? "#5FBEAE" : "#3F9E8E"));
        SetBrush("QuickVioletBrush", ParseColor(dark ? "#9A8DD6" : "#7A6DB6"));
        SetBrush("QuickAmberBrush", ParseColor(dark ? "#D8AA6A" : "#B88A4A"));

        SetBrush("TextPrimaryBrush", ParseColor(dark ? "#FFFFFF" : "#1D2230"));
        SetBrush("TextSecondaryBrush", ParseColor(dark ? "#B3FFFFFF" : "#5A6376"));
        SetBrush("TextTertiaryBrush", ParseColor(dark ? "#61FFFFFF" : "#8A93A6"));

        SetBrush("CardBackgroundBrush", ParseColor(dark ? "#0DFFFFFF" : "#B3FFFFFF"));
        SetBrush("CardBorderBrush", ParseColor(dark ? "#14FFFFFF" : "#12000000"));
        SetBrush("CardBackgroundStrongBrush", ParseColor(dark ? "#14FFFFFF" : "#D9FFFFFF"));

        SetBrush("SidebarTintBrush", ParseColor(dark ? "#0DFFFFFF" : "#B3FFFFFF"));
        SetBrush("SidebarBorderBrush", ParseColor(dark ? "#14FFFFFF" : "#12000000"));
        SetBrush("PlayerBarBrush", ParseColor(dark ? "#CC1A1F28" : "#E6FFFFFF"));
        SetBrush("PlayerBarBorderBrush", ParseColor(dark ? "#17FFFFFF" : "#14000000"));

        SetBrush("DividerBrush", ParseColor(dark ? "#0FFFFFFF" : "#0F000000"));
        SetBrush("SelectionBrush", ParseColor(dark ? "#2EDDE4F2" : "#241D2230"));
    }

    private static void SetBrush(string key, Color color)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is SolidColorBrush brush)
        {
            brush.Color = color;
            return;
        }

        Application.Current.Resources[key] = new SolidColorBrush(color);
    }

    private static Color ParseColor(string hex)
    {
        var text = hex.TrimStart('#');

        byte alpha = 0xFF;
        if (text.Length == 8)
        {
            alpha = Convert.ToByte(text[..2], 16);
            text = text[2..];
        }

        return Color.FromArgb(
            alpha,
            Convert.ToByte(text[..2], 16),
            Convert.ToByte(text.Substring(2, 2), 16),
            Convert.ToByte(text.Substring(4, 2), 16));
    }

    private sealed record ThemePalette(string Primary, string Light, string Dark);
}
