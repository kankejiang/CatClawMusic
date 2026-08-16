#if WINDOWS
using CatClawMusic.Core.Interfaces;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using WColor = Windows.UI.Color;

namespace CatClawMusic.Maui.Platforms.Windows;

/// <summary>
/// Windows 平台 KaraokeLabel：Win2D CanvasControl 自绘，与 Android 端 KaraokePlatformView
/// （Canvas + ClipRect 逐行裁剪）完全同构：
/// 1. 未唱色（灰）绘制整行文本
/// 2. 已唱色（白）再次绘制，裁剪只露出左侧 [0, progress × 文本宽] → 已唱色从左往右
///    "刷"过，边界随进度连续移动（正在唱的字半亮半暗）。
/// 测量与绘制使用同一个 CanvasTextLayout（Win2D 引擎内自洽），不存在字体 fallback
/// 不一致 / 渐变坐标系 / Clip 坐标单位等 TextBlock 方案的坑。
/// </summary>
public class KaraokeLabelHandler : ViewHandler<Controls.KaraokeLabel, CanvasControl>
{
    private CanvasTextLayout? _layout;
    private string _layoutText = "";
    private float _layoutFontSize = -1;
    private bool _layoutBold;
    private CanvasHorizontalAlignment _layoutAlign = CanvasHorizontalAlignment.Left;
    private float _layoutMaxWidth = -1;
    /// <summary>最近一次布局的宽度约束：OnDraw 必须与布局用同一换行宽度，
    /// 否则（ActualWidth 与约束的浮点差异）会把最后一个字 Wrap 到第二行被裁剪。</summary>
    private float _layoutConstraint = -1;

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
            // FillProgress 单独处理：仅请求重绘，不触发布局重测
            [nameof(Controls.KaraokeLabel.FillProgress)] = MapFillProgress,
            [nameof(Controls.KaraokeLabel.HorizontalTextAlignment)] = MapAll,
            [nameof(Controls.KaraokeLabel.LineBreakMode)] = MapAll,
            [nameof(Controls.KaraokeLabel.Padding)] = MapAll,
        };

    public KaraokeLabelHandler() : base(Mapper) { }

    protected override CanvasControl CreatePlatformView()
    {
        var control = new CanvasControl();
        control.Draw += OnDraw;
        return control;
    }

    protected override void DisconnectHandler(CanvasControl platformView)
    {
        platformView.Draw -= OnDraw;
        _layout?.Dispose();
        _layout = null;
        base.DisconnectHandler(platformView);
    }

    private static void MapAll(KaraokeLabelHandler handler, Controls.KaraokeLabel view)
    {
        if (handler.PlatformView == null || view == null) return;
        handler.InvalidateLayoutAndRedraw();
    }

    /// <summary>FillProgress 变化：仅请求重绘（逐字填充左→右推进）</summary>
    private static void MapFillProgress(KaraokeLabelHandler handler, Controls.KaraokeLabel view)
    {
        if (handler.PlatformView == null || view == null) return;
        try { handler.PlatformView.Invalidate(); } catch { }
    }

    /// <summary>文本/字体属性变化：布局可能改变，重测 + 重绘</summary>
    private void InvalidateLayoutAndRedraw()
    {
        _layout?.Dispose();
        _layout = null;
        _layoutText = "";
        try { PlatformView?.Invalidate(); } catch { }
        try { PlatformView?.InvalidateMeasure(); } catch { }
    }

    /// <summary>测量：用 CanvasTextLayout 计算文本尺寸（DIP），支持换行。
    /// 记录宽度约束供 OnDraw 复用，保证布局/绘制换行一致。
    /// 按**文字内容宽**排布（短句=文字宽，长句=换行到约束宽）；
    /// 水平对齐（居左/居中/居右）在 OnDraw 里用控件实际宽与文字宽之差计算
    /// （见 alignX），标签内部自洽完成对齐，不依赖外层容器定位——
    /// 外层容器通常把标签设为满宽，避免 Windows 下内容宽标签在居中/居右时撑出窗口。</summary>
    public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
    {
        try
        {
            _layoutConstraint = (float)Math.Max(0, widthConstraint);
            var layout = EnsureLayout(_layoutConstraint);
            var pad = VirtualView.Padding;
            var w = layout.LayoutBounds.Width + pad.Left + pad.Right;
            var h = layout.LayoutBounds.Height + pad.Top + pad.Bottom;
            return new Size(Math.Min(w, widthConstraint > 0 ? widthConstraint : w), h);
        }
        catch
        {
            return new Size(0, 0);
        }
    }

    /// <summary>按文本/字号/粗体/对齐/最大宽度创建（或复用）文本布局。Win2D 坐标均为 DIP。
    /// 对齐由 CanvasTextLayout 内部完成（标签恒为满宽，layout maxWidth = 控件宽，坐标系一致）：
    /// 左/中/右对齐时 Win2D 在 layout 内按 maxWidth 对齐文本，与 Android StaticLayout 行为一致。
    /// 已唱填充的裁剪起点用 LayoutBounds.X（文本实际左缘，含对齐偏移），见 OnDraw。</summary>
    private CanvasTextLayout EnsureLayout(float maxWidth)
    {
        var view = VirtualView;
        var text = view.Text ?? "";
        var size = (float)view.FontSize;
        var bold = view.FontAttributes.HasFlag(FontAttributes.Bold);
        var align = view.HorizontalTextAlignment switch
        {
            TextAlignment.Start => CanvasHorizontalAlignment.Left,
            TextAlignment.End => CanvasHorizontalAlignment.Right,
            _ => CanvasHorizontalAlignment.Center
        };

        if (_layout != null && _layoutText == text && Math.Abs(_layoutFontSize - size) < 0.01f
            && _layoutBold == bold && _layoutAlign == align
            && Math.Abs(_layoutMaxWidth - maxWidth) < 0.5f)
            return _layout;

        _layout?.Dispose();
        var format = new CanvasTextFormat
        {
            FontSize = size,
            FontWeight = new global::Windows.UI.Text.FontWeight(bold ? (ushort)700 : (ushort)400),
            WordWrapping = CanvasWordWrapping.Wrap,
            HorizontalAlignment = align,
        };
        _layout = new CanvasTextLayout(
            CanvasDevice.GetSharedDevice(), text, format, maxWidth, 0);
        _layoutText = text;
        _layoutFontSize = size;
        _layoutBold = bold;
        _layoutAlign = align;
        _layoutMaxWidth = maxWidth;
        return _layout;
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        try
        {
            var view = VirtualView;
            if (view == null) return;
            var text = view.Text ?? "";
            if (text.Length == 0) return;

            // 用布局时的宽度约束创建/复用 layout，但**钳制为控件实际宽**：
            // StackLayout 交叉轴测量会给 GetDesiredSize 传 ∞ 约束（_layoutConstraint 可能为 ∞），
            // 若直接用 ∞ 创建 layout 会整句不换行 → 长句绘制宽度超出控件/容器。
            // 控件实际宽（ActualWidth）优先；布局约束较小（换行正确）时取约束。
            var controlW = (float)Math.Max(0, sender.ActualWidth);
            var maxWidth = controlW > 0 ? controlW : _layoutConstraint;
            if (_layoutConstraint > 0 && _layoutConstraint < maxWidth)
                maxWidth = _layoutConstraint;
            maxWidth = (float)Math.Max(0, maxWidth);
            var layout = EnsureLayout(maxWidth);
            var ds = args.DrawingSession;
            ds.Clear(global::Microsoft.UI.Colors.Transparent);

            var progress = Math.Clamp(view.FillProgress, 0.0, 1.0);

            // 已唱色：TextColor，透明则退 OutlineColor，再退白色（铁律：任何情况文字可见）
            var filled = view.TextColor;
            if (filled is null || filled.Alpha <= 0.01f)
                filled = view.OutlineColor;
            if (filled is null || filled.Alpha <= 0.01f)
                filled = Colors.White;

            // 未唱色：OutlineColor，透明则用已唱色的 55% 透明度
            var empty = view.OutlineColor;
            if (empty is null || empty.Alpha <= 0.01f)
                empty = new Color(filled.Red, filled.Green, filled.Blue, filled.Alpha * 0.55f);

            var padLeft = (float)view.Padding.Left;
            var padTop = (float)view.Padding.Top;
            var layoutW = (float)layout.LayoutBounds.Width;
            var layoutH = (float)layout.LayoutBounds.Height;
            // 文本实际左缘（相对 layout 原点）：左对齐=0；居中/居右时 Win2D 在 maxWidth
            // 内对齐文本 → LayoutBounds.X 给出对齐偏移。已唱填充与裁剪都从该左缘开始。
            var textLeft = (float)layout.LayoutBounds.X;

            // 1. 未唱色整行
            ds.DrawTextLayout(layout, padLeft, padTop, ToWColor(empty));

            // 2. 已唱色按进度从左到右**逐行**裁剪（与 Android 逐行着色一致）：
            //    长句换行成两行时，第一行先亮完、第二行才开始亮。
            //    之前用「整块矩形（宽度=进度×总宽，高度=全部行高）」裁剪——
            //    进度一半时第二行的左半也会被裁出已唱色，导致"第二行提前着色"。
            if (progress > 0.01f && layoutW > 0)
            {
                var totalChars = Math.Max(1, text.Length);
                var filledChars = progress * totalChars;
                float y = padTop;
                foreach (var lineM in layout.LineMetrics)
                {
                    var lineLen = Math.Max(1, lineM.Length);
                    var lineFilled = Math.Clamp(filledChars, 0, lineLen);
                    if (lineFilled > 0.01f)
                    {
                        // 该行填充宽度按行内字符比例（行宽近似 layoutW；短行仅轻微超前）
                        var lineProgress = (float)(lineFilled / lineLen);
                        var fillX = (float)Math.Min(lineProgress * layoutW, layoutW);
                        using (var clipGeom = Microsoft.Graphics.Canvas.Geometry.CanvasGeometry.CreateRectangle(
                            CanvasDevice.GetSharedDevice(),
                            new global::Windows.Foundation.Rect(padLeft + textLeft, y, fillX, lineM.Height)))
                        using (var layer = ds.CreateLayer(1.0f, clipGeom))
                        {
                            ds.DrawTextLayout(layout, padLeft, padTop, ToWColor(filled));
                        }
                    }
                    filledChars -= lineLen;
                    if (filledChars <= 0) break;
                    y += lineM.Height;
                }
            }
        }
        catch
        {
            // 绘制异常不影响歌词功能
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
