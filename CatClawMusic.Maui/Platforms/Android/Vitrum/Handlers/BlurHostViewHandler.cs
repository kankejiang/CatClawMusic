using Vitrum.Android;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace Vitrum.Android.Handlers;

/// <summary>
/// MAUI handler for <see cref="BlurHostView"/>.
/// Creates a <see cref="NativeBlurHostView"/> and exposes its <see cref="BlurEngine"/>
/// so consumer handlers can register with it.
/// </summary>
public class BlurHostViewHandler : ContentViewHandler
{
    public static new IPropertyMapper<BlurHostView, BlurHostViewHandler> Mapper =
        new PropertyMapper<BlurHostView, BlurHostViewHandler>(ContentViewHandler.Mapper)
        {
            [nameof(BlurHostView.BlurRadius)] = MapBlurRadius,
            [nameof(BlurHostView.CaptureBackground)] = MapCaptureBackground,
        };

    public BlurHostViewHandler() : base(Mapper) { }

    protected override ContentViewGroup CreatePlatformView()
        => new NativeBlurHostView(Context);

    public new NativeBlurHostView PlatformView => (NativeBlurHostView)base.PlatformView;

    /// <summary>The engine that captures the background and distributes the blurred texture.</summary>
    public BlurEngine Engine => PlatformView.Engine;

    static void MapBlurRadius(BlurHostViewHandler handler, BlurHostView view)
        => handler.Engine.SetBlurRadius(view.BlurRadius);

    static void MapCaptureBackground(BlurHostViewHandler handler, BlurHostView view)
        => handler.Engine.SetCaptureBackground(view.CaptureBackground.ToInt());
}
