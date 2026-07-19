namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 主题管理服务接口，负责应用主题、深色模式及自定义背景的设置与应用
/// </summary>
public interface IThemeService
{
    /// <summary>当前应用的主题</summary>
    AppTheme CurrentTheme { get; }

    /// <summary>当前的深色模式设置</summary>
    DarkModeSetting DarkModeSetting { get; }

    /// <summary>可选的主题列表</summary>
    List<AppTheme> AvailableThemes { get; }

    /// <summary>自定义背景图片路径（未设置时为 null）</summary>
    string? CustomBackgroundPath { get; }

    /// <summary>自定义背景的不透明度（0.0 - 1.0）</summary>
    double CustomBackgroundOpacity { get; }

    /// <summary>是否设置了自定义背景</summary>
    bool HasCustomBackground { get; }

    /// <summary>是否启用雾面动态背景（播放页/歌词页）</summary>
    bool FrostedBackgroundEnabled { get; }

    /// <summary>切换当前主题</summary>
    /// <param name="theme">目标主题</param>
    void SetTheme(AppTheme theme);

    /// <summary>设置深色模式策略</summary>
    /// <param name="setting">深色模式设置</param>
    void SetDarkModeSetting(DarkModeSetting setting);

    /// <summary>将当前主题与深色模式设置应用到界面</summary>
    void ApplyTheme();

    /// <summary>判断系统当前是否处于深色模式</summary>
    bool IsSystemDarkMode();

    /// <summary>判断应用当前实际显示效果是否为深色（综合考虑系统与设置）</summary>
    bool IsEffectivelyDark();

    /// <summary>设置自定义背景图片及不透明度</summary>
    /// <param name="imagePath">图片文件路径，传入 null 表示清除</param>
    /// <param name="opacity">背景不透明度</param>
    void SetCustomBackground(string? imagePath, double opacity = 0.5);

    /// <summary>仅修改自定义背景的不透明度</summary>
    /// <param name="opacity">背景不透明度</param>
    void SetCustomBackgroundOpacity(double opacity);

    /// <summary>清除自定义背景设置</summary>
    void ClearCustomBackground();

    /// <summary>设置雾面动态背景开关</summary>
    /// <param name="enabled">是否启用雾面背景</param>
    void SetFrostedBackgroundEnabled(bool enabled);
}

/// <summary>
/// 应用主题枚举（5 套夜空银河原型色板）
/// </summary>
public enum AppTheme
{
    /// <summary>品牌紫蓝</summary>
    BrandPurpleBlue = 0,
    /// <summary>极光青</summary>
    AuroraCyan = 1,
    /// <summary>魅紫粉</summary>
    WarmPurplePink = 2,
    /// <summary>日落橙</summary>
    SunsetOrange = 3,
    /// <summary>翡翠绿</summary>
    EmeraldGreen = 4
}

/// <summary>
/// 深色模式设置枚举
/// </summary>
public enum DarkModeSetting
{
    /// <summary>浅色模式</summary>
    Light = 0,
    /// <summary>深色模式</summary>
    Dark = 1,
    /// <summary>跟随系统</summary>
    FollowSystem = 2
}
