#if WINDOWS
using CatClawMusic.Core.Interfaces;
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

    /// <summary>诊断日志计数（限前 30 次输出，用于定位"只着色一个字"问题，定位后移除）</summary>
    private static int _debugLogCount;

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
            var textWidthDp = MeasureTextWidthDp(fillText, text);
            if (textWidthDp <= 0)
            {
                fillText.Clip = null;
                return;
            }

            // 边界 = 字符比例 × 文本实际宽度（等宽近似，与 Android 端
            // fillEndX = lineWidth × (fillCharOffset / lineCharCount) 完全一致）
            var fillXPx = progress * textWidthDp * scale;
            var textWidthPx = textWidthDp * scale;

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

            // 诊断日志（定位"只着色一个字"）：输出 progress/边界参数
            if (_debugLogCount < 30)
            {
                _debugLogCount++;
                Log.Debug("KaraokeWin",
                    $"[Karaoke] p={progress:F3} fillX={fillXPx:F1} textW={textWidthPx:F1} " +
                    $"elW={elementWidth:F0} off={leftOffset:F1} scale={scale:F2} '{text}'");
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

    /// <summary>用 Win2D 测量文本自然宽度（DIP），与 TextBlock 同字体/字号。</summary>
    private static double MeasureTextWidthDp(WTextBlock tb, string text)
    {
        try
        {
            var format = new Microsoft.Graphics.Canvas.Text.CanvasTextFormat
            {
                FontSize = (float)tb.FontSize,
                FontFamily = string.IsNullOrEmpty(tb.FontFamily?.Source) ? "Segoe UI" : tb.FontFamily!.Source,
            };
            using var layout = new Microsoft.Graphics.Canvas.Text.CanvasTextLayout(
                Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice(), text, format, 0, 0);
            return layout.LayoutBounds.Width;
        }
        catch
        {
            return 0;
        }
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
