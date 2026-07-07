#if WINDOWS
using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Color = Microsoft.Maui.Graphics.Color;
using WColor = Windows.UI.Color;
using WBorder = Microsoft.UI.Xaml.Controls.Border;
using WBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;

namespace CatClawMusic.Maui.Platforms.Windows;

public class FrostedBackgroundHandler : ViewHandler<Controls.FrostedBackground, WBorder>
{
    public static IPropertyMapper<Controls.FrostedBackground, FrostedBackgroundHandler> Mapper =
        new PropertyMapper<Controls.FrostedBackground, FrostedBackgroundHandler>(ViewMapper)
        {
            [nameof(Controls.FrostedBackground.TintColor)] = MapTintColor,
            [nameof(Controls.FrostedBackground.DimAmount)] = MapTintColor,
        };

    public FrostedBackgroundHandler() : base(Mapper)
    {
    }

    protected override WBorder CreatePlatformView()
    {
        return new WBorder
        {
            Background = new WBrush(WColor.FromArgb(255, 11, 13, 32)),
        };
    }

    private static void MapTintColor(FrostedBackgroundHandler handler, Controls.FrostedBackground view)
    {
        if (handler.PlatformView == null) return;
        try
        {
            var tint = view.TintColor;
            var dim = view.DimAmount;
            byte r = (byte)(tint.Red * 255 * (1 - dim) + 11 * dim);
            byte g = (byte)(tint.Green * 255 * (1 - dim) + 13 * dim);
            byte b = (byte)(tint.Blue * 255 * (1 - dim) + 32 * dim);
            handler.PlatformView.Background = new WBrush(WColor.FromArgb(255, r, g, b));
        }
        catch { }
    }
}
#endif
