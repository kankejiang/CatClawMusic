#if WINDOWS
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using WTextBlock = Microsoft.UI.Xaml.Controls.TextBlock;
using WSolidBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using WTextAlignment = Microsoft.UI.Xaml.TextAlignment;
using WColor = Windows.UI.Color;

namespace CatClawMusic.Maui.Platforms.Windows;

/// <summary>
/// Windows 平台 KaraokeLabel：TextBlock + 逐字 Run 着色实现卡拉OK填充。
/// FillProgress ∈ [0,1]：按字符数把文本切成"已唱（TextColor）"与"未唱（OutlineColor）"
/// 两段 Run，实现真正逐字渐进效果（Android 端 Canvas 描边+实心同理，仅绘制手段不同）。
/// 未唱色透明时回退为已唱色的 55% 透明度，保证任何情况下文字可见。
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
            // FillProgress 单独处理：仅重建前景 Run，不触发 InvalidateMeasure
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

        // 先设 Text（会清空 Inlines），再按进度重建前景 Run
        tb.Text = view.Text ?? string.Empty;
        tb.FontSize = view.FontSize;
        tb.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(string.IsNullOrEmpty(view.FontFamily) ? "OpenSansSemibold" : view.FontFamily);

        tb.TextAlignment = view.HorizontalTextAlignment switch
        {
            TextAlignment.Start => WTextAlignment.Left,
            TextAlignment.End => WTextAlignment.Right,
            _ => WTextAlignment.Center
        };

        ApplyFillProgress(tb, view.Text ?? string.Empty, view.FillProgress, view.TextColor, view.OutlineColor);

        tb.Padding = new Microsoft.UI.Xaml.Thickness(
            view.Padding.Left, view.Padding.Top, view.Padding.Right, view.Padding.Bottom);

        tb.InvalidateMeasure();
    }

    /// <summary>FillProgress 变化：仅重建前景 Run（逐字着色），不触发重测</summary>
    private static void MapFillProgress(KaraokeLabelHandler handler, Controls.KaraokeLabel view)
    {
        if (handler.PlatformView == null || view == null) return;
        ApplyFillProgress(handler.PlatformView, view.Text ?? string.Empty, view.FillProgress, view.TextColor, view.OutlineColor);
    }

    /// <summary>
    /// 逐字填充前景色：已唱字符用 TextColor，未唱字符用 OutlineColor。
    /// 铁律：
    /// 1. 绝不设置 tb.Opacity —— 那会覆盖 MAUI View.Opacity 属性绑定。
    /// 2. TextColor 被设成全透明时退回 OutlineColor；OutlineColor 也透明时
    ///    未唱段用 TextColor 的 55% 透明度（任何情况下文字都可见）。
    /// </summary>
    private static void ApplyFillProgress(WTextBlock tb, string text, double fillProgress, Color textColor, Color outlineColor)
    {
        var progress = Math.Clamp(fillProgress, 0.0, 1.0);

        // 已唱色：TextColor，透明则退 OutlineColor，再退白色
        var filledColor = textColor;
        if (filledColor is null || filledColor.Alpha <= 0.01f)
            filledColor = outlineColor;
        if (filledColor is null || filledColor.Alpha <= 0.01f)
            filledColor = Colors.White;

        // 未唱色：OutlineColor，透明则用已唱色的 55% 透明度（保持可读）
        var emptyColor = outlineColor;
        if (emptyColor is null || emptyColor.Alpha <= 0.01f)
            emptyColor = new Color(filledColor.Red, filledColor.Green, filledColor.Blue, filledColor.Alpha * 0.55f);

        if (string.IsNullOrEmpty(text))
        {
            tb.Foreground = new WSolidBrush(ToWColor(filledColor));
            return;
        }

        // 逐字：按字符比例切分已唱/未唱两段 Run（0=全未唱，1=全已唱）
        var filled = (int)Math.Round(text.Length * progress);
        if (filled <= 0)
        {
            tb.Foreground = new WSolidBrush(ToWColor(emptyColor));
            return;
        }
        if (filled >= text.Length)
        {
            tb.Foreground = new WSolidBrush(ToWColor(filledColor));
            return;
        }

        tb.Inlines.Clear();
        tb.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
        {
            Text = text[..filled],
            Foreground = new WSolidBrush(ToWColor(filledColor))
        });
        tb.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
        {
            Text = text[filled..],
            Foreground = new WSolidBrush(ToWColor(emptyColor))
        });
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
