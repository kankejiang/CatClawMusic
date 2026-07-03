using CatClawMusic.Core.Interfaces;
using CoreAppTheme = CatClawMusic.Core.Interfaces.AppTheme;
using MauiAppTheme = Microsoft.Maui.ApplicationModel.AppTheme;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System.IO;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// MAUI 主题管理服务，支持 10 种颜色主题和明/暗/跟随系统三种模式。
/// 通过 MAUI ResourceDictionary 动态切换主题色。
/// </summary>
public class ThemeService : IThemeService
{
    private const string KeyTheme = "theme_index";
    private const string KeyDarkMode = "dark_mode";
    private const string KeyCustomBgPath = "custom_bg_path";
    private const string KeyCustomBgOpacity = "custom_bg_opacity";

    private CoreAppTheme _currentTheme;
    private DarkModeSetting _darkModeSetting;
    private string? _customBackgroundPath;
    private double _customBackgroundOpacity = 0.5;

    /// <summary>主题色定义（10种主题：紫、粉、蓝、绿、橙、红、青、黄、靛蓝、青蓝）</summary>
    private static readonly Dictionary<CoreAppTheme, ThemeColors> ThemeMap = new()
    {
        [CoreAppTheme.Purple] = new ThemeColors("#9B7ED8", "#E8E0FF", "#7C5DCE"),
        [CoreAppTheme.Pink] = new ThemeColors("#EC407A", "#FFE0EB", "#D81B60"),
        [CoreAppTheme.Blue] = new ThemeColors("#42A5F5", "#D6E8FF", "#1E88E5"),
        [CoreAppTheme.Green] = new ThemeColors("#66BB6A", "#D6F5D8", "#43A047"),
        [CoreAppTheme.Orange] = new ThemeColors("#FF7043", "#FFE0D6", "#F4511E"),
        [CoreAppTheme.Red] = new ThemeColors("#EF5350", "#FFE0E0", "#E53935"),
        [CoreAppTheme.Teal] = new ThemeColors("#26A69A", "#D6F5F0", "#00897B"),
        [CoreAppTheme.Yellow] = new ThemeColors("#FFC107", "#FFF8D6", "#FFB300"),
        [CoreAppTheme.Indigo] = new ThemeColors("#5C6BC0", "#E0E4FF", "#3949AB"),
        [CoreAppTheme.Cyan] = new ThemeColors("#00BCD4", "#D6F7FB", "#00ACC1"),
    };

    public CoreAppTheme CurrentTheme => _currentTheme;
    public DarkModeSetting DarkModeSetting => _darkModeSetting;
    public string? CustomBackgroundPath => _customBackgroundPath;
    public double CustomBackgroundOpacity => _customBackgroundOpacity;
    public bool HasCustomBackground => !string.IsNullOrEmpty(_customBackgroundPath) && File.Exists(_customBackgroundPath);

    public List<CoreAppTheme> AvailableThemes => Enum.GetValues<CoreAppTheme>().ToList();

    public ThemeService()
    {
        LoadSettings();
        // 启动时立即设置 UserAppTheme，确保 RequestedTheme 正确
        if (Application.Current != null)
        {
            Application.Current.UserAppTheme = _darkModeSetting switch
            {
                DarkModeSetting.Light => MauiAppTheme.Light,
                DarkModeSetting.Dark => MauiAppTheme.Dark,
                _ => MauiAppTheme.Unspecified,
            };
        }
    }

    public void SetCustomBackground(string? imagePath, double opacity = 0.5)
    {
        _customBackgroundPath = imagePath;
        _customBackgroundOpacity = Math.Clamp(opacity, 0.1, 1.0);
        if (string.IsNullOrEmpty(imagePath))
        {
            Preferences.Default.Remove(KeyCustomBgPath);
            Preferences.Default.Remove(KeyCustomBgOpacity);
        }
        else
        {
            Preferences.Default.Set(KeyCustomBgPath, imagePath);
            Preferences.Default.Set(KeyCustomBgOpacity, _customBackgroundOpacity);
        }
        ApplyTheme();
    }

    public void SetCustomBackgroundOpacity(double opacity)
    {
        _customBackgroundOpacity = Math.Clamp(opacity, 0.1, 1.0);
        Preferences.Default.Set(KeyCustomBgOpacity, _customBackgroundOpacity);
        ApplyTheme();
    }

    public void ClearCustomBackground()
    {
        SetCustomBackground(null);
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

            ApplyCustomBackground(app.Resources, isDark);

            UpdatePlatformStatusBar();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeService] ApplyTheme failed: {ex.Message}");
        }
    }

    private static void UpdatePlatformStatusBar()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
#if ANDROID
            if (Platform.CurrentActivity is global::CatClawMusic.Maui.MainActivity activity)
            {
                activity.UpdateDecorViewBackground();
            }
#endif
        });
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
            _customBackgroundPath = Preferences.Default.Get<string?>(KeyCustomBgPath, null);
            _customBackgroundOpacity = Preferences.Default.Get(KeyCustomBgOpacity, 0.5);
            if (_customBackgroundPath != null && !File.Exists(_customBackgroundPath))
                _customBackgroundPath = null;

            if (!ThemeMap.ContainsKey(_currentTheme))
                _currentTheme = CoreAppTheme.Purple;
        }
        catch
        {
            _currentTheme = CoreAppTheme.Purple;
            _darkModeSetting = DarkModeSetting.FollowSystem;
            _customBackgroundPath = null;
            _customBackgroundOpacity = 0.5;
        }
    }

    private void SaveSetting(string key, int value)
    {
        try { Preferences.Default.Set(key, value); } catch { }
    }

    #endregion

    private static void ApplyDarkPalette(ResourceDictionary resources, ThemeColors colors)
    {
        var primary = Color.FromArgb(colors.Primary);
        var darkBase = Color.FromArgb("#080914");
        var midTone = Color.FromArgb("#0F1228");
        var primaryTint = primary.WithAlpha(0.18f);
        var accentTint = Color.FromArgb(GetAccentColor(_currentThemeStatic(colors.Primary))).WithAlpha(0.1f);

        resources["WindowBackgroundColor"] = darkBase;
        resources["WindowBackgroundAltColor"] = Color.FromArgb("#131735");
        resources["SurfaceColor"] = Color.FromArgb("#171B33");
        resources["CardBackgroundColor"] = Color.FromArgb("#1AFFFFFF");
        resources["CardBackgroundStrongColor"] = Color.FromArgb("#2DFFFFFF");
        resources["InputBackgroundColor"] = Color.FromArgb("#15FFFFFF");
        resources["InputBorderColor"] = Color.FromArgb("#2BFFFFFF");
        resources["DividerColor"] = Color.FromArgb("#1FFFFFFF");
        resources["GlassStrokeColor"] = Color.FromArgb("#28FFFFFF");
        resources["GlassStrokeStrongColor"] = Color.FromArgb("#4AFFFFFF");
        resources["ChipInactiveColor"] = Color.FromArgb("#15FFFFFF");
        resources["ChipActiveColor"] = Color.FromArgb(colors.Primary);
        resources["ChipInactiveTextColor"] = Color.FromArgb("#C8CDE8");
        resources["ChipActiveTextColor"] = Colors.White;
        resources["BadgeBackgroundColor"] = Color.FromArgb("#14FFFFFF");
        resources["TextPrimaryColor"] = Color.FromArgb("#F5F6FF");
        resources["TextSecondaryColor"] = Color.FromArgb("#BCC0DD");
        resources["TextHintColor"] = Color.FromArgb("#868CAE");
        resources["TabActiveColor"] = Color.FromArgb(colors.Primary);
        resources["TabInactiveColor"] = Color.FromArgb("#868CAE");
        resources["TabBarBackgroundColor"] = Color.FromArgb("#D80A0D1E");

        // 增强饱和度的iOS风格渐变背景
        resources["PageBackgroundBrush"] = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb("#0B0D20"), 0f),
            new(Blend(darkBase, primaryTint), 0.2f),
            new(Blend(midTone, primaryTint.WithAlpha(0.6f)), 0.45f),
            new(Blend(midTone, accentTint), 0.75f),
            new(darkBase, 1f),
        }, new Point(0.5, 0), new Point(0.5, 1));

        resources["HeroBrush"] = BuildLinearBrush(colors.Primary, GetAccentColorHex(colors.Primary), 0.0f, 1.0f);
        resources["PrimaryGlowBrush"] = BuildRadialBrush($"{AlphaHex(0x5A)}{colors.Primary[1..]}", $"{AlphaHex(0x00)}{colors.Primary[1..]}");
        var accent = GetAccentColor(_currentThemeStatic(colors.Primary));
        resources["AccentGlowBrush"] = BuildRadialBrush($"{AlphaHex(0x45)}{accent[1..]}", $"{AlphaHex(0x00)}{accent[1..]}");
        resources["GlassHighlightBrush"] = BuildLinearBrush("#28FFFFFF", "#04FFFFFF");
    }

    private static void ApplyLightPalette(ResourceDictionary resources, ThemeColors colors)
    {
        var primary = Color.FromArgb(colors.Primary);
        var primaryLight = Color.FromArgb(colors.Light);
        var lightBase = Color.FromArgb("#F8F7FF");
        var primaryWash = primaryLight.WithAlpha(0.6f);
        var accent = Color.FromArgb(GetAccentColor(_currentThemeStatic(colors.Primary))).WithAlpha(0.22f);

        resources["WindowBackgroundColor"] = lightBase;
        resources["WindowBackgroundAltColor"] = Color.FromArgb("#EEEBFF");
        resources["SurfaceColor"] = Color.FromArgb("#FFFFFFFF");
        resources["CardBackgroundColor"] = Color.FromArgb("#E6FFFFFF");
        resources["CardBackgroundStrongColor"] = Color.FromArgb("#F5FFFFFF");
        resources["InputBackgroundColor"] = Color.FromArgb("#D9FFFFFF");
        resources["InputBorderColor"] = Color.FromArgb("#40FFFFFF");
        resources["DividerColor"] = Color.FromArgb("#1A000000");
        resources["GlassStrokeColor"] = Color.FromArgb("#40FFFFFF");
        resources["GlassStrokeStrongColor"] = Color.FromArgb("#80FFFFFF");
        resources["ChipInactiveColor"] = Color.FromArgb("#D0FFFFFF");
        resources["ChipActiveColor"] = Color.FromArgb(colors.Primary);
        resources["ChipInactiveTextColor"] = Color.FromArgb("#4A5278");
        resources["ChipActiveTextColor"] = Colors.White;
        resources["BadgeBackgroundColor"] = Color.FromArgb("#E6FFFFFF");
        resources["TextPrimaryColor"] = Color.FromArgb("#1A1F3A");
        resources["TextSecondaryColor"] = Color.FromArgb("#4A5278");
        resources["TextHintColor"] = Color.FromArgb("#6B7399");
        resources["TabActiveColor"] = Color.FromArgb(colors.Primary);
        resources["TabInactiveColor"] = Color.FromArgb("#5C648F");
        resources["TabBarBackgroundColor"] = Color.FromArgb("#F5F8F7FF");

        // 增强饱和度的iOS风格浅色渐变
        resources["PageBackgroundBrush"] = new LinearGradientBrush(new GradientStopCollection
        {
            new(Blend(lightBase, primaryWash), 0f),
            new(Blend(lightBase, primaryWash.WithAlpha(0.35f)), 0.35f),
            new(lightBase, 0.6f),
            new(Blend(lightBase, accent), 0.85f),
            new(lightBase, 1f),
        }, new Point(0.5, 0), new Point(0.5, 1));

        resources["HeroBrush"] = BuildLinearBrush(colors.Primary, colors.Dark, 0.0f, 1.0f);
        resources["PrimaryGlowBrush"] = BuildRadialBrush($"{AlphaHex(0x4A)}{colors.Primary[1..]}", $"{AlphaHex(0x00)}{colors.Primary[1..]}");
        var accentCol = GetAccentColor(_currentThemeStatic(colors.Primary));
        resources["AccentGlowBrush"] = BuildRadialBrush($"{AlphaHex(0x35)}{accentCol[1..]}", $"{AlphaHex(0x00)}{accentCol[1..]}");
        resources["GlassHighlightBrush"] = BuildLinearBrush("#55FFFFFF", "#10FFFFFF");
    }

    private static Color Blend(Color baseColor, Color overlay)
    {
        var a = overlay.Alpha;
        return new Color(
            baseColor.Red * (1 - a) + overlay.Red * a,
            baseColor.Green * (1 - a) + overlay.Green * a,
            baseColor.Blue * (1 - a) + overlay.Blue * a,
            1f);
    }

    private void ApplyCustomBackground(ResourceDictionary resources, bool isDark)
    {
        bool hasBg = HasCustomBackground;
        resources["CustomBackgroundEnabled"] = hasBg;
        resources["CustomBackgroundOpacity"] = _customBackgroundOpacity;

        if (hasBg)
        {
            try
            {
                byte[] bgBytes = File.ReadAllBytes(_customBackgroundPath!);
                resources["CustomBackgroundImage"] = ImageSource.FromStream(() => new MemoryStream(bgBytes));
            }
            catch
            {
                resources["CustomBackgroundEnabled"] = false;
                resources["CustomBackgroundImage"] = null;
                RestorePageBackground(resources, isDark);
                return;
            }

            double maskAlpha = isDark ? 0.55 : 0.35;
            resources["CustomBackgroundMaskColor"] = isDark
                ? Colors.Black.WithAlpha((float)maskAlpha)
                : Colors.White.WithAlpha((float)maskAlpha);

            double overlayAlpha = isDark ? 0.75 : 0.6;
            resources["PageBackgroundBrush"] = new SolidColorBrush(
                (isDark ? Color.FromArgb("#080914") : Color.FromArgb("#F8F7FF")).WithAlpha((float)overlayAlpha));
            resources["WindowBackgroundColor"] = (isDark ? Color.FromArgb("#080914") : Color.FromArgb("#F8F7FF")).WithAlpha((float)overlayAlpha);
        }
        else
        {
            resources["CustomBackgroundImage"] = null;
        }
    }

    private void RestorePageBackground(ResourceDictionary resources, bool isDark)
    {
        var colors = ThemeMap[_currentTheme];
        if (isDark)
            ApplyDarkPalette(resources, colors);
        else
            ApplyLightPalette(resources, colors);
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
        CoreAppTheme.Purple => "#55D6FF",
        CoreAppTheme.Pink => "#FFB86E",
        CoreAppTheme.Blue => "#5AE4FF",
        CoreAppTheme.Green => "#67E5C1",
        CoreAppTheme.Orange => "#FFD36E",
        CoreAppTheme.Red => "#FF8A65",
        CoreAppTheme.Teal => "#80CBC4",
        CoreAppTheme.Yellow => "#FFAB40",
        CoreAppTheme.Indigo => "#7C4DFF",
        CoreAppTheme.Cyan => "#84FFFF",
        _ => "#55D6FF"
    };

    private static string GetAccentColorHex(string primaryHex)
        => primaryHex switch
        {
            "#9B7ED8" => "#55D6FF",
            "#EC407A" => "#FFB86E",
            "#42A5F5" => "#5AE4FF",
            "#66BB6A" => "#67E5C1",
            "#FF7043" => "#FFD36E",
            "#EF5350" => "#FF8A65",
            "#26A69A" => "#80CBC4",
            "#FFC107" => "#FFAB40",
            "#5C6BC0" => "#7C4DFF",
            "#00BCD4" => "#84FFFF",
            _ => "#55D6FF"
        };

    private static CoreAppTheme _currentThemeStatic(string primaryHex)
        => primaryHex switch
        {
            "#9B7ED8" => CoreAppTheme.Purple,
            "#EC407A" => CoreAppTheme.Pink,
            "#42A5F5" => CoreAppTheme.Blue,
            "#66BB6A" => CoreAppTheme.Green,
            "#FF7043" => CoreAppTheme.Orange,
            "#EF5350" => CoreAppTheme.Red,
            "#26A69A" => CoreAppTheme.Teal,
            "#FFC107" => CoreAppTheme.Yellow,
            "#5C6BC0" => CoreAppTheme.Indigo,
            "#00BCD4" => CoreAppTheme.Cyan,
            _ => CoreAppTheme.Purple
        };

    private static string AlphaHex(byte alpha) => alpha.ToString("X2");

    /// <summary>主题颜色组</summary>
    private record ThemeColors(string Primary, string Light, string Dark);
}
