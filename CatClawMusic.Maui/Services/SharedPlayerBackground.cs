namespace CatClawMusic.Maui.Services;

/// <summary>
/// 播放器雾面背景的共享协调器。
/// 手机竖屏下，播放页（NowPlayingPage）与全屏歌词页（FullLyricsPage）是 ViewPager 的相邻两页，
/// 若各自持有 FrostedBackground，切页时背景会随页面一起滑动（"背景切页面"）。
/// 改为：MainPage 在 ViewPager 之外放一层共享 FrostedBackground，两页页内背景停用，
/// 切页时只有内容层滑动，背景纹丝不动。
/// 本类仅承载"当前是否处于共享背景场景"的开关，供两页判断该不该隐藏自己的页内背景。
/// </summary>
public static class SharedPlayerBackground
{
    /// <summary>true = 共享背景场景（手机竖屏 ViewPager），两页应隐藏页内 FrostedBackground。
    /// 横屏桌面舞台 / Windows 端为 false（页面独立呈现，保留页内背景）。</summary>
    public static bool Enabled { get; set; }

    /// <summary>读取设置里的"雾面动态背景"开关（ThemeService 写入的资源键）。</summary>
    public static bool FrostedEnabled =>
        Application.Current?.Resources != null
        && Application.Current.Resources.TryGetValue("FrostedBackgroundEnabled", out var value)
        && value is bool b && b;

    /// <summary>页面内 FrostedBackground 是否该显示：非共享场景 + 开关打开。
    /// 共享场景下背景由 MainPage 统一提供，页内那层必须停掉（否则双层叠加且随页面平移）。</summary>
    public static bool PageBackgroundVisible => !Enabled && FrostedEnabled;
}
