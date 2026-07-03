using CatClawMusic.Core.Interfaces;
using CoreAppTheme = CatClawMusic.Core.Interfaces.AppTheme;
using MauiAppTheme = Microsoft.Maui.ApplicationModel.AppTheme;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System.IO;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// MAUI 主题管理服务，支持 5 种颜色主题和明/暗/跟随系统三种模式。
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

    /// <summary>主题色定义（对应 5 主题：紫、粉、蓝、绿、橙）</summary>
    private static readonly Dictionary<CoreAppTheme, ThemeColors> ThemeMap = new()
    {
        [CoreAppTheme.Purple] = new ThemeColors("#9B7ED8", "#E8E0FF", "#B8A9FF"),
        [CoreAppTheme.Pink] = new ThemeColors("#EC407A", "#FFE0EB", "#F48FB1"),
        [CoreAppTheme.Blue] = new ThemeColors("#42A5F5", "#D6E8FF", "#90CAF9"),
        [CoreAppTheme.Green] = new ThemeColors("#66BB6A", "#D6F5D8", "#A5D6A7"),
        [CoreAppTheme.Orange] = new ThemeColors("#FF7043", "#FFE0D6", "#FFAB91"),
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
        var primaryTint = primary.WithAlpha(0.12f);
        var accentTint = Color.FromArgb(GetAccentColor(_currentThemeStatic(colors.Primary))).WithAlpha(0.06f);

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
        resources["TabActiveColor"] = Color.FromArgb("#F5F6FF");
        resources["TabInactiveColor"] = Color.FromArgb("#868CAE");
        resources["TabBarBackgroundColor"] = Color.FromArgb("#C80A0D1E");

        // iOS-style multi-stop gradient: deep base → soft primary wash at top → deep base with accent hint at bottom
        resources["PageBackgroundBrush"] = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb("#0B0D20"), 0f),
            new(Blend(darkBase, primaryTint), 0.25f),
            new(Blend(midTone, primaryTint), 0.5f),
            new(Blend(midTone, accentTint), 0.75f),
            new(darkBase, 1f),
        }, new Point(0.5, 0), new Point(0.5, 1));

        resources["HeroBrush"] = BuildLinearBrush(colors.Primary, GetAccentColorHex(colors.Primary), 0.0f, 1.0f);
        resources["PrimaryGlowBrush"] = BuildRadialBrush($"{AlphaHex(0x4D)}{colors.Primary[1..]}", $"{AlphaHex(0x00)}{colors.Primary[1..]}");
        var accent = GetAccentColor(_currentThemeStatic(colors.Primary));
        resources["AccentGlowBrush"] = BuildRadialBrush($"{AlphaHex(0x3D)}{accent[1..]}", $"{AlphaHex(0x00)}{accent[1..]}");
        resources["GlassHighlightBrush"] = BuildLinearBrush("#28FFFFFF", "#04FFFFFF");
    }

    private static void ApplyLightPalette(ResourceDictionary resources, ThemeColors colors)
    {
        var primary = Color.FromArgb(colors.Primary);
        var primaryLight = Color.FromArgb(colors.Light);
        var lightBase = Color.FromArgb("#F8F7FF");
        var primaryWash = primaryLight.WithAlpha(0.45f);
        var accent = Color.FromArgb(GetAccentColor(_currentThemeStatic(colors.Primary))).WithAlpha(0.15f);

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
        resources["ChipInactiveTextColor"] = Color.FromArgb("#5C648F");
        resources["ChipActiveTextColor"] = Colors.White;
        resources["BadgeBackgroundColor"] = Color.FromArgb("#E6FFFFFF");
        resources["TextPrimaryColor"] = Color.FromArgb("#1A1F3A");
        resources["TextSecondaryColor"] = Color.FromArgb("#58608A");
        resources["TextHintColor"] = Color.FromArgb("#7D85A8");
        resources["TabActiveColor"] = Color.FromArgb(colors.Primary);
        resources["TabInactiveColor"] = Color.FromArgb("#8A90B2");
        resources["TabBarBackgroundColor"] = Color.FromArgb("#F0F8F7FF");

        // iOS-style light gradient: soft primary tint at top → clean white middle → subtle accent at bottom
        resources["PageBackgroundBrush"] = new LinearGradientBrush(new GradientStopCollection
        {
            new(Blend(lightBase, primaryWash), 0f),
            new(Blend(lightBase, primaryWash.WithAlpha(0.25f)), 0.35f),
            new(lightBase, 0.6f),
            new(Blend(lightBase, accent), 0.85f),
            new(lightBase, 1f),
        }, new Point(0.5, 0), new Point(0.5, 1));

        resources["HeroBrush"] = BuildLinearBrush(colors.Primary, colors.Dark, 0.0f, 1.0f);
        resources["PrimaryGlowBrush"] = BuildRadialBrush($"{AlphaHex(0x3A)}{colors.Primary[1..]}", $"{AlphaHex(0x00)}{colors.Primary[1..]}");
        var accentCol = GetAccentColor(_currentThemeStatic(colors.Primary));
        resources["AccentGlowBrush"] = BuildRadialBrush($"{AlphaHex(0x25)}{accentCol[1..]}", $"{AlphaHex(0x00)}{accentCol[1..]}");
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
        CoreAppTheme.Pink => "#FFB86E",
        CoreAppTheme.Blue => "#5AE4FF",
        CoreAppTheme.Green => "#67E5C1",
        CoreAppTheme.Orange => "#FFD36E",
        _ => "#55D6FF"
    };

    private static string GetAccentColorHex(string primaryHex)
        => primaryHex switch
        {
            "#EC407A" => "#FFB86E",
            "#42A5F5" => "#5AE4FF",
            "#66BB6A" => "#67E5C1",
            "#FF7043" => "#FFD36E",
            _ => "#55D6FF"
        };

    private static CoreAppTheme _currentThemeStatic(string primaryHex)
        => primaryHex switch
        {
            "#EC407A" => CoreAppTheme.Pink,
            "#42A5F5" => CoreAppTheme.Blue,
            "#66BB6A" => CoreAppTheme.Green,
            "#FF7043" => CoreAppTheme.Orange,
            _ => CoreAppTheme.Purple
        };

    private static string AlphaHex(byte alpha) => alpha.ToString("X2");

    /// <summary>主题颜色组</summary>
    private record ThemeColors(string Primary, string Light, string Dark);
}
