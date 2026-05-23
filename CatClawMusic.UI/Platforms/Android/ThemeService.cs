using CatClawMusic.Core.Interfaces;
using Android.OS;
using AndroidX.AppCompat.App;

namespace CatClawMusic.UI.Platforms.Android;

/// <summary>主题服务，管理应用配色主题和深色模式的切换与持久化</summary>
public class ThemeService : IThemeService
{
    private const string PrefsName = "catclaw_prefs";
    private const string ThemeKey = "selected_theme";
    private const string DarkModeKey = "dark_mode_setting";

    private AppTheme _currentTheme;
    private DarkModeSetting _darkModeSetting;

    /// <summary>当前主题配色</summary>
    public AppTheme CurrentTheme => _currentTheme;
    /// <summary>当前深色模式设置</summary>
    public DarkModeSetting DarkModeSetting => _darkModeSetting;

    /// <summary>可用主题列表</summary>
    public List<AppTheme> AvailableThemes => new List<AppTheme>
    {
        AppTheme.Purple,
        AppTheme.Pink,
        AppTheme.Blue,
        AppTheme.Green,
        AppTheme.Orange
    };

    /// <summary>初始化主题服务，加载保存的主题和深色模式设置</summary>
    public ThemeService()
    {
        _currentTheme = LoadSavedTheme();
        _darkModeSetting = LoadDarkModeSetting();
    }

    /// <summary>切换应用主题配色</summary>
    /// <param name="theme">目标主题</param>
    public void SetTheme(AppTheme theme)
    {
        if (_currentTheme == theme) return;
        _currentTheme = theme;
        SaveTheme(theme);
    }

    /// <summary>设置深色模式策略</summary>
    /// <param name="setting">深色模式设置</param>
    public void SetDarkModeSetting(DarkModeSetting setting)
    {
        if (_darkModeSetting == setting) return;
        _darkModeSetting = setting;
        SaveDarkModeSetting(setting);
        ApplyDarkMode();
    }

    /// <summary>立即应用当前主题到 MainActivity</summary>
    public void ApplyTheme()
    {
        var activity = MainActivity.Instance;
        if (activity == null) return;
        activity.ApplyThemeAndRefresh();
    }

    /// <summary>检查系统是否为深色模式</summary>
    /// <returns>true 表示系统处于深色模式</returns>
    public bool IsSystemDarkMode()
    {
        try
        {
            var context = global::Android.App.Application.Context;
            var uiMode = context.Resources.Configuration.UiMode;
            var currentMode = uiMode & global::Android.Content.Res.UiMode.NightMask;
            return currentMode == global::Android.Content.Res.UiMode.NightYes;
        }
        catch { }
        return false;
    }

    /// <summary>判断当前是否为有效深色模式（综合用户设置和系统状态）</summary>
    /// <returns>true 表示当前应使用深色主题</returns>
    public bool IsEffectivelyDark()
    {
        return _darkModeSetting == DarkModeSetting.Dark ||
            (_darkModeSetting == DarkModeSetting.FollowSystem && IsSystemDarkMode());
    }

    /// <summary>获取主题对应的 Android 资源 ID</summary>
    /// <param name="theme">目标主题，不指定则使用当前主题</param>
    /// <returns>主题资源 ID</returns>
    public int GetThemeResourceId(AppTheme? theme = null)
    {
        var t = theme ?? _currentTheme;
        return t switch
        {
            AppTheme.Pink => Resource.Style.CatClawTheme_Pink,
            AppTheme.Blue => Resource.Style.CatClawTheme_Blue,
            AppTheme.Green => Resource.Style.CatClawTheme_Green,
            AppTheme.Orange => Resource.Style.CatClawTheme_Orange,
            _ => Resource.Style.CatClawTheme
        };
    }

    private void ApplyDarkMode()
    {
        var activity = MainActivity.Instance;
        if (activity != null)
        {
            activity.ApplyThemeAndRefresh();
            return;
        }
        switch (_darkModeSetting)
        {
            case DarkModeSetting.Light:
                AppCompatDelegate.DefaultNightMode = AppCompatDelegate.ModeNightNo;
                break;
            case DarkModeSetting.Dark:
                AppCompatDelegate.DefaultNightMode = AppCompatDelegate.ModeNightYes;
                break;
            case DarkModeSetting.FollowSystem:
                AppCompatDelegate.DefaultNightMode = AppCompatDelegate.ModeNightFollowSystem;
                break;
        }
    }

    private AppTheme LoadSavedTheme()
    {
        var prefs = global::Android.App.Application.Context.GetSharedPreferences(PrefsName, global::Android.Content.FileCreationMode.Private)!;
        int savedValue = prefs.GetInt(ThemeKey, (int)AppTheme.Purple);
        return (AppTheme)savedValue;
    }

    private DarkModeSetting LoadDarkModeSetting()
    {
        var prefs = global::Android.App.Application.Context.GetSharedPreferences(PrefsName, global::Android.Content.FileCreationMode.Private)!;
        int savedValue = prefs.GetInt(DarkModeKey, (int)DarkModeSetting.Light);
        return (DarkModeSetting)savedValue;
    }

    private void SaveTheme(AppTheme theme)
    {
        var prefs = global::Android.App.Application.Context.GetSharedPreferences(PrefsName, global::Android.Content.FileCreationMode.Private)!;
        var editor = prefs.Edit();
        editor.PutInt(ThemeKey, (int)theme);
        editor.Apply();
    }

    private void SaveDarkModeSetting(DarkModeSetting setting)
    {
        var prefs = global::Android.App.Application.Context.GetSharedPreferences(PrefsName, global::Android.Content.FileCreationMode.Private)!;
        var editor = prefs.Edit();
        editor.PutInt(DarkModeKey, (int)setting);
        editor.Apply();
    }
}
