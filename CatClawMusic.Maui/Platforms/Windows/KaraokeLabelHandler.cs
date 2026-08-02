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
            // FillProgress 单独处理：仅更新颜色/透明度，不触发 InvalidateMeasure
            [nameof(Controls.KaraokeLabel.FillProgress)] = MapFillProgress,
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

        ApplyFillProgress(tb, view.FillProgress, view.TextColor, view.OutlineColor);

        tb.Padding = new Microsoft.UI.Xaml.Thickness(
            view.Padding.Left, view.Padding.Top, view.Padding.Right, view.Padding.Bottom);

        tb.InvalidateMeasure();
    }

    /// <summary>FillProgress 变化：仅更新颜色和透明度，不触发重测</summary>
    private static void MapFillProgress(KaraokeLabelHandler handler, Controls.KaraokeLabel view)
    {
        if (handler.PlatformView == null || view == null) return;
        ApplyFillProgress(handler.PlatformView, view.FillProgress, view.TextColor, view.OutlineColor);
    }

    /// <summary>
    /// 前景色计算。两条铁律：
    ///
    /// 1. 绝不设置 tb.Opacity —— 那会覆盖 MAUI View.Opacity 属性绑定，
    ///    让上层 ViewModel 设的 Opacity 全部失效。
    /// 2. 前景永远基于 TextColor，绝不切换成 OutlineColor。
    ///    历史实现在 FillProgress&lt;0.5 时把前景换成 OutlineColor 再乘 0.45 alpha，
    ///    一旦调用方把 OutlineColor 设为 Transparent（常见做法，因为 Windows 本就不画描边），
    ///    整行文字会彻底隐形——这正是播放页"歌词只显示两行"的根因。
    ///
    /// 未唱行只做轻微减淡（0.75，下限有保证），任何情况下文字都可见。
    /// </summary>
    private static void ApplyFillProgress(WTextBlock tb, double fillProgress, Color textColor, Color outlineColor)
    {
        var progress = Math.Clamp(fillProgress, 0.0, 1.0);

        // TextColor 若被设成全透明，退回 OutlineColor；两者都透明则兜底为白色，保证可见
        var baseColor = textColor;
        if (baseColor is null || baseColor.Alpha <= 0.01f)
            baseColor = outlineColor;
        if (baseColor is null || baseColor.Alpha <= 0.01f)
            baseColor = Colors.White;

        var alphaScale = progress >= 0.5 ? 1.0f : 0.75f;
        tb.Foreground = new WSolidBrush(ToWColor(new Color(
            baseColor.Red, baseColor.Green, baseColor.Blue, baseColor.Alpha * alphaScale)));
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
