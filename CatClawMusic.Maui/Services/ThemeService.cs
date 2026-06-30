using CatClawMusic.Core.Interfaces;
using CoreAppTheme = CatClawMusic.Core.Interfaces.AppTheme;
using MauiAppTheme = Microsoft.Maui.ApplicationModel.AppTheme;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// MAUI 主题管理服务，支持 5 种颜色主题和明/暗/跟随系统三种模式。
/// 通过 MAUI ResourceDictionary 动态切换主题色。
/// </summary>
public class ThemeService : IThemeService
{
    private const string KeyTheme = "theme_index";
    private const string KeyDarkMode = "dark_mode";

    private CoreAppTheme _currentTheme;
    private DarkModeSetting _darkModeSetting;

    /// <summary>主题色定义（对应原版 5 主题：紫、粉、蓝、绿、橙）</summary>
    private static readonly Dictionary<CoreAppTheme, ThemeColors> ThemeMap = new()
    {
        [CoreAppTheme.Purple] = new ThemeColors("#7B61FF", "#E8E0FF", "#B8A9FF"),
        [CoreAppTheme.Pink] = new ThemeColors("#FF6B9D", "#FFE0EB", "#FFB3CC"),
        [CoreAppTheme.Blue] = new ThemeColors("#4A90D9", "#D6E8FF", "#8FBCFF"),
        [CoreAppTheme.Green] = new ThemeColors("#4CAF50", "#D6F5D8", "#81C784"),
        [CoreAppTheme.Orange] = new ThemeColors("#FF9800", "#FFE8CC", "#FFB74D"),
    };

    public CoreAppTheme CurrentTheme => _currentTheme;
    public DarkModeSetting DarkModeSetting => _darkModeSetting;

    public List<CoreAppTheme> AvailableThemes => Enum.GetValues<CoreAppTheme>().ToList();

    public ThemeService()
    {
        LoadSettings();
    }

    public void SetTheme(CoreAppTheme theme)
    {
        _currentTheme = theme;
        SaveSetting(KeyTheme, (int)theme);
        ApplyTheme();
    }

    public void SetDarkModeSetting(DarkModeSetting setting)
    {
        _darkModeSetting = setting;
        SaveSetting(KeyDarkMode, (int)setting);

        Application.Current!.UserAppTheme = setting switch
        {
            DarkModeSetting.Light => MauiAppTheme.Light,
            DarkModeSetting.Dark => MauiAppTheme.Dark,
            _ => MauiAppTheme.Unspecified,
        };

        ApplyTheme();
    }

    public void ApplyTheme()
    {
        try
        {
            var app = Application.Current;
            if (app?.Resources == null) return;

            var colors = ThemeMap[_currentTheme];
            var isDark = IsEffectivelyDark();

            // 动态资源键 — 页面通过 {DynamicResource Key} 引用
            app.Resources["PrimaryColor"] = Color.FromArgb(colors.Primary);
            app.Resources["PrimaryLightColor"] = Color.FromArgb(colors.Light);
            app.Resources["PrimaryDarkColor"] = Color.FromArgb(colors.Dark);
            app.Resources["AccentColor"] = Color.FromArgb(GetAccentColor(_currentTheme));

            if (isDark)
            {
                ApplyDarkPalette(app.Resources, colors);
            }
            else
            {
                ApplyLightPalette(app.Resources, colors);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeService] ApplyTheme failed: {ex.Message}");
        }
    }

    public bool IsSystemDarkMode()
    {
        return Application.Current?.RequestedTheme == MauiAppTheme.Dark;
    }

    public bool IsEffectivelyDark()
    {
        return _darkModeSetting switch
        {
            DarkModeSetting.Dark => true,
            DarkModeSetting.Light => false,
            _ => IsSystemDarkMode(),
        };
    }

    #region 持久化

    private void LoadSettings()
    {
        try
        {
            _currentTheme = (CoreAppTheme)Preferences.Default.Get(KeyTheme, 0);
            _darkModeSetting = (DarkModeSetting)Preferences.Default.Get(KeyDarkMode, 2);
        }
        catch
        {
            _currentTheme = CoreAppTheme.Purple;
            _darkModeSetting = DarkModeSetting.FollowSystem;
        }
    }

    private void SaveSetting(string key, int value)
    {
        try { Preferences.Default.Set(key, value); } catch { }
    }

    #endregion

    private static void ApplyDarkPalette(ResourceDictionary resources, ThemeColors colors)
    {
        resources["WindowBackgroundColor"] = Color.FromArgb("#0A0B17");
        resources["WindowBackgroundAltColor"] = Color.FromArgb("#171B34");
        resources["SurfaceColor"] = Color.FromArgb("#1A1E38");
        resources["CardBackgroundColor"] = Color.FromArgb("#22FFFFFF");
        resources["CardBackgroundStrongColor"] = Color.FromArgb("#30FFFFFF");
        resources["InputBackgroundColor"] = Color.FromArgb("#1AFFFFFF");
        resources["InputBorderColor"] = Color.FromArgb("#33FFFFFF");
        resources["DividerColor"] = Color.FromArgb("#24FFFFFF");
        resources["GlassStrokeColor"] = Color.FromArgb("#2EFFFFFF");
        resources["GlassStrokeStrongColor"] = Color.FromArgb("#52FFFFFF");
        resources["ChipInactiveColor"] = Color.FromArgb("#18FFFFFF");
        resources["ChipActiveColor"] = Color.FromArgb(colors.Primary);
        resources["ChipInactiveTextColor"] = Color.FromArgb("#D0D5ED");
        resources["ChipActiveTextColor"] = Colors.White;
        resources["BadgeBackgroundColor"] = Color.FromArgb("#16FFFFFF");
        resources["TextPrimaryColor"] = Color.FromArgb("#F7F8FF");
        resources["TextSecondaryColor"] = Color.FromArgb("#C2C6E4");
        resources["TextHintColor"] = Color.FromArgb("#8D93B7");
        resources["TabActiveColor"] = Color.FromArgb("#F7F8FF");
        resources["TabInactiveColor"] = Color.FromArgb("#8D93B7");
        resources["TabBarBackgroundColor"] = Color.FromArgb("#CC111427");
        resources["PageBackgroundBrush"] = BuildLinearBrush("#0A0B17", "#151933", "#0B0D1C");
        resources["HeroBrush"] = BuildLinearBrush(colors.Primary, GetAccentColorHex(colors.Primary), 0.0f, 1.0f);
        resources["PrimaryGlowBrush"] = BuildRadialBrush($"{AlphaHex(0x55)}{colors.Primary[1..]}", $"{AlphaHex(0x00)}{colors.Primary[1..]}");
        var accent = GetAccentColor(_currentThemeStatic(colors.Primary));
        resources["AccentGlowBrush"] = BuildRadialBrush($"{AlphaHex(0x44)}{accent[1..]}", $"{AlphaHex(0x00)}{accent[1..]}");
        resources["GlassHighlightBrush"] = BuildLinearBrush("#30FFFFFF", "#05FFFFFF");
    }

    private static void ApplyLightPalette(ResourceDictionary resources, ThemeColors colors)
    {
        resources["WindowBackgroundColor"] = Color.FromArgb("#EEF2FF");
        resources["WindowBackgroundAltColor"] = Color.FromArgb("#DCE4FF");
        resources["SurfaceColor"] = Color.FromArgb("#F7F9FF");
        resources["CardBackgroundColor"] = Color.FromArgb("#BFFFFFFF");
        resources["CardBackgroundStrongColor"] = Color.FromArgb("#E6FFFFFF");
        resources["InputBackgroundColor"] = Color.FromArgb("#D9FFFFFF");
        resources["InputBorderColor"] = Color.FromArgb("#33FFFFFF");
        resources["DividerColor"] = Color.FromArgb("#1F5060AA");
        resources["GlassStrokeColor"] = Color.FromArgb("#26FFFFFF");
        resources["GlassStrokeStrongColor"] = Color.FromArgb("#66FFFFFF");
        resources["ChipInactiveColor"] = Color.FromArgb("#D9FFFFFF");
        resources["ChipActiveColor"] = Color.FromArgb(colors.Primary);
        resources["ChipInactiveTextColor"] = Color.FromArgb("#5C648F");
        resources["ChipActiveTextColor"] = Colors.White;
        resources["BadgeBackgroundColor"] = Color.FromArgb("#CCFFFFFF");
        resources["TextPrimaryColor"] = Color.FromArgb("#1B2140");
        resources["TextSecondaryColor"] = Color.FromArgb("#5D668E");
        resources["TextHintColor"] = Color.FromArgb("#7E86A7");
        resources["TabActiveColor"] = Color.FromArgb(colors.Primary);
        resources["TabInactiveColor"] = Color.FromArgb("#7E86A7");
        resources["TabBarBackgroundColor"] = Color.FromArgb("#F2FFFFFF");
        resources["PageBackgroundBrush"] = BuildLinearBrush("#EEF2FF", "#E4EAFF", "#F8FAFF");
        resources["HeroBrush"] = BuildLinearBrush(colors.Primary, colors.Dark, 0.0f, 1.0f);
        resources["PrimaryGlowBrush"] = BuildRadialBrush($"{AlphaHex(0x44)}{colors.Primary[1..]}", $"{AlphaHex(0x00)}{colors.Primary[1..]}");
        var accent = GetAccentColor(_currentThemeStatic(colors.Primary));
        resources["AccentGlowBrush"] = BuildRadialBrush($"{AlphaHex(0x2F)}{accent[1..]}", $"{AlphaHex(0x00)}{accent[1..]}");
        resources["GlassHighlightBrush"] = BuildLinearBrush("#55FFFFFF", "#00FFFFFF");
    }

    private static LinearGradientBrush BuildLinearBrush(string startHex, string endHex, float startOffset = 0f, float endOffset = 1f)
        => new(new GradientStopCollection
        {
            new(Color.FromArgb(startHex), startOffset),
            new(Color.FromArgb(endHex), endOffset)
        }, new Point(0, 0), new Point(1, 1));

    private static LinearGradientBrush BuildLinearBrush(string startHex, string middleHex, string endHex)
        => new(new GradientStopCollection
        {
            new(Color.FromArgb(startHex), 0f),
            new(Color.FromArgb(middleHex), 0.55f),
            new(Color.FromArgb(endHex), 1f)
        }, new Point(0, 0), new Point(1, 1));

    private static RadialGradientBrush BuildRadialBrush(string centerHex, string edgeHex)
        => new(new GradientStopCollection
        {
            new(Color.FromArgb(centerHex), 0f),
            new(Color.FromArgb(edgeHex), 1f)
        })
        {
            Center = new Point(0.5, 0.5),
            Radius = 0.9f
        };

    private static string GetAccentColor(CoreAppTheme theme) => theme switch
    {
        CoreAppTheme.Pink => "#FFB86E",
        CoreAppTheme.Blue => "#5AE4FF",
        CoreAppTheme.Green => "#67E5C1",
        CoreAppTheme.Orange => "#FFD36E",
        _ => "#55D6FF"
    };

    private static string GetAccentColorHex(string primaryHex)
        => primaryHex switch
        {
            "#FF6B9D" => "#FFB86E",
            "#4A90D9" => "#5AE4FF",
            "#4CAF50" => "#67E5C1",
            "#FF9800" => "#FFD36E",
            _ => "#55D6FF"
        };

    private static CoreAppTheme _currentThemeStatic(string primaryHex)
        => primaryHex switch
        {
            "#FF6B9D" => CoreAppTheme.Pink,
            "#4A90D9" => CoreAppTheme.Blue,
            "#4CAF50" => CoreAppTheme.Green,
            "#FF9800" => CoreAppTheme.Orange,
            _ => CoreAppTheme.Purple
        };

    private static string AlphaHex(byte alpha) => alpha.ToString("X2");

    /// <summary>主题颜色组</summary>
    private record ThemeColors(string Primary, string Light, string Dark);
}
