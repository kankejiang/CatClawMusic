#if WINDOWS
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using WGrid = Microsoft.UI.Xaml.Controls.Grid;
using WRectangle = Microsoft.UI.Xaml.Shapes.Rectangle;
using WSolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using WColor = Windows.UI.Color;
using CatClawMusic.Maui.Controls;

namespace CatClawMusic.Maui.Platforms.Windows;

/// <summary>
/// Windows 端 BackdropBlur 兜底 handler：Windows 竖屏场景不追求真模糊，
/// 退化为半透明卡片底色（透出下层内容），避免破坏布局并保证任何平台都能运行。
/// </summary>
public class BackdropBlurHandler : ViewHandler<BackdropBlur, WGrid>
{
    public static IPropertyMapper<BackdropBlur, BackdropBlurHandler> Mapper =
        new PropertyMapper<BackdropBlur, BackdropBlurHandler>(ViewMapper)
        {
            [nameof(BackdropBlur.BlurRadius)] = MapStyle,
            [nameof(BackdropBlur.Target)] = MapStyle,
        };

    private readonly WRectangle _surface = new();

    public BackdropBlurHandler() : base(Mapper) { }

    protected override WGrid CreatePlatformView()
    {
        var grid = new WGrid();
        _surface.Fill = new WSolidColorBrush(WColor.FromArgb(230, 11, 13, 32));
        _surface.IsHitTestVisible = false;
        grid.Children.Add(_surface);
        return grid;
    }

    private static void MapStyle(BackdropBlurHandler handler, BackdropBlur view)
    {
        // 半透明卡片底色兜底：透出下层内容同时保证分层清晰
        handler._surface.Fill = new WSolidColorBrush(WColor.FromArgb(225, 11, 13, 32));
    }
}
#endif