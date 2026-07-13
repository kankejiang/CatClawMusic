using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 雾面动态背景控件：将封面图片进行4分块旋转、多重模糊和色调处理，
/// 生成雾面玻璃质感的动态背景。用于播放页和歌词页面。
/// </summary>
public class FrostedBackground : View
{
    /// <summary>封面图片源</summary>
    public static readonly BindableProperty SourceProperty =
        BindableProperty.Create(nameof(Source), typeof(ImageSource), typeof(FrostedBackground), null,
            propertyChanged: OnSourceChanged);

    /// <summary>是否激活（关闭时隐藏背景）</summary>
    public static readonly BindableProperty IsActiveProperty =
        BindableProperty.Create(nameof(IsActive), typeof(bool), typeof(FrostedBackground), true,
            propertyChanged: OnIsActiveChanged);

    /// <summary>色调叠加颜色（用于色调处理，通常为主题色）</summary>
    public static readonly BindableProperty TintColorProperty =
        BindableProperty.Create(nameof(TintColor), typeof(Color), typeof(FrostedBackground),
            Colors.Transparent, propertyChanged: OnTintChanged);

    /// <summary>色调叠加强度（0.0 - 1.0）</summary>
    public static readonly BindableProperty TintOpacityProperty =
        BindableProperty.Create(nameof(TintOpacity), typeof(double), typeof(FrostedBackground),
            0.35, propertyChanged: OnTintChanged);

    /// <summary>背景暗化程度（0.0 - 1.0，数值越大背景越暗，提升前景可读性）</summary>
    public static readonly BindableProperty DimAmountProperty =
        BindableProperty.Create(nameof(DimAmount), typeof(double), typeof(FrostedBackground),
            0.35, propertyChanged: OnTintChanged);

    /// <summary>填充模式（AspectFill / AspectFit / Fill）</summary>
    public static readonly BindableProperty AspectProperty =
        BindableProperty.Create(nameof(Aspect), typeof(Aspect), typeof(FrostedBackground),
            Aspect.AspectFill, propertyChanged: OnAspectChanged);

    /// <summary>
    /// 缓存键（可选）：用于跨实例共享处理后的位图。
    /// NowPlayingPage 和 FullLyricsPage 两个实例绑定到同一封面时，
    /// 通过相同的 CacheKey 命中缓存，避免重复进行 CPU 密集的 mesh+模糊处理。
    /// 通常绑定到封面文件路径或歌曲 ID。
    /// </summary>
    public static readonly BindableProperty CacheKeyProperty =
        BindableProperty.Create(nameof(CacheKey), typeof(string), typeof(FrostedBackground), null,
            propertyChanged: OnCacheKeyChanged);

    /// <summary>
    /// 用户是否正在滑动列表。滑动时暂停流体动画以释放主线程/GPU 资源，提升滑动流畅度。
    /// 通常绑定到 IInteractionStateService.IsUserScrolling。
    /// </summary>
    public static readonly BindableProperty IsScrollingProperty =
        BindableProperty.Create(nameof(IsScrolling), typeof(bool), typeof(FrostedBackground), false,
            propertyChanged: OnIsScrollingChanged);

    /// <summary>封面图片源</summary>
    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>是否激活</summary>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>色调叠加颜色</summary>
    public Color TintColor
    {
        get => (Color)GetValue(TintColorProperty);
        set => SetValue(TintColorProperty, value);
    }

    /// <summary>色调叠加强度</summary>
    public double TintOpacity
    {
        get => (double)GetValue(TintOpacityProperty);
        set => SetValue(TintOpacityProperty, value);
    }

    /// <summary>背景暗化程度</summary>
    public double DimAmount
    {
        get => (double)GetValue(DimAmountProperty);
        set => SetValue(DimAmountProperty, value);
    }

    /// <summary>填充模式</summary>
    public Aspect Aspect
    {
        get => (Aspect)GetValue(AspectProperty);
        set => SetValue(AspectProperty, value);
    }

    /// <summary>缓存键（用于跨实例共享处理后的位图）</summary>
    public string? CacheKey
    {
        get => (string?)GetValue(CacheKeyProperty);
        set => SetValue(CacheKeyProperty, value);
    }

    /// <summary>用户是否正在滑动列表（滑动时暂停动画）</summary>
    public bool IsScrolling
    {
        get => (bool)GetValue(IsScrollingProperty);
        set => SetValue(IsScrollingProperty, value);
    }

    private static void OnSourceChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is FrostedBackground fb)
            fb.Handler?.UpdateValue(nameof(Source));
    }

    private static void OnIsActiveChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is FrostedBackground fb)
            fb.Handler?.UpdateValue(nameof(IsActive));
    }

    private static void OnTintChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is FrostedBackground fb)
        {
            fb.Handler?.UpdateValue(nameof(TintColor));
            fb.Handler?.UpdateValue(nameof(TintOpacity));
            fb.Handler?.UpdateValue(nameof(DimAmount));
        }
    }

    private static void OnAspectChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is FrostedBackground fb)
            fb.Handler?.UpdateValue(nameof(Aspect));
    }

    private static void OnCacheKeyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        // CacheKey 变化时触发 Source 重新映射（让 Handler 使用新的 cache key）
        if (bindable is FrostedBackground fb)
            fb.Handler?.UpdateValue(nameof(Source));
    }

    private static void OnIsScrollingChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is FrostedBackground fb)
            fb.Handler?.UpdateValue(nameof(IsScrolling));
    }
}
