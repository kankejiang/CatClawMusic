namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 主题管理服务接口
/// </summary>
public interface IThemeService
{
    AppTheme CurrentTheme { get; }

    DarkModeSetting DarkModeSetting { get; }

    List<AppTheme> AvailableThemes { get; }

    string? CustomBackgroundPath { get; }

    double CustomBackgroundOpacity { get; }

    bool HasCustomBackground { get; }

    void SetTheme(AppTheme theme);

    void SetDarkModeSetting(DarkModeSetting setting);

    void ApplyTheme();

    bool IsSystemDarkMode();

    bool IsEffectivelyDark();

    void SetCustomBackground(string? imagePath, double opacity = 0.5);

    void SetCustomBackgroundOpacity(double opacity);

    void ClearCustomBackground();
}

/// <summary>
/// 应用主题枚举
/// </summary>
public enum AppTheme
{
    Purple = 0,
    Pink = 1,
    Blue = 2,
    Green = 3,
    Orange = 4,
    Red = 5,
    Teal = 6,
    Yellow = 7,
    Indigo = 8,
    Cyan = 9
}

/// <summary>
/// 深色模式设置枚举
/// </summary>
public enum DarkModeSetting
{
    Light = 0,      // 浅色模式
    Dark = 1,       // 深色模式
    FollowSystem = 2 // 跟随系统
}
