namespace Vitrum;

/// <summary>
/// Frosted-glass overlay that renders the blurred background captured by the nearest
/// <see cref="BlurHostView"/> in the visual tree.
/// </summary>
/// <remarks>
/// <para>
/// The handler automatically finds and registers with the nearest <see cref="BlurHostView"/>
/// by walking up the MAUI element tree and checking each level's siblings.
/// Consumers can be arbitrarily deep inside a sibling of the host — nesting depth
/// inside a <b>sibling</b> is safe.
/// </para>
/// <para>
/// <b>Critical:</b> a consumer must never be inside the <see cref="BlurHostView"/>'s
/// own content subtree. See <see cref="BlurHostView"/> remarks and README.md.
/// </para>
/// </remarks>
public class BlurConsumerView : ContentView
{
    /// <summary>
    /// Semi-transparent ARGB color drawn on top of the blurred texture.
    /// Controls the tint hue and opacity of the frosted-glass effect.
    /// </summary>
    /// <remarks>
    /// <c>#B21C1C25</c> (70% opacity navy) works well for dark-theme action bars and input areas.
    /// For a lighter glass on light themes, try <c>#B2FFFFFF</c>.
    /// The default <c>#661C1C25</c> is 40% opacity — increase alpha for a denser frost.
    /// </remarks>
    public static readonly BindableProperty TintColorProperty =
        BindableProperty.Create(nameof(TintColor), typeof(Color), typeof(BlurConsumerView),
            Color.FromArgb("#661C1C25"));

    /// <inheritdoc cref="TintColorProperty"/>
    public Color TintColor
    {
        get => (Color)GetValue(TintColorProperty);
        set => SetValue(TintColorProperty, value);
    }

    /// <summary>
    /// Enables the liquid glass lens/refraction effect on top of the backdrop blur.
    /// Requires Android 13+ (API 33). On older devices falls back to plain blur.
    /// </summary>
    public static readonly BindableProperty LiquidGlassProperty =
        BindableProperty.Create(nameof(LiquidGlass), typeof(bool), typeof(BlurConsumerView), false);

    /// <inheritdoc cref="LiquidGlassProperty"/>
    public bool LiquidGlass
    {
        get => (bool)GetValue(LiquidGlassProperty);
        set => SetValue(LiquidGlassProperty, value);
    }

    /// <summary>
    /// Corner radius in dp applied to the liquid glass lens shape.
    /// Use 0 for a flat-edge rectangle, or match the visual corner radius of the view.
    /// </summary>
    public static readonly BindableProperty LiquidGlassCornerRadiusProperty =
        BindableProperty.Create(nameof(LiquidGlassCornerRadius), typeof(float), typeof(BlurConsumerView), 0f);

    /// <inheritdoc cref="LiquidGlassCornerRadiusProperty"/>
    public float LiquidGlassCornerRadius
    {
        get => (float)GetValue(LiquidGlassCornerRadiusProperty);
        set => SetValue(LiquidGlassCornerRadiusProperty, value);
    }

    /// <summary>
    /// When false, blur and liquid glass rendering is skipped entirely.
    /// The view draws only the tint overlay over a transparent background.
    /// Use this for the "None" performance mode to avoid GPU capture costs.
    /// </summary>
    public static readonly BindableProperty BlurEnabledProperty =
        BindableProperty.Create(nameof(BlurEnabled), typeof(bool), typeof(BlurConsumerView), true);

    /// <inheritdoc cref="BlurEnabledProperty"/>
    public bool BlurEnabled
    {
        get => (bool)GetValue(BlurEnabledProperty);
        set => SetValue(BlurEnabledProperty, value);
    }
}
