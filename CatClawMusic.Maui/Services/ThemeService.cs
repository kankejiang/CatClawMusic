using CatClawMusic.Core.Interfaces;
using CoreAppTheme = CatClawMusic.Core.Interfaces.AppTheme;
using MauiAppTheme = Microsoft.Maui.ApplicationModel.AppTheme;

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
            app.Resources["PrimaryColor"] = Microsoft.Maui.Graphics.Color.FromArgb(colors.Primary);
            app.Resources["PrimaryLightColor"] = Microsoft.Maui.Graphics.Color.FromArgb(colors.Light);
            app.Resources["PrimaryDarkColor"] = Microsoft.Maui.Graphics.Color.FromArgb(colors.Dark);

            if (isDark)
            {
                app.Resources["WindowBackgroundColor"] = Microsoft.Maui.Graphics.Color.FromArgb("#121212");
                app.Resources["CardBackgroundColor"] = Microsoft.Maui.Graphics.Color.FromArgb("#1E1E1E");
                app.Resources["TextPrimaryColor"] = Microsoft.Maui.Graphics.Color.FromArgb("#FFFFFF");
                app.Resources["TextSecondaryColor"] = Microsoft.Maui.Graphics.Color.FromArgb("#B3B3B3");
                app.Resources["TabActiveColor"] = Microsoft.Maui.Graphics.Color.FromArgb(colors.Primary);
                app.Resources["TabInactiveColor"] = Microsoft.Maui.Graphics.Color.FromArgb("#666666");
                app.Resources["DividerColor"] = Microsoft.Maui.Graphics.Color.FromArgb("#2C2C2C");
                app.Resources["SurfaceColor"] = Microsoft.Maui.Graphics.Color.FromArgb("#252525");
            }
            else
            {
                app.Resources["WindowBackgroundColor"] = Microsoft.Maui.Graphics.Color.FromArgb("#FAFAFA");
                app.Resources["CardBackgroundColor"] = Microsoft.Maui.Graphics.Color.FromArgb("#FFFFFF");
                app.Resources["TextPrimaryColor"] = Microsoft.Maui.Graphics.Color.FromArgb("#1A1A1A");
                app.Resources["TextSecondaryColor"] = Microsoft.Maui.Graphics.Color.FromArgb("#666666");
                app.Resources["TabActiveColor"] = Microsoft.Maui.Graphics.Color.FromArgb(colors.Primary);
                app.Resources["TabInactiveColor"] = Microsoft.Maui.Graphics.Color.FromArgb("#999999");
                app.Resources["DividerColor"] = Microsoft.Maui.Graphics.Color.FromArgb("#E0E0E0");
                app.Resources["SurfaceColor"] = Microsoft.Maui.Graphics.Color.FromArgb("#F5F5F5");
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

    /// <summary>主题颜色组</summary>
    private record ThemeColors(string Primary, string Light, string Dark);
}
