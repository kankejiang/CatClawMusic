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
/// 后期逐字歌词可在 OnDraw 中按字符计算裁剪边界实现逐字填充。
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
            [nameof(Controls.KaraokeLabel.FillProgress)] = MapSync,
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
}

/// <summary>
/// Android 原生绘制视图：负责实际的 Canvas 文字绘制。
/// 用 StaticLayout 测量换行与行位置，用 Canvas.drawText 按行绘制空心+实心两层。
/// </summary>
public class KaraokePlatformView : AView
{
    private Controls.KaraokeLabel? _view;
    private readonly APaint _paint = new() { AntiAlias = true };
    private TextPaint? _measurePaint;
    private StaticLayout? _layout;

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
        var textColor = ToAndroidColor(_view.TextColor);
        var outlineColor = ToAndroidColor(_view.OutlineColor);
        var strokeWidth = (float)_view.StrokeWidth * density;
        var progress = (float)Math.Clamp(_view.FillProgress, 0.0, 1.0);

        ConfigurePaint(_paint);
        var text = _view.Text ?? string.Empty;

        canvas.Save();
        canvas.Translate(0, paddingTop);

        // 1) 先画空心描边层（整个文字）
        _paint.Color = outlineColor;
        _paint.SetStyle(APaint.Style.Stroke);
        _paint.StrokeWidth = strokeWidth;
        DrawAllLines(canvas, text, _paint);

        // 2) 再画实心填充层，按 FillProgress 裁剪从左到右的进度位置
        //    progress=0 时实心层完全裁掉，只剩空心；progress=1 时全部填充
        //    后期逐字歌词：这里可改为按字符索引逐字裁剪实现每字独立进度
        var fillWidth = _layout.Width * progress;
        if (fillWidth > 0.5f)
        {
            canvas.Save();
            canvas.ClipRect(0, 0, fillWidth, _layout.Height, global::Android.Graphics.Region.Op.Intersect);
            _paint.Color = textColor;
            _paint.SetStyle(APaint.Style.Fill);
            DrawAllLines(canvas, text, _paint);
            canvas.Restore();
        }

        canvas.Restore();
    }

    private void DrawAllLines(Canvas canvas, string text, APaint paint)
    {
        for (int i = 0; i < _layout!.LineCount; i++)
        {
            var lineLeft = _layout.GetLineLeft(i);
            // baseline = lineTop - paint.ascent()
            var lineBaseline = _layout.GetLineTop(i) - paint.Ascent();
            var start = _layout.GetLineStart(i);
            var end = _layout.GetLineEnd(i);
            if (end > start && start < text.Length)
            {
                end = Math.Min(end, text.Length);
                canvas.DrawText(text, start, end, lineLeft, lineBaseline, paint);
            }
        }
    }

    private void ConfigurePaint(APaint paint)
    {
        if (_view == null) return;
        var density = Density;
        paint.TextSize = (float)_view.FontSize * density;
        paint.AntiAlias = true;
        paint.FakeBoldText = _view.FontAttributes.HasFlag(FontAttributes.Bold);

        var family = _view.FontFamily;
        if (!string.IsNullOrEmpty(family))
        {
            var typeface = Typeface.Create(family, _view.FontAttributes.HasFlag(FontAttributes.Bold)
                ? TypefaceStyle.Bold : TypefaceStyle.Normal);
            paint.SetTypeface(typeface);
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
            (byte)(color.Alpha * 255),
            (byte)(color.Red * 255),
            (byte)(color.Green * 255),
            (byte)(color.Blue * 255));
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
