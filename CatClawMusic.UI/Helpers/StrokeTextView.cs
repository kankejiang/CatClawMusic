using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.Widget;

namespace CatClawMusic.UI.Helpers;

/// <summary>
/// 带描边轮廓的 TextView：先用深色描边绘制文字轮廓，再填充前景色，
/// 使歌词在浅色背景上依然清晰可读（类似 KTV 字幕效果）
/// </summary>
public class StrokeTextView : TextView
{
    private Color _strokeColor = Color.Argb(128, 0, 0, 0);
    private float _strokeWidth = 2.5f;
    private bool _strokeEnabled = true;

    public StrokeTextView(Context context) : base(context) => Init();
    public StrokeTextView(Context context, IAttributeSet? attrs) : base(context, attrs) => InitAttrs(attrs);
    public StrokeTextView(Context context, IAttributeSet? attrs, int defStyleAttr) : base(context, attrs, defStyleAttr) => InitAttrs(attrs);

    private void Init()
    {
        /* Android 9 (API 28) 以下：Paint.Style.Stroke 在硬件加速下可能渲染异常，
         * 强制使用软件渲染层确保描边效果正确。
         * Android 9+：硬件加速渲染管线已修复此问题，可安全使用硬件加速，
         * 歌词滚动动画期间性能提升显著（5个 StrokeTextView 实例全部受益） */
        if (global::Android.OS.Build.VERSION.SdkInt < global::Android.OS.BuildVersionCodes.P)
            SetLayerType(global::Android.Views.LayerType.Software, null);
    }

    private void InitAttrs(IAttributeSet? attrs)
    {
        Init();
        if (attrs == null) return;

        var a = Context!.ObtainStyledAttributes(attrs, new[] {
            Android.Resource.Attribute.TextColor,
            Android.Resource.Attribute.TextSize,
        });
        a.Recycle();
    }

    /// <summary>设置描边颜色（默认 50% 黑色）</summary>
    public Color StrokeColor
    {
        get => _strokeColor;
        set { _strokeColor = value; Invalidate(); }
    }

    /// <summary>设置描边宽度（默认 2.5px）</summary>
    public float StrokeWidth
    {
        get => _strokeWidth;
        set { _strokeWidth = value; Invalidate(); }
    }

    /// <summary>启用/禁用描边效果</summary>
    public bool StrokeEnabled
    {
        get => _strokeEnabled;
        set { _strokeEnabled = value; Invalidate(); }
    }

    protected override void OnDraw(Canvas canvas)
    {
        if (!_strokeEnabled || string.IsNullOrEmpty(Text))
        {
            base.OnDraw(canvas);
            return;
        }

        var tp = Paint;
        var currentColor = tp.Color;

        // 第一遍：描边轮廓
        tp.SetStyle(Android.Graphics.Paint.Style.Stroke);
        tp.StrokeWidth = _strokeWidth;
        tp.Color = _strokeColor;
        tp.StrokeJoin = Android.Graphics.Paint.Join.Round;
        base.OnDraw(canvas);

        // 第二遍：填充前景色
        tp.SetStyle(Android.Graphics.Paint.Style.Fill);
        tp.Color = currentColor;
        tp.StrokeWidth = 0;
        base.OnDraw(canvas);
    }
}
