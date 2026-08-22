using Vitrum.Android;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace Vitrum.Android.Handlers;

/// <summary>
/// MAUI handler for <see cref="BlurConsumerView"/>.
/// On connect, finds the nearest <see cref="BlurHostViewHandler"/> in the element tree
/// and registers the native view with its <see cref="BlurEngine"/>.
/// </summary>
public class BlurConsumerViewHandler : ContentViewHandler
{
    public static new IPropertyMapper<BlurConsumerView, BlurConsumerViewHandler> Mapper =
        new PropertyMapper<BlurConsumerView, BlurConsumerViewHandler>(ContentViewHandler.Mapper)
        {
            [nameof(BlurConsumerView.TintColor)] = MapTintColor,
            [nameof(BlurConsumerView.LiquidGlass)] = MapLiquidGlass,
            [nameof(BlurConsumerView.LiquidGlassCornerRadius)] = MapLiquidGlass,
            [nameof(BlurConsumerView.BlurEnabled)] = MapBlurEnabled,
        };

    public BlurConsumerViewHandler() : base(Mapper) { }

    protected override ContentViewGroup CreatePlatformView()
        => new NativeBlurConsumerView(Context);

    public new NativeBlurConsumerView PlatformView => (NativeBlurConsumerView)base.PlatformView;

    protected override void ConnectHandler(ContentViewGroup platformView)
    {
        base.ConnectHandler(platformView);
        var host = FindHostHandler();
        if (host != null)
        {
            PlatformView.AttachEngine(host.Engine);
            host.Engine.RegisterConsumer(PlatformView);
        }
    }

    protected override void DisconnectHandler(ContentViewGroup platformView)
    {
        var host = FindHostHandler();
        host?.Engine.UnregisterConsumer(PlatformView);
        PlatformView.DetachEngine();
        base.DisconnectHandler(platformView);
    }

    /// <summary>
    /// Walks up the MAUI element tree looking for a <see cref="BlurHostViewHandler"/>.
    /// Checks both direct ancestors and siblings at each level, so consumers can live
    /// arbitrarily deep inside a sibling of the host.
    /// </summary>
    BlurHostViewHandler? FindHostHandler()
    {
        var element = VirtualView?.Parent;
        while (element != null)
        {
            if (element.Handler is BlurHostViewHandler h) return h;

            if (element is Microsoft.Maui.Controls.Layout layout)
                foreach (var child in layout)
                    if (child.Handler is BlurHostViewHandler h2) return h2;

            element = element.Parent;
        }
        return null;
    }

    static void MapTintColor(BlurConsumerViewHandler handler, BlurConsumerView view)
        => handler.PlatformView.SetTintColor(view.TintColor.ToInt());

    static void MapLiquidGlass(BlurConsumerViewHandler handler, BlurConsumerView view)
    {
        float density = handler.Context.Resources!.DisplayMetrics!.Density;
        float cornerRadiusPx = view.LiquidGlassCornerRadius * density;
        handler.PlatformView.SetLiquidGlass(view.LiquidGlass, cornerRadiusPx);
    }

    static void MapBlurEnabled(BlurConsumerViewHandler handler, BlurConsumerView view)
        => handler.PlatformView.SetBlurEnabled(view.BlurEnabled);
}
