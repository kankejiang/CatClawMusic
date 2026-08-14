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

    /// <summary>按文本/字号/粗体/最大宽度创建（或复用）文本布局。Win2D 坐标均为 DIP。</summary>
    private CanvasTextLayout EnsureLayout(float maxWidth)
    {
        var view = VirtualView;
        var text = view.Text ?? "";
        var size = (float)view.FontSize;
        var bold = view.FontAttributes.HasFlag(FontAttributes.Bold);

        if (_layout != null && _layoutText == text && Math.Abs(_layoutFontSize - size) < 0.01f
            && _layoutBold == bold && Math.Abs(_layoutMaxWidth - maxWidth) < 0.5f)
            return _layout;

        _layout?.Dispose();
        // 布局内部**恒用左对齐**：Win2D 的 HorizontalAlignment 是按 maxWidth 计算对齐的，
        // 与控件实际渲染宽度不一致时，居中/居右会被整体右移出可视区。
        // 真正的对齐偏移在 OnDraw 里用控件实际宽手动计算（见 alignX），彻底避免该错位。
        var format = new CanvasTextFormat
        {
            FontSize = size,
            FontWeight = new global::Windows.UI.Text.FontWeight(bold ? (ushort)700 : (ushort)400),
            WordWrapping = CanvasWordWrapping.Wrap,
            HorizontalAlignment = CanvasHorizontalAlignment.Left,
        };        _layout = new CanvasTextLayout(
            CanvasDevice.GetSharedDevice(), text, format, maxWidth, 0);
        _layoutText = text;
        _layoutFontSize = size;
        _layoutBold = bold;
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

            // 用布局时的宽度约束创建/复用 layout（与 GetDesiredSize 一致，避免换行漂移）；
            // 从未布局时兜底用控件实际宽。
            var maxWidth = _layoutConstraint >= 0 ? _layoutConstraint : (float)Math.Max(0, sender.ActualWidth);
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

            // 水平对齐偏移：文字宽 < 控件宽（控件通常被外层设为满宽）时，
            // 按 HorizontalTextAlignment 把文字整体偏移到居中/居右位置；
            // 文字充满控件宽（换行铺满）时偏移为 0。与 Android StaticLayout 对齐行为一致。
            var controlW = (float)Math.Max(0, sender.ActualWidth);
            var availableW = controlW - padLeft - (float)view.Padding.Right;
            var alignX = 0f;
            switch (view.HorizontalTextAlignment)
            {
                case TextAlignment.Center:
                    alignX = Math.Max(0, (availableW - layoutW) / 2f);
                    break;
                case TextAlignment.End:
                    alignX = Math.Max(0, availableW - layoutW);
                    break;
            }

            // 1. 未唱色整行
            ds.DrawTextLayout(layout, padLeft + alignX, padTop, ToWColor(empty));

            // 2. 已唱色按进度从左到右裁剪（与 Android ClipRect 同构）。
            //    Win2D 1.3.2 裁剪用 CreateLayer(float, CanvasGeometry)：
            //    矩形几何裁剪 + 活动层（using 结束自动恢复绘制状态）。
            if (progress > 0.01f && layoutW > 0)
            {
                var fillX = (float)Math.Min(progress * layoutW, layoutW);
                using (var clipGeom = Microsoft.Graphics.Canvas.Geometry.CanvasGeometry.CreateRectangle(
                    CanvasDevice.GetSharedDevice(),
                    new global::Windows.Foundation.Rect(padLeft + alignX, padTop, fillX, layoutH)))
                using (var layer = ds.CreateLayer(1.0f, clipGeom))
                {
                    ds.DrawTextLayout(layout, padLeft + alignX, padTop, ToWColor(filled));
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
