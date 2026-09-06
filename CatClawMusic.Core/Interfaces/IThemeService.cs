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

    /// <summary>是否启用雾面动态背景（全站封面流雾面）</summary>
    bool FrostedBackgroundEnabled { get; }

    /// <summary>是否启用莫奈取色背景（背景色跟随系统壁纸，Material You）。
    /// 与动态封面背景单选互斥。</summary>
    bool MonetBackgroundEnabled { get; }

    /// <summary>是否开启动态封面取色背景（背景色跟随当前歌曲封面）。
    /// 与莫奈单选互斥，且依赖雾面动态背景开启。</summary>
    bool CoverBackgroundEnabled { get; }

    /// <summary>主题/背景应用完成事件：ApplyTheme 结束时触发。
    /// 用于修复运行时替换自定义背景时 DynamicResource 不通知原生视图、
    /// 以及返回主页面后背景丢失的问题（页面侧收到后强制重刷背景。</summary>
    event Action? Applied;

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

    /// <summary>设置雾面动态背景开关（关闭时级联关闭动态封面背景）</summary>
    /// <param name="enabled">是否启用雾面背景</param>
    void SetFrostedBackgroundEnabled(bool enabled);

    /// <summary>设置莫奈取色背景开关（与动态封面单选互斥：开启时自动关闭对方）</summary>
    /// <param name="enabled">是否启用莫奈取色背景</param>
    void SetMonetBackgroundEnabled(bool enabled);

    /// <summary>设置动态封面取色背景开关（与莫奈单选互斥；依赖雾面动态背景，雾面关闭时拒绝开启）</summary>
    /// <param name="enabled">是否开启动态封面取色背景</param>
    void SetCoverBackgroundEnabled(bool enabled);

    /// <summary>封面取色回传：当前歌曲封面的主/次强调色（0xRRGGBB）。
    /// primaryRgb 传 0 表示当前无封面（清空封面色，背景回退莫奈/标准渐变）。
    /// 取色结果与已存调色板相同时内部去重，不触发背景重刷。</summary>
    void UpdateCoverPalette(int primaryRgb, int secondaryRgb);
}

/// <summary>
/// 应用主题枚举
/// </summary>
public enum AppTheme
{
    /// <summary>紫色主题</summary>
    Purple = 0,
    /// <summary>粉色主题</summary>
    Pink = 1,
    /// <summary>蓝色主题</summary>
    Blue = 2,
    /// <summary>橙色主题</summary>
    Orange = 4,
    /// <summary>青色主题</summary>
    Teal = 6,
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
