namespace Vitrum;

/// <summary>
/// Root container that owns the blur capture source.
/// </summary>
/// <remarks>
/// Wrap your background content in this view. BlurConsumerViews must be siblings,
/// never inside the host's content subtree. See README.md for crash rules.
/// </remarks>
public class BlurHostView : ContentView
{
    /// <summary>
    /// Blur sigma in dp. Default 60 is tuned for high-performance devices.
    /// Increase for heavier blur (text less readable), decrease for lighter effect.
    /// </summary>
    public static readonly BindableProperty BlurRadiusProperty =
        BindableProperty.Create(nameof(BlurRadius), typeof(float), typeof(BlurHostView), 60f);

    public float BlurRadius
    {
        get => (float)GetValue(BlurRadiusProperty);
        set => SetValue(BlurRadiusProperty, value);
    }

    /// <summary>
    /// Color pre-filled into the blur node before recording content.
    /// Set this to your page background color so the blur has a dark base,
    /// producing a denser frosted effect. Default is transparent.
    /// </summary>
    public static readonly BindableProperty CaptureBackgroundProperty =
        BindableProperty.Create(nameof(CaptureBackground), typeof(Color), typeof(BlurHostView),
            Colors.Transparent);

    public Color CaptureBackground
    {
        get => (Color)GetValue(CaptureBackgroundProperty);
        set => SetValue(CaptureBackgroundProperty, value);
    }
}
