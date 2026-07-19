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
    private const string KeyFrostedBg = "frosted_bg_enabled";

    private CoreAppTheme _currentTheme;
    private DarkModeSetting _darkModeSetting;
    private string? _customBackgroundPath;
    private double _customBackgroundOpacity = 0.5;
    private bool _frostedBackgroundEnabled = true;

    /// <summary>主题色定义（5 套夜空银河原型色板）</summary>
    private static readonly Dictionary<CoreAppTheme, ThemeColors> ThemeMap = new()
    {
        [CoreAppTheme.BrandPurpleBlue] = new ThemeColors("#8C7BFF", "#E8E0FF", "#6B5DCC"),
        [CoreAppTheme.AuroraCyan]      = new ThemeColors("#55D6FF", "#D6F7FF", "#3FA8CC"),
        [CoreAppTheme.WarmPurplePink]  = new ThemeColors("#B07CFF", "#F0E0FF", "#8A5ECC"),
        [CoreAppTheme.SunsetOrange]    = new ThemeColors("#FFB07C", "#FFE8D6", "#CC8A5E"),
        [CoreAppTheme.EmeraldGreen]    = new ThemeColors("#7CFFB0", "#D6FFEA", "#5ECC8A"),
    };

    /// <summary>获取当前主题色枚举</summary>
    public CoreAppTheme CurrentTheme => _currentTheme;
    /// <summary>获取当前暗黑模式设置</summary>
    public DarkModeSetting DarkModeSetting => _darkModeSetting;
    /// <summary>获取自定义背景图片的绝对路径；未设置时为 null</summary>
    public string? CustomBackgroundPath => _customBackgroundPath;
    /// <summary>获取自定义背景的不透明度（0.1 ~ 1.0）</summary>
    public double CustomBackgroundOpacity => _customBackgroundOpacity;
    /// <summary>获取是否存在有效的自定义背景图片</summary>
    public bool HasCustomBackground => !string.IsNullOrEmpty(_customBackgroundPath) && File.Exists(_customBackgroundPath);

    /// <summary>获取是否启用雾面动态背景（播放页/歌词页）</summary>
    public bool FrostedBackgroundEnabled => _frostedBackgroundEnabled;

    /// <summary>获取所有可选主题列表</summary>
    public List<CoreAppTheme> AvailableThemes => Enum.GetValues<CoreAppTheme>().ToList();

    /// <summary>构造函数，加载持久化设置并立即应用主题</summary>
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

    /// <summary>设置自定义背景图片及不透明度</summary>
    /// <param name="imagePath">背景图片路径；传 null 或空字符串表示清除背景</param>
    /// <param name="opacity">不透明度（0.1 ~ 1.0），自动钳制到范围内</param>
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

    /// <summary>仅更新自定义背景的不透明度</summary>
    /// <param name="opacity">不透明度（0.1 ~ 1.0），自动钳制到范围内</param>
    public void SetCustomBackgroundOpacity(double opacity)
    {
        _customBackgroundOpacity = Math.Clamp(opacity, 0.1, 1.0);
        Preferences.Default.Set(KeyCustomBgOpacity, _customBackgroundOpacity);
        ApplyTheme();
    }

    /// <summary>清除自定义背景图片设置</summary>
    public void ClearCustomBackground()
    {
        SetCustomBackground(null);
    }

    /// <summary>设置雾面动态背景开关并持久化</summary>
    /// <param name="enabled">是否启用雾面背景</param>
    public void SetFrostedBackgroundEnabled(bool enabled)
    {
        _frostedBackgroundEnabled = enabled;
        Preferences.Default.Set(KeyFrostedBg, enabled);
        ApplyTheme();
    }

    /// <summary>切换主题色并持久化</summary>
    /// <param name="theme">目标主题色枚举</param>
    public void SetTheme(CoreAppTheme theme)
    {
        _currentTheme = theme;
        SaveSetting(KeyTheme, (int)theme);
        ApplyTheme();
    }

    /// <summary>设置暗黑模式（明/暗/跟随系统）并持久化</summary>
    /// <param name="setting">暗黑模式选项</param>
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

    /// <summary>应用当前主题色与暗黑模式到应用资源字典，刷新所有绑定</summary>
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
            // 雾面动态背景开关（供播放页/歌词页 DynamicResource 绑定）
            app.Resources["FrostedBackgroundEnabled"] = _frostedBackgroundEnabled;

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
            Log.Debug("ThemeService", $"[ThemeService] ApplyTheme failed: {ex.Message}");
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

    /// <summary>获取系统当前是否处于暗黑模式</summary>
    /// <returns>系统暗黑模式返回 true；否则返回 false</returns>
    public bool IsSystemDarkMode()
    {
        return Application.Current?.RequestedTheme == MauiAppTheme.Dark;
    }

    /// <summary>获取应用最终生效的暗黑状态（综合用户设置与系统状态）</summary>
    /// <returns>暗黑模式生效返回 true；否则返回 false</returns>
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
            _frostedBackgroundEnabled = Preferences.Default.Get(KeyFrostedBg, true);
            if (_customBackgroundPath != null && !File.Exists(_customBackgroundPath))
                _customBackgroundPath = null;

            // 旧版保存了 10 种主题，新版仅保留 5 套原型色板；非法值重置为默认
            if (!ThemeMap.ContainsKey(_currentTheme))
                _currentTheme = CoreAppTheme.BrandPurpleBlue;
        }
        catch
        {
            _currentTheme = CoreAppTheme.BrandPurpleBlue;
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
        resources["GalaxyDeepColor"] = Blend(darkBase, primary.WithAlpha(0.12f));
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
        resources["BadgeStrokeColor"] = Color.FromArgb("#28FFFFFF");
        resources["CardOverlayColor"] = Color.FromArgb("#0AFFFFFF");
        resources["ButtonOverlayColor"] = Color.FromArgb("#12FFFFFF");
        resources["ProgressTrackColor"] = Color.FromArgb("#20FFFFFF");
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
        // 夜空银河底色：保持深空质感（即便浅色模式也以深空夜空为底，符合「夜空银河」品牌），
        // 仅做轻微主题色染色，避免与星点（白色）对比丢失。
        resources["GalaxyDeepColor"] = Blend(Color.FromArgb("#0C0E1C"), primary.WithAlpha(0.14f));
        resources["WindowBackgroundAltColor"] = Color.FromArgb("#EEEBFF");
        resources["SurfaceColor"] = Color.FromArgb("#FFFFFFFF");
        resources["CardBackgroundColor"] = Color.FromArgb("#FFFFFFFF");
        resources["CardBackgroundStrongColor"] = Color.FromArgb("#F5F7FF");
        resources["InputBackgroundColor"] = Color.FromArgb("#F0F2FF");
        resources["InputBorderColor"] = Color.FromArgb("#30000000");
        resources["DividerColor"] = Color.FromArgb("#1A000000");
        resources["GlassStrokeColor"] = Color.FromArgb("#28000000");
        resources["GlassStrokeStrongColor"] = Color.FromArgb("#50000000");
        resources["ChipInactiveColor"] = Color.FromArgb("#E8ECFF");
        resources["ChipActiveColor"] = Color.FromArgb(colors.Primary);
        resources["ChipInactiveTextColor"] = Color.FromArgb("#4A5278");
        resources["ChipActiveTextColor"] = Colors.White;
        resources["BadgeBackgroundColor"] = accent;
        resources["BadgeStrokeColor"] = accent;
        resources["CardOverlayColor"] = Color.FromArgb("#08000000");
        resources["ButtonOverlayColor"] = Color.FromArgb("#12000000");
        resources["ProgressTrackColor"] = Color.FromArgb("#18000000");
        resources["TextPrimaryColor"] = Color.FromArgb("#1A1F3A");
        resources["TextSecondaryColor"] = Color.FromArgb("#4A5278");
        resources["TextHintColor"] = Color.FromArgb("#6B7399");
        resources["TabActiveColor"] = Color.FromArgb(colors.Primary);
        resources["TabInactiveColor"] = Color.FromArgb("#5C648F");
        resources["TabBarBackgroundColor"] = Color.FromArgb("#F5F8FF");

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
        CoreAppTheme.BrandPurpleBlue => "#55D6FF",
        CoreAppTheme.AuroraCyan      => "#5CFFC4",
        CoreAppTheme.WarmPurplePink  => "#FF7CB0",
        CoreAppTheme.SunsetOrange    => "#FF7C9E",
        CoreAppTheme.EmeraldGreen    => "#55D6FF",
        _ => "#55D6FF"
    };

    private static string GetAccentColorHex(string primaryHex)
        => primaryHex switch
        {
            "#8C7BFF" => "#55D6FF",
            "#55D6FF" => "#5CFFC4",
            "#B07CFF" => "#FF7CB0",
            "#FFB07C" => "#FF7C9E",
            "#7CFFB0" => "#55D6FF",
            _ => "#55D6FF"
        };

    private static CoreAppTheme _currentThemeStatic(string primaryHex)
        => primaryHex switch
        {
            "#8C7BFF" => CoreAppTheme.BrandPurpleBlue,
            "#55D6FF" => CoreAppTheme.AuroraCyan,
            "#B07CFF" => CoreAppTheme.WarmPurplePink,
            "#FFB07C" => CoreAppTheme.SunsetOrange,
            "#7CFFB0" => CoreAppTheme.EmeraldGreen,
            _ => CoreAppTheme.BrandPurpleBlue
        };

    private static string AlphaHex(byte alpha) => alpha.ToString("X2");

    /// <summary>主题颜色组</summary>
    private record ThemeColors(string Primary, string Light, string Dark);
}
