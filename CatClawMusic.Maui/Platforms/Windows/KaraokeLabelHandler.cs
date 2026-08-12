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

    /// <summary>字符右缘位置缓存（文本+字号+字体 → 每字符右缘，DIP）。同一行歌词每 tick 命中。</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, double[]> CharEdgeCache = new();

    /// <summary>
    /// 逐字填充前景：LinearGradientBrush 在 progress 处硬切"已唱（TextColor）/未唱（OutlineColor）"，
    /// 边界随进度连续移动 → 已唱色从左往右刷过（与 Android ClipRect 裁剪同一视觉）。
    ///
    /// ⚠ 关键校准：
    /// ① 渐变 RelativeToBoundingBox 相对的是 **TextBlock 元素框**（歌词行整列宽），不是文本本身——
    ///    必须把边界换算到文本实际宽度上（否则边界按列宽推进会"一次跨过多个字"）。
    /// ② 逐字进度是"字符比例"（filledChars / totalChars），但字符**不等宽**（空格/窄符号/全角标点）——
    ///    若用"字符比例 × 文本总宽"映射像素，边界会与字符实际位置错位。
    ///    这里用 Win2D 逐前缀测量每个字符的右缘（缓存），把字符比例精确映射到像素位置：
    ///    边界 = 前 n 个字符的实际宽度和 + 当前字符内部按宽度比例渐变。
    ///
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

        // 字符比例 → 文本内像素位置（按字符实际宽度，缓存字符右缘表）
        var visualOffset = progress;
        try
        {
            var elementWidth = tb.ActualWidth;
            var scale = tb.XamlRoot?.RasterizationScale ?? 1.0;
            if (elementWidth > 1)
            {
                var edges = GetCharRightEdges(tb, text);
                var totalWidth = edges.Length > 0 ? edges[^1] : 0.0;
                if (totalWidth > 0)
                {
                    var fillX = CharFractionToPixels(edges, progress);   // DIP
                    visualOffset = fillX * scale / elementWidth;
                }
            }
        }
        catch { }
        visualOffset = Math.Clamp(visualOffset, 0.0, 1.0);

        // 两个 stop 同 offset → 硬边界：边界左侧已唱色、右侧未唱色，边界随进度平滑右移。
        var brush = new Microsoft.UI.Xaml.Media.LinearGradientBrush
        {
            StartPoint = new global::Windows.Foundation.Point(0, 0.5),
            EndPoint = new global::Windows.Foundation.Point(1, 0.5),
            MappingMode = Microsoft.UI.Xaml.Media.BrushMappingMode.RelativeToBoundingBox
        };
        brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Color = ToWColor(filledColor), Offset = visualOffset });
        brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Color = ToWColor(emptyColor), Offset = visualOffset });
        tb.Foreground = brush;
    }

    /// <summary>字符比例 → 文本内像素位置（DIP）：前 n 个字符的实际宽度和 + 当前字符内部按宽度比例渐变。
    /// 字符边界处硬切（前字全亮），字符内部平滑过渡（正在唱的字半亮半暗）——空格/符号作为独立
    /// 字符单元宽度精确，穿过时不会影响前后字的着色状态。</summary>
    private static double CharFractionToPixels(double[] edges, double progress)
    {
        if (edges.Length == 0) return 0;
        if (progress <= 0) return 0;
        if (progress >= 1) return edges[^1];

        var charF = progress * edges.Length;
        var idx = (int)charF;
        if (idx >= edges.Length) return edges[^1];
        var frac = charF - idx;
        var left = idx == 0 ? 0 : edges[idx - 1];
        var right = edges[idx];
        return left + (right - left) * frac;
    }

    /// <summary>用 Win2D 逐前缀测量每个字符的右缘（DIP），与 TextBlock 同字体/字号。结果按
    /// 文本+字号+字体缓存（同一行歌词每 tick 命中，避免重复测量）。</summary>
    private static double[] GetCharRightEdges(WTextBlock tb, string text)
    {
        var key = tb.FontSize.ToString("F1") + "|" + (tb.FontFamily?.Source ?? "") + "|" + text;
        if (CharEdgeCache.TryGetValue(key, out var cached))
            return cached;
        if (CharEdgeCache.Count > 128)
            CharEdgeCache.Clear();

        var edges = new double[text.Length];
        var format = new Microsoft.Graphics.Canvas.Text.CanvasTextFormat
        {
            FontSize = (float)tb.FontSize,
            FontFamily = string.IsNullOrEmpty(tb.FontFamily?.Source) ? "Segoe UI" : tb.FontFamily!.Source,
        };
        var device = Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice();
        for (int i = 0; i < text.Length; i++)
        {
            using var layout = new Microsoft.Graphics.Canvas.Text.CanvasTextLayout(
                device, text[..(i + 1)], format, 0, 0);
            edges[i] = layout.LayoutBounds.Width;
        }
        CharEdgeCache[key] = edges;
        return edges;
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
