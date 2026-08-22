using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 真毛玻璃背景控件：实时抓取其下层（目标视图）的内容区域做高斯模糊，
/// 用于底部导航栏/迷你播放器等悬浮栏。必须让内容区延伸到栏位背后才能透出下层内容。
/// </summary>
public class BackdropBlur : View
{
    /// <summary>要模糊的下层目标视图（通常绑定到内容区 ViewPager/Grid）</summary>
    public static readonly BindableProperty TargetProperty =
        BindableProperty.Create(nameof(Target), typeof(View), typeof(BackdropBlur), null,
            propertyChanged: OnTargetChanged);

    /// <summary>模糊半径(dp)</summary>
    public static readonly BindableProperty BlurRadiusProperty =
        BindableProperty.Create(nameof(BlurRadius), typeof(double), typeof(BackdropBlur), 24.0,
            propertyChanged: OnBlurRadiusChanged);

    /// <summary>要模糊的下层目标视图</summary>
    public View? Target
    {
        get => (View?)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    /// <summary>模糊半径(dp)</summary>
    public double BlurRadius
    {
        get => (double)GetValue(BlurRadiusProperty);
        set => SetValue(BlurRadiusProperty, value);
    }

    private static void OnTargetChanged(BindableObject bindable, object _, object __)
    {
        if (bindable is BackdropBlur bb)
            bb.Handler?.UpdateValue(nameof(Target));
    }

    private static void OnBlurRadiusChanged(BindableObject bindable, object _, object __)
    {
        if (bindable is BackdropBlur bb)
            bb.Handler?.UpdateValue(nameof(BlurRadius));
    }
}