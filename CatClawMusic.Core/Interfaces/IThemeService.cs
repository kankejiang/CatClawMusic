namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 主题管理服务接口
/// </summary>
public interface IThemeService
{
    /// <summary>
    /// 获取当前主题
    /// </summary>
    AppTheme CurrentTheme { get; }

    /// <summary>
    /// 获取深色模式设置
    /// </summary>
    DarkModeSetting DarkModeSetting { get; }

    /// <summary>
    /// 获取所有可用的主题
    /// </summary>
    List<AppTheme> AvailableThemes { get; }

    /// <summary>
    /// 设置主题
    /// </summary>
    void SetTheme(AppTheme theme);

    /// <summary>
    /// 设置深色模式设置
    /// </summary>
    void SetDarkModeSetting(DarkModeSetting setting);

    /// <summary>
    /// 应用当前主题到活动
    /// </summary>
    void ApplyTheme();

    /// <summary>
    /// 检测系统当前是否处于深色模式
    /// </summary>
    bool IsSystemDarkMode();
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
    Orange = 4
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
