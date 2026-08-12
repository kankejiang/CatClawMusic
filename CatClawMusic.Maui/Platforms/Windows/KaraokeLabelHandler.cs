#if WINDOWS
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using WTextBlock = Microsoft.UI.Xaml.Controls.TextBlock;
using WSolidBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using WTextAlignment = Microsoft.UI.Xaml.TextAlignment;
using WColor = Windows.UI.Color;

namespace CatClawMusic.Maui.Platforms.Windows;

/// <summary>
/// Windows 平台 KaraokeLabel：TextBlock + 线性渐变前景实现卡拉OK逐字填充。
/// 与 Android 端 ClipRect 逐行裁剪同视觉：先整行未唱色，已唱色（TextColor）作为
/// LinearGradientBrush 从 0 到 progress 的硬边界渐变"从左往右刷过"，边界按进度
/// 连续移动（正在唱的字半亮半暗），progress 0→1 平滑推进。
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
            // FillProgress 单独处理：仅重建前景渐变，不触发 InvalidateMeasure
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

        // 先设 Text（会清空 Inlines），再按进度重建前景渐变
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

    /// <summary>FillProgress 变化：仅重建前景渐变（逐字填充左→右推进），不触发重测</summary>
    private static void MapFillProgress(KaraokeLabelHandler handler, Controls.KaraokeLabel view)
    {
        if (handler.PlatformView == null || view == null) return;
        ApplyFillProgress(handler.PlatformView, view.Text ?? string.Empty, view.FillProgress, view.TextColor, view.OutlineColor);
    }

    /// <summary>
    /// 逐字填充前景：LinearGradientBrush 在 progress 处硬切"已唱（TextColor）/未唱（OutlineColor）"，
    /// 边界随进度连续移动 → 已唱色从左往右刷过（与 Android ClipRect 裁剪同一视觉）。
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

        // 边界极端值：整行单色（避免零宽渐变段的渲染开销）
        if (progress <= 0.001)
        {
            tb.Foreground = new WSolidBrush(ToWColor(emptyColor));
            return;
        }
        if (progress >= 0.999)
        {
            tb.Foreground = new WSolidBrush(ToWColor(filledColor));
            return;
        }

        // 渐变相对文本包围盒（TextBlock 宽度=内容宽，与 Android 按行文本宽度裁剪一致）。
        // 两个 stop 同 offset → 硬边界：边界左侧已唱色、右侧未唱色，边界随 progress 平滑右移。
        var brush = new Microsoft.UI.Xaml.Media.LinearGradientBrush
        {
            StartPoint = new global::Windows.Foundation.Point(0, 0.5),
            EndPoint = new global::Windows.Foundation.Point(1, 0.5),
            MappingMode = Microsoft.UI.Xaml.Media.BrushMappingMode.RelativeToBoundingBox
        };
        brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Color = ToWColor(filledColor), Offset = progress });
        brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Color = ToWColor(emptyColor), Offset = progress });
        tb.Foreground = brush;
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
