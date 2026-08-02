using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Effects;

/// <summary>
/// 歌词行高斯模糊效果（RoutingEffect）。
///
/// 仅 Windows 端有对应 PlatformEffect 实现；其它平台无匹配实现时 MAUI 会直接忽略，不会报错。
///
/// 因为 <see cref="RoutingEffect"/> 不是 BindableObject，无法在自身上放可绑定属性，
/// 故采用**附加属性**承载模糊半径，在 DataTemplate 内这样用：
///   &lt;VerticalStackLayout effects:LyricBlurEffect.BlurAmount="{Binding Blur}"&gt;
///     &lt;VerticalStackLayout.Effects&gt;
///       &lt;effects:LyricBlurEffect /&gt;
///     &lt;/VerticalStackLayout.Effects&gt;
///     ...
///   &lt;/VerticalStackLayout&gt;
/// PlatformEffect 通过 OnElementPropertyChanged 读取此附加属性来决定模糊强度。
/// </summary>
public class LyricBlurEffect : RoutingEffect
{
    public LyricBlurEffect() : base("CatClaw.LyricBlur")
    {
    }

    public static readonly BindableProperty BlurAmountProperty =
        BindableProperty.CreateAttached(
            "BlurAmount",
            typeof(double),
            typeof(LyricBlurEffect),
            0.0,
            propertyChanged: OnBlurAmountChanged);

    /// <summary>模糊半径（DP）。0 表示清晰。</summary>
    public static double GetBlurAmount(BindableObject view) => (double)view.GetValue(BlurAmountProperty);

    public static void SetBlurAmount(BindableObject view, double value) => view.SetValue(BlurAmountProperty, value);

    private static void OnBlurAmountChanged(BindableObject bindable, object oldValue, object newValue)
    {
        // 实际生效在 PlatformEffect.OnElementPropertyChanged 中处理。
    }
}
