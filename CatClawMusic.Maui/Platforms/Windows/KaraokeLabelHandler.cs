#if WINDOWS
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using WGrid = Microsoft.UI.Xaml.Controls.Grid;
using WTextBlock = Microsoft.UI.Xaml.Controls.TextBlock;
using WSolidBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using WTextAlignment = Microsoft.UI.Xaml.TextAlignment;
using WColor = Windows.UI.Color;

namespace CatClawMusic.Maui.Platforms.Windows;

/// <summary>
/// Windows 平台 KaraokeLabel：双层 TextBlock + Clip 裁剪实现卡拉OK逐字填充。
/// 与 Android 端 canvas.ClipRect 逐行裁剪完全同构：
/// - 底层 _baseText：未唱色（灰）完整文本
/// - 上层 _fillText：已唱色（白）完整文本，叠加其上，Clip 只露出左侧 [0, fillXPx]
/// 边界 = 字符右缘表映射（Win2D 逐前缀测量，缓存），逐字从左往右推进，
/// 当前字内部按宽度比例平滑过渡（半亮半暗）。
/// Clip 是确定性的像素裁剪，不依赖渐变坐标系 → 修复"一行里多字同时着色"。
/// 未唱色透明时回退为已唱色的 55% 透明度，保证任何情况下文字可见。
/// </summary>
public class KaraokeLabelHandler : ViewHandler<Controls.KaraokeLabel, WGrid>
{
    private WTextBlock _baseText = null!;
    private WTextBlock _fillText = null!;

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
            // FillProgress 单独处理：仅更新上层 Clip，不触发 InvalidateMeasure
            [nameof(Controls.KaraokeLabel.FillProgress)] = MapFillProgress,
            [nameof(Controls.KaraokeLabel.HorizontalTextAlignment)] = MapAll,
            [nameof(Controls.KaraokeLabel.LineBreakMode)] = MapAll,
            [nameof(Controls.KaraokeLabel.Padding)] = MapAll,
        };

    public KaraokeLabelHandler() : base(Mapper) { }

    protected override WGrid CreatePlatformView()
    {
        var grid = new WGrid();
        // 两层同格完全重叠：未唱层在底、已唱层在上（Clip 裁剪露出左侧已唱部分）
        _baseText = new WTextBlock { TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap };
        _fillText = new WTextBlock { TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap };
        grid.Children.Add(_baseText);
        grid.Children.Add(_fillText);
        return grid;
    }

    private static void MapAll(KaraokeLabelHandler handler, Controls.KaraokeLabel view)
    {
        if (handler.PlatformView == null || view == null) return;
        var baseText = handler._baseText;
        var fillText = handler._fillText;
        if (baseText == null || fillText == null) return;

        var text = view.Text ?? string.Empty;
        var fontFamily = new Microsoft.UI.Xaml.Media.FontFamily(
            string.IsNullOrEmpty(view.FontFamily) ? "OpenSansSemibold" : view.FontFamily);
        var alignment = view.HorizontalTextAlignment switch
        {
            TextAlignment.Start => WTextAlignment.Left,
            TextAlignment.End => WTextAlignment.Right,
            _ => WTextAlignment.Center
        };
        var padding = new Microsoft.UI.Xaml.Thickness(
            view.Padding.Left, view.Padding.Top, view.Padding.Right, view.Padding.Bottom);

        foreach (var tb in new[] { baseText, fillText })
        {
            tb.Text = text;
            tb.FontSize = view.FontSize;
            tb.FontFamily = fontFamily;
            tb.TextAlignment = alignment;
            tb.Padding = padding;
        }

        ApplyFillProgress(handler, view, text);

        handler.PlatformView.InvalidateMeasure();
    }

    /// <summary>FillProgress 变化：仅更新上层已唱层的 Clip（逐字填充左→右推进），不触发重测</summary>
    private static void MapFillProgress(KaraokeLabelHandler handler, Controls.KaraokeLabel view)
    {
        if (handler.PlatformView == null || view == null || handler._fillText == null) return;
        ApplyFillProgress(handler, view, view.Text ?? string.Empty);
    }

    /// <summary>字符右缘位置缓存（文本+字号+字体 → 每字符右缘，DIP）。同一行歌词每 tick 命中。</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, double[]> CharEdgeCache = new();

    /// <summary>
    /// 应用逐字填充：底层涂未唱色、上层涂已唱色并裁剪出 [0, fillXPx]。
    /// 边界位置 = 字符右缘表映射：前 n 个字符的实际宽度和 + 当前字符内部按宽度比例渐变。
    ///
    /// ⚠ 元素框校准：Clip 相对 TextBlock 自身坐标（物理像素）。若元素被拉伸到列宽
    /// （宽于文本），按对齐方式把文本起点偏移计入，保证边界落在文本实际位置上。
    ///
    /// 铁律：
    /// 1. 绝不设置 TextBlock.Opacity —— 那会覆盖 MAUI View.Opacity 属性绑定。
    /// 2. TextColor 被设成全透明时退回 OutlineColor；OutlineColor 也透明时
    ///    未唱段用 TextColor 的 55% 透明度（任何情况下文字都可见）。
    /// </summary>
    private static void ApplyFillProgress(KaraokeLabelHandler handler, Controls.KaraokeLabel view, string text)
    {
        var progress = Math.Clamp(view.FillProgress, 0.0, 1.0);

        // 已唱色：TextColor，透明则退 OutlineColor，再退白色
        var filledColor = view.TextColor;
        if (filledColor is null || filledColor.Alpha <= 0.01f)
            filledColor = view.OutlineColor;
        if (filledColor is null || filledColor.Alpha <= 0.01f)
            filledColor = Colors.White;

        // 未唱色：OutlineColor，透明则用已唱色的 55% 透明度（保持可读）
        var emptyColor = view.OutlineColor;
        if (emptyColor is null || emptyColor.Alpha <= 0.01f)
            emptyColor = new Color(filledColor.Red, filledColor.Green, filledColor.Blue, filledColor.Alpha * 0.55f);

        handler._baseText.Foreground = new WSolidBrush(ToWColor(emptyColor));
        handler._fillText.Foreground = new WSolidBrush(ToWColor(filledColor));

        var fillText = handler._fillText;
        if (string.IsNullOrEmpty(text))
        {
            fillText.Clip = null;
            return;
        }

        // 极端进度：整行单色（清除/全露 Clip）
        if (progress <= 0.001)
        {
            fillText.Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry
            {
                Rect = new global::Windows.Foundation.Rect(0, -1e6, 0.01, 2e6)
            };
            return;
        }
        if (progress >= 0.999)
        {
            fillText.Clip = null;
            return;
        }

        try
        {
            var scale = fillText.XamlRoot?.RasterizationScale ?? 1.0;
            var edges = GetCharRightEdges(handler._baseText, text);
            var totalWidthDp = edges.Length > 0 ? edges[^1] : 0.0;
            if (totalWidthDp <= 0)
            {
                fillText.Clip = null;
                return;
            }

            // 字符比例 → 文本内像素位置（DIP）→ 物理像素
            var fillXPx = CharFractionToPixels(edges, progress) * scale;
            var textWidthPx = totalWidthDp * scale;

            // 元素被拉伸（宽于文本）时，把文本起点偏移计入（按对齐方式）
            var elementWidth = fillText.ActualWidth;
            double leftOffset = 0;
            if (elementWidth > textWidthPx + 1 && textWidthPx > 0)
            {
                leftOffset = view.HorizontalTextAlignment switch
                {
                    TextAlignment.Start => 0,
                    TextAlignment.End => elementWidth - textWidthPx,
                    _ => (elementWidth - textWidthPx) / 2
                };
            }

            fillText.Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry
            {
                Rect = new global::Windows.Foundation.Rect(leftOffset, -1e6, fillXPx, 2e6)
            };
        }
        catch
        {
            fillText.Clip = null;
        }
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
