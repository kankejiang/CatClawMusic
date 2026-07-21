#if ANDROID
using Android.Content;
using Android.Graphics;
using Android.Text;
using Android.Views;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using AColor = Android.Graphics.Color;
using APaint = Android.Graphics.Paint;
using AView = Android.Views.View;
using ALayout = Android.Text.Layout;
using MColor = Microsoft.Maui.Graphics.Color;
using MTextAlignment = Microsoft.Maui.TextAlignment;

namespace CatClawMusic.Maui.Platforms.Android;

/// <summary>
/// Android 平台 KaraokeLabel 渲染器：用 Canvas 绘制空心描边（未唱）与实心填充（已唱）。
/// FillProgress = 0 时纯空心，= 1 时纯实心，中间值时从左到右渐进填充。
/// 支持居左/居中/居右对齐，裁剪区域根据实际文字边界计算。
/// </summary>
public class KaraokeLabelHandler : ViewHandler<Controls.KaraokeLabel, KaraokePlatformView>
{
    public static IPropertyMapper<Controls.KaraokeLabel, KaraokeLabelHandler> Mapper =
        new PropertyMapper<Controls.KaraokeLabel, KaraokeLabelHandler>(ViewMapper)
        {
            [nameof(Controls.KaraokeLabel.Text)] = MapSync,
            [nameof(Controls.KaraokeLabel.FontSize)] = MapSync,
            [nameof(Controls.KaraokeLabel.FontFamily)] = MapSync,
            [nameof(Controls.KaraokeLabel.FontAttributes)] = MapSync,
            [nameof(Controls.KaraokeLabel.TextColor)] = MapSync,
            [nameof(Controls.KaraokeLabel.OutlineColor)] = MapSync,
            [nameof(Controls.KaraokeLabel.StrokeWidth)] = MapSync,
            // FillProgress 只触发重绘，不触发重测布局（避免频繁分配 StaticLayout 导致内存崩溃）
            [nameof(Controls.KaraokeLabel.FillProgress)] = MapFillProgress,
            [nameof(Controls.KaraokeLabel.HorizontalTextAlignment)] = MapSync,
            [nameof(Controls.KaraokeLabel.LineBreakMode)] = MapSync,
            [nameof(Controls.KaraokeLabel.Padding)] = MapSync,
        };

    public KaraokeLabelHandler() : base(Mapper) { }

    protected override KaraokePlatformView CreatePlatformView() => new(Context!);

    private static void MapSync(KaraokeLabelHandler handler, Controls.KaraokeLabel view)
    {
        handler.PlatformView?.SyncFromVirtual(view);
    }

    /// <summary>FillProgress 变化：仅重绘，不重测布局</summary>
    private static void MapFillProgress(KaraokeLabelHandler handler, Controls.KaraokeLabel view)
    {
        handler.PlatformView?.UpdateFillProgress(view);
    }
}

/// <summary>
/// Android 原生绘制视图：负责实际的 Canvas 文字绘制。
/// 用 StaticLayout 测量换行与行位置，逐行绘制空心+实心两层。
/// </summary>
public class KaraokePlatformView : AView
{
    private Controls.KaraokeLabel? _view;
    private readonly APaint _paint = new() { AntiAlias = true };
    private TextPaint? _measurePaint;
    private StaticLayout? _layout;
    // Typeface 缓存：OnDraw 每帧都会调 ConfigurePaint，Typeface.Create 每次都做
    // JNI 字符串封送 + 字体查找，按 family+bold 缓存避免重复开销
    private Typeface? _cachedTypeface;
    private string? _cachedTypefaceFamily;
    private bool _cachedTypefaceBold;

    public KaraokePlatformView(Context context) : base(context)
    {
        _measurePaint = new TextPaint { AntiAlias = true };
    }

    /// <summary>从虚拟视图同步属性并触发重绘/重测</summary>
    public void SyncFromVirtual(Controls.KaraokeLabel view)
    {
        _view = view;
        RequestLayout();
        Invalidate();
    }

    /// <summary>仅更新填充进度并重绘（不重测布局，避免频繁分配 StaticLayout）</summary>
    public void UpdateFillProgress(Controls.KaraokeLabel view)
    {
        _view = view;
        Invalidate();
    }

    private float Density => Resources?.DisplayMetrics?.Density ?? 1f;

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        if (_view == null)
        {
            base.OnMeasure(widthMeasureSpec, heightMeasureSpec);
            return;
        }

        var density = Density;
        var width = MeasureSpec.GetSize(widthMeasureSpec);
        var padding = _view.Padding;
        var paddingLeft = (float)padding.Left * density;
        var paddingRight = (float)padding.Right * density;
        var paddingTop = (float)padding.Top * density;
        var paddingBottom = (float)padding.Bottom * density;

        var availableWidth = width - paddingLeft - paddingRight;
        if (availableWidth <= 0) availableWidth = Math.Max(width, 1);

        ConfigurePaint(_measurePaint!);
        var text = _view.Text ?? string.Empty;
        var layoutWidth = (int)Math.Ceiling(availableWidth);
        if (layoutWidth <= 0) layoutWidth = 1;

        var oldLayout = _layout;
        _layout = new StaticLayout(text, _measurePaint!, layoutWidth, ToLayoutAlignment(_view.HorizontalTextAlignment), 1f, 0f, false);
        oldLayout?.Dispose();

        var desiredHeight = _layout.Height + paddingTop + paddingBottom;
        var finalWidth = ResolveSize(width > 0 ? width : (int)(_layout.Width + paddingLeft + paddingRight), widthMeasureSpec);
        var finalHeight = ResolveSize((int)desiredHeight, heightMeasureSpec);
        SetMeasuredDimension(finalWidth, finalHeight);
    }

    protected override void OnDraw(Canvas? canvas)
    {
        if (canvas == null || _view == null || _layout == null) return;

        var density = Density;
        var padding = _view.Padding;
        var paddingTop = (float)padding.Top * density;
        var paddingLeft = (float)padding.Left * density;
        var textColor = ToAndroidColor(_view.TextColor);
        var dimColor = ToAndroidColor(_view.OutlineColor);
        var progress = (float)Math.Clamp(_view.FillProgress, 0.0, 1.0);

        ConfigurePaint(_paint);
        var text = _view.Text ?? string.Empty;

        canvas.Save();
        canvas.Translate(paddingLeft, paddingTop);

        // 1) 先画未唱部分：浅灰色实心文字（整行）
        _paint.Color = dimColor;
        _paint.SetStyle(APaint.Style.Fill);
        DrawAllLines(canvas, null);

        // 2) 再画已唱部分：亮白色实心文字，按进度从左到右裁剪填充
        if (progress > 0.01f)
        {
            _paint.Color = textColor;
            DrawAllLines(canvas, progress);
        }

        canvas.Restore();
    }

    /// <summary>
    /// 绘制所有行。若 fillProgress 为 null，绘制整行（未唱浅色层）；
    /// 若 fillProgress 有值，按总字符进度逐行填充已唱亮色层（Apple Music 风格：先唱完一行再唱下一行）。
    /// </summary>
    private void DrawAllLines(Canvas canvas, float? fillProgress)
    {
        if (!fillProgress.HasValue)
        {
            for (int i = 0; i < _layout!.LineCount; i++)
            {
                var lineLeft = _layout.GetLineLeft(i);
                var lineBaseline = _layout.GetLineTop(i) - _paint.Ascent();
                var start = _layout.GetLineStart(i);
                var end = _layout.GetLineEnd(i);
                var lineWidth = _layout.GetLineWidth(i);
                if (end <= start || start >= (_view?.Text?.Length ?? 0)) continue;
                end = Math.Min(end, _view!.Text?.Length ?? end);
                canvas.DrawText(_view.Text!, start, end, lineLeft, lineBaseline, _paint);
            }
            return;
        }

        var progress = Math.Clamp(fillProgress.Value, 0f, 1f);
        var totalChars = 0;
        for (int i = 0; i < _layout!.LineCount; i++)
        {
            var start = _layout.GetLineStart(i);
            var end = _layout.GetLineEnd(i);
            if (end > start) totalChars += end - start;
        }

        if (totalChars <= 0) return;

        var filledCharsF = totalChars * progress;
        var charCounter = 0f;

        for (int i = 0; i < _layout.LineCount; i++)
        {
            var lineLeft = _layout.GetLineLeft(i);
            var lineBaseline = _layout.GetLineTop(i) - _paint.Ascent();
            var start = _layout.GetLineStart(i);
            var end = _layout.GetLineEnd(i);
            var lineWidth = _layout.GetLineWidth(i);
            if (end <= start || start >= (_view?.Text?.Length ?? 0)) continue;
            end = Math.Min(end, _view!.Text?.Length ?? end);
            var lineCharCount = end - start;

            var lineStartChar = charCounter;
            var lineEndChar = charCounter + lineCharCount;
            charCounter = lineEndChar;

            if (filledCharsF >= lineEndChar)
            {
                canvas.DrawText(_view.Text!, start, end, lineLeft, lineBaseline, _paint);
            }
            else if (filledCharsF > lineStartChar)
            {
                var fillCharOffset = filledCharsF - lineStartChar;
                var fillEndX = lineLeft + lineWidth * (fillCharOffset / lineCharCount);
                var clipTop = _layout.GetLineTop(i);
                var clipBottom = i + 1 < _layout.LineCount ? _layout.GetLineTop(i + 1) : _layout.Height;

                canvas.Save();
                canvas.ClipRect(lineLeft, clipTop, fillEndX, clipBottom, global::Android.Graphics.Region.Op.Intersect);
                canvas.DrawText(_view.Text!, start, end, lineLeft, lineBaseline, _paint);
                canvas.Restore();
            }
        }
    }

    private void ConfigurePaint(APaint paint)
    {
        if (_view == null) return;
        var density = Density;
        paint.TextSize = (float)_view.FontSize * density;
        paint.AntiAlias = true;
        var bold = _view.FontAttributes.HasFlag(FontAttributes.Bold);
        paint.FakeBoldText = bold;

        var family = _view.FontFamily;
        if (!string.IsNullOrEmpty(family))
        {
            if (_cachedTypeface == null || _cachedTypefaceFamily != family || _cachedTypefaceBold != bold)
            {
                _cachedTypeface = Typeface.Create(family, bold ? TypefaceStyle.Bold : TypefaceStyle.Normal);
                _cachedTypefaceFamily = family;
                _cachedTypefaceBold = bold;
            }
            paint.SetTypeface(_cachedTypeface);
        }
    }

    private static ALayout.Alignment ToLayoutAlignment(MTextAlignment alignment) => alignment switch
    {
        MTextAlignment.Start => ALayout.Alignment.AlignNormal,
        MTextAlignment.End => ALayout.Alignment.AlignOpposite,
        _ => ALayout.Alignment.AlignCenter,
    };

    private static AColor ToAndroidColor(MColor color)
    {
        return new AColor(
            (byte)(color.Red * 255),
            (byte)(color.Green * 255),
            (byte)(color.Blue * 255),
            (byte)(color.Alpha * 255));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _paint.Dispose();
            _measurePaint?.Dispose();
            _layout?.Dispose();
        }
        base.Dispose(disposing);
    }
}
#endif
