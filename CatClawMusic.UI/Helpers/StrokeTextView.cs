using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.Widget;

namespace CatClawMusic.UI.Helpers;

public class StrokeTextView : TextView
{
    private Color _strokeColor = Color.Argb(128, 0, 0, 0);
    private float _strokeWidth = 2.5f;
    private bool _strokeEnabled = true;

    private LinearGradient? _lyricGradient;
    private readonly Matrix _lyricMatrix = new();
    private float _lyricProgress = -1;
    private float _gradientTextWidth;
    private float _textStartX;
    private const float GradientTransitionRatio = 0.03f;
    private static readonly int[] GradientColors = { unchecked((int)0xFFFFFFFF), unchecked((int)0xFF777777) };
    private static readonly float[] GradientStops = { 0f, 1f };

    public StrokeTextView(Context context) : base(context) => Init();
    public StrokeTextView(Context context, IAttributeSet? attrs) : base(context, attrs) => InitAttrs(attrs);
    public StrokeTextView(Context context, IAttributeSet? attrs, int defStyleAttr) : base(context, attrs, defStyleAttr) => InitAttrs(attrs);

    private void Init()
    {
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

    public Color StrokeColor
    {
        get => _strokeColor;
        set { _strokeColor = value; Invalidate(); }
    }

    public float StrokeWidth
    {
        get => _strokeWidth;
        set { _strokeWidth = value; Invalidate(); }
    }

    public bool StrokeEnabled
    {
        get => _strokeEnabled;
        set { _strokeEnabled = value; Invalidate(); }
    }

    public void SetupLyricGradient()
    {
        _lyricGradient?.Dispose();
        _lyricGradient = null;
        _lyricProgress = -1;

        if (string.IsNullOrEmpty(Text)) return;

        var paint = Paint;
        paint.SetShader(null);
        _gradientTextWidth = paint.MeasureText(Text);
        var viewWidth = Width > 0 ? Width : _gradientTextWidth;
        _textStartX = Math.Max((viewWidth - _gradientTextWidth) / 2f, 0f);
        var transitionWidth = Math.Max(_gradientTextWidth * GradientTransitionRatio, 60f);

        _lyricGradient = new LinearGradient(
            0, 0, transitionWidth, 0,
            GradientColors, GradientStops,
            Shader.TileMode.Clamp);
    }

    public void SetLyricProgress(float progress)
    {
        if (_lyricGradient == null) return;
        progress = Math.Clamp(progress, 0f, 1f);
        if (Math.Abs(_lyricProgress - progress) < 0.002f) return;
        _lyricProgress = progress;

        var brightEnd = _textStartX + _gradientTextWidth * progress;
        _lyricMatrix.Reset();
        _lyricMatrix.SetTranslate(brightEnd, 0);
        _lyricGradient.SetLocalMatrix(_lyricMatrix);
        Invalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        if (!_strokeEnabled || string.IsNullOrEmpty(Text))
        {
            base.OnDraw(canvas);
            return;
        }

        var tp = Paint;
        var savedShader = tp.Shader;

        tp.SetShader(null);
        tp.SetStyle(Android.Graphics.Paint.Style.Stroke);
        tp.StrokeWidth = _strokeWidth;
        tp.Color = _strokeColor;
        tp.StrokeJoin = Android.Graphics.Paint.Join.Round;
        base.OnDraw(canvas);

        tp.SetShader(_lyricGradient ?? savedShader);
        tp.SetStyle(Android.Graphics.Paint.Style.Fill);
        tp.StrokeWidth = 0;
        base.OnDraw(canvas);
    }
}
