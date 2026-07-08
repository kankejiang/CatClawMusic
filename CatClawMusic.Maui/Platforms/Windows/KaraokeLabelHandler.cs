#if WINDOWS
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using WTextBlock = Microsoft.UI.Xaml.Controls.TextBlock;
using WSolidBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using WTextAlignment = Microsoft.UI.Xaml.TextAlignment;
using WColor = Windows.UI.Color;

namespace CatClawMusic.Maui.Platforms.Windows;

/// <summary>
/// Windows 平台 KaraokeLabel 回退实现：用 TextBlock 绘制。
/// FillProgress &lt; 0.5 时用透明度模拟"未唱"效果，&gt;= 0.5 时完全实心。
/// Windows 不支持原生空心描边文字，此为近似回退。
/// </summary>
public class KaraokeLabelHandler : ViewHandler<Controls.KaraokeLabel, WTextBlock>
{
    public static IPropertyMapper<Controls.KaraokeLabel, KaraokeLabelHandler> Mapper =
        new PropertyMapper<Controls.KaraokeLabel, KaraokeLabelHandler>(ViewMapper)
        {
            [nameof(Controls.KaraokeLabel.Text)] = MapAll,
            [nameof(Controls.KaraokeLabel.FontSize)] = MapAll,
            [nameof(Controls.KaraokeLabel.FontFamily)] = MapAll,
            [nameof(Controls.KaraokeLabel.FontAttributes)] = MapAll,
            [nameof(Controls.KaraokeLabel.TextColor)] = MapAll,
            [nameof(Controls.KaraokeLabel.OutlineColor)] = MapAll,
            [nameof(Controls.KaraokeLabel.StrokeWidth)] = MapAll,
            [nameof(Controls.KaraokeLabel.FillProgress)] = MapAll,
            [nameof(Controls.KaraokeLabel.HorizontalTextAlignment)] = MapAll,
            [nameof(Controls.KaraokeLabel.LineBreakMode)] = MapAll,
            [nameof(Controls.KaraokeLabel.Padding)] = MapAll,
        };

    public KaraokeLabelHandler() : base(Mapper) { }

    protected override WTextBlock CreatePlatformView()
    {
        return new WTextBlock
        {
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            TextAlignment = WTextAlignment.Center
        };
    }

    private static void MapAll(KaraokeLabelHandler handler, Controls.KaraokeLabel view)
    {
        if (handler.PlatformView == null || view == null) return;
        var tb = handler.PlatformView;

        tb.Text = view.Text ?? string.Empty;
        tb.FontSize = view.FontSize;
        tb.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(string.IsNullOrEmpty(view.FontFamily) ? "OpenSansSemibold" : view.FontFamily);

        tb.TextAlignment = view.HorizontalTextAlignment switch
        {
            TextAlignment.Start => WTextAlignment.Left,
            TextAlignment.End => WTextAlignment.Right,
            _ => WTextAlignment.Center
        };

        var progress = Math.Clamp(view.FillProgress, 0.0, 1.0);
        var color = progress >= 0.5
            ? ToWColor(view.TextColor)
            : ToWColor(view.OutlineColor);
        tb.Foreground = new WSolidBrush(color);

        // 未唱行降低透明度近似模拟"空心"弱化效果
        tb.Opacity = progress >= 0.5 ? 1.0 : 0.45;

        tb.Padding = new Microsoft.UI.Xaml.Thickness(
            view.Padding.Left, view.Padding.Top, view.Padding.Right, view.Padding.Bottom);

        tb.InvalidateMeasure();
    }

    private static WColor ToWColor(Color color)
    {
        return WColor.FromArgb(
            (byte)(color.Alpha * 255),
            (byte)(color.Red * 255),
            (byte)(color.Green * 255),
            (byte)(color.Blue * 255));
    }
}
#endif
