using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 流光喷发动态背景控件（Halcyon / Apple Music 风格，程序化生成，不读封面）。
/// 用共享的 FrostedFlowProcessor 在两端同一套数学渲染：4 个光点随时间漂移 + Perlin 噪声扰动 +
/// HSV 降饱和 + 提亮 + 颗粒，颜色在若干阶段间缓慢过渡。用于播放页、全屏歌词页及主题背景页。
/// </summary>
public class FrostedBackground : View
{
    /// <summary>是否激活（关闭时隐藏背景）。绑定播放状态：非播放时不跑动画。</summary>
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

    /// <summary>是否使用深色流光预设（浅色柔和高亮 vs 深色深邃光源）</summary>
    public static readonly BindableProperty IsDarkProperty =
        BindableProperty.Create(nameof(IsDark), typeof(bool), typeof(FrostedBackground), false,
            propertyChanged: OnIsDarkChanged);

    /// <summary>封面源数据（ARGB 像素数组及尺寸，供封面流渲染）。有封面时背景切换为"模糊+过饱和封面流"</summary>
    public static readonly BindableProperty CoverSourceProperty =
        BindableProperty.Create(nameof(CoverSource), typeof(CatClawMusic.Maui.Services.Frosted.CoverFlowProcessor.CoverSource),
            typeof(FrostedBackground), default(CatClawMusic.Maui.Services.Frosted.CoverFlowProcessor.CoverSource),
            propertyChanged: OnCoverSourceChanged);

    /// <summary>
    /// 用户是否正在滑动列表。滑动时暂停流光动画（释放主线程/CPU 资源），提升滑动流畅度。
    /// 通常绑定到 IInteractionStateService.IsUserScrolling。
    /// </summary>
    public static readonly BindableProperty IsScrollingProperty =
        BindableProperty.Create(nameof(IsScrolling), typeof(bool), typeof(FrostedBackground), false,
            propertyChanged: OnIsScrollingChanged);

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

    /// <summary>深色流光预设</summary>
    public bool IsDark
    {
        get => (bool)GetValue(IsDarkProperty);
        set => SetValue(IsDarkProperty, value);
    }

    /// <summary>封面源数据（有值时背景切为封面流）</summary>
    public CatClawMusic.Maui.Services.Frosted.CoverFlowProcessor.CoverSource CoverSource
    {
        get => (CatClawMusic.Maui.Services.Frosted.CoverFlowProcessor.CoverSource)GetValue(CoverSourceProperty);
        set => SetValue(CoverSourceProperty, value);
    }

    /// <summary>用户是否正在滑动列表（滑动时暂停动画）</summary>
    public bool IsScrolling
    {
        get => (bool)GetValue(IsScrollingProperty);
        set => SetValue(IsScrollingProperty, value);
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

    private static void OnIsDarkChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is FrostedBackground fb)
            fb.Handler?.UpdateValue(nameof(IsDark));
    }

    private static void OnIsScrollingChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is FrostedBackground fb)
            fb.Handler?.UpdateValue(nameof(IsScrolling));
    }

    private static void OnCoverSourceChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is FrostedBackground fb)
            fb.Handler?.UpdateValue(nameof(CoverSource));
    }
}