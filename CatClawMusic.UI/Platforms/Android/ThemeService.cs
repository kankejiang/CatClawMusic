using CatClawMusic.Core.Interfaces;
using Android.OS;
using AndroidX.AppCompat.App;

namespace CatClawMusic.UI.Platforms.Android;

/// <summary>主题服务，管理应用主题色和深色模式切换</summary>
public class ThemeService : IThemeService
{
    private const string PrefsName = "catclaw_prefs";
    private const string ThemeKey = "selected_theme";
    private const string DarkModeKey = "dark_mode_setting";

    private AppTheme _currentTheme;
    private DarkModeSetting _darkModeSetting;

    /// <summary>当前主题</summary>
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

    public ThemeService()
    {
        _currentTheme = LoadSavedTheme();
        _darkModeSetting = LoadDarkModeSetting();
    }

    /// <summary>设置当前主题色</summary>
    public void SetTheme(AppTheme theme)
    {
        if (_currentTheme == theme) return;
        
        _currentTheme = theme;
        SaveTheme(theme);
    }

    /// <summary>设置深色模式</summary>
    public void SetDarkModeSetting(DarkModeSetting setting)
    {
        if (_darkModeSetting == setting) return;
        
        _darkModeSetting = setting;
        SaveDarkModeSetting(setting);
        ApplyDarkMode();
    }

    /// <summary>应用当前主题到 Activity</summary>
    public void ApplyTheme()
    {
        var activity = MainActivity.Instance;
        if (activity == null) return;

        activity.SetTheme(GetThemeResourceId(_currentTheme));
        ApplyDarkMode();
    }

    /// <summary>检测系统是否处于深色模式</summary>
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

    /// <summary>应用深色模式到 AppCompatDelegate</summary>
    private void ApplyDarkMode()
    {
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

    /// <summary>从 SharedPreferences 加载保存的主题</summary>
    private AppTheme LoadSavedTheme()
    {
        var prefs = global::Android.App.Application.Context.GetSharedPreferences(PrefsName, global::Android.Content.FileCreationMode.Private)!;
        int savedValue = prefs.GetInt(ThemeKey, (int)AppTheme.Purple);
        return (AppTheme)savedValue;
    }

    /// <summary>从 SharedPreferences 加载深色模式设置</summary>
    private DarkModeSetting LoadDarkModeSetting()
    {
        var prefs = global::Android.App.Application.Context.GetSharedPreferences(PrefsName, global::Android.Content.FileCreationMode.Private)!;
        int savedValue = prefs.GetInt(DarkModeKey, (int)DarkModeSetting.Light);
        return (DarkModeSetting)savedValue;
    }

    /// <summary>保存主题到 SharedPreferences</summary>
    private void SaveTheme(AppTheme theme)
    {
        var prefs = global::Android.App.Application.Context.GetSharedPreferences(PrefsName, global::Android.Content.FileCreationMode.Private)!;
        var editor = prefs.Edit();
        editor.PutInt(ThemeKey, (int)theme);
        editor.Apply();
    }

    /// <summary>保存深色模式设置到 SharedPreferences</summary>
    private void SaveDarkModeSetting(DarkModeSetting setting)
    {
        var prefs = global::Android.App.Application.Context.GetSharedPreferences(PrefsName, global::Android.Content.FileCreationMode.Private)!;
        var editor = prefs.Edit();
        editor.PutInt(DarkModeKey, (int)setting);
        editor.Apply();
    }

    /// <summary>根据主题获取对应的 Style 资源 ID</summary>
    private int GetThemeResourceId(AppTheme theme)
    {
        return theme switch
        {
            AppTheme.Pink => Resource.Style.CatClawTheme_Pink,
            AppTheme.Blue => Resource.Style.CatClawTheme_Blue,
            AppTheme.Green => Resource.Style.CatClawTheme_Green,
            AppTheme.Orange => Resource.Style.CatClawTheme_Orange,
            _ => Resource.Style.CatClawTheme
        };
    }
}
