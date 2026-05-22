using CatClawMusic.Core.Interfaces;
using Android.OS;
using AndroidX.AppCompat.App;

namespace CatClawMusic.UI.Platforms.Android;

public class ThemeService : IThemeService
{
    private const string PrefsName = "catclaw_prefs";
    private const string ThemeKey = "selected_theme";
    private const string DarkModeKey = "dark_mode_setting";

    private AppTheme _currentTheme;
    private DarkModeSetting _darkModeSetting;

    public AppTheme CurrentTheme => _currentTheme;
    public DarkModeSetting DarkModeSetting => _darkModeSetting;

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

    public void SetTheme(AppTheme theme)
    {
        if (_currentTheme == theme) return;
        _currentTheme = theme;
        SaveTheme(theme);
    }

    public void SetDarkModeSetting(DarkModeSetting setting)
    {
        if (_darkModeSetting == setting) return;
        _darkModeSetting = setting;
        SaveDarkModeSetting(setting);
        ApplyDarkMode();
    }

    public void ApplyTheme()
    {
        var activity = MainActivity.Instance;
        if (activity == null) return;
        activity.ApplyThemeAndRefresh();
    }

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

    public bool IsEffectivelyDark()
    {
        return _darkModeSetting == DarkModeSetting.Dark ||
            (_darkModeSetting == DarkModeSetting.FollowSystem && IsSystemDarkMode());
    }

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
