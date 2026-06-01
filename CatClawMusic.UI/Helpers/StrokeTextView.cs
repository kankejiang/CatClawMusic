using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.Widget;

namespace CatClawMusic.UI.Helpers;

public class StrokeTextView : TextView
{
    private Color _strokeColor = Color.Argb(128, 0, 0, 0);
    private float _strokeWidth = 2.5f;
    private bool _strokeEnabled = false;
    private float _lyricProgress = -1f;
    private Color _sungColor = Color.White;
    private Color _unsungColor = Color.Argb(0xCC, 0xBB, 0xBB, 0xBB);
    private bool _suppressInvalidate;

    public StrokeTextView(Context context) : base(context) { }
    public StrokeTextView(Context context, IAttributeSet? attrs) : base(context, attrs) { }
    public StrokeTextView(Context context, IAttributeSet? attrs, int defStyleAttr) : base(context, attrs, defStyleAttr) { }
    public StrokeTextView(IntPtr handle, Android.Runtime.JniHandleOwnership ownership) : base(handle, ownership) { }

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

    public float LyricProgress
    {
        get => _lyricProgress;
        set
        {
            if (Math.Abs(_lyricProgress - value) < 0.001f) return;
            _lyricProgress = value;
            Invalidate();
        }
    }

    public Color SungColor
    {
        get => _sungColor;
        set { _sungColor = value; Invalidate(); }
    }

    public Color UnsungColor
    {
        get => _unsungColor;
        set { _unsungColor = value; Invalidate(); }
    }

    public void ResetLyricProgress()
    {
        _lyricProgress = -1f;
        Invalidate();
    }

    public override void Invalidate()
    {
        if (_suppressInvalidate) return;
        base.Invalidate();
    }

    public override void Invalidate(int l, int t, int r, int b)
    {
        if (_suppressInvalidate) return;
        base.Invalidate(l, t, r, b);
    }

    protected override void OnDraw(Canvas canvas)
    {
        if (string.IsNullOrEmpty(Text) || Layout == null)
        {
            base.OnDraw(canvas);
            return;
        }

        bool needsGradient = _lyricProgress >= 0f;
        bool needsStroke = _strokeEnabled;

        if (!needsGradient && !needsStroke)
        {
            base.OnDraw(canvas);
            return;
        }

        var originalTextColor = new Color(TextColors.DefaultColor);

        _suppressInvalidate = true;
        try
        {
            if (needsStroke)
            {
                SetTextColor(_strokeColor);
                Paint.SetStyle(Android.Graphics.Paint.Style.Stroke);
                Paint.StrokeWidth = _strokeWidth;
                Paint.StrokeJoin = Android.Graphics.Paint.Join.Round;
                base.OnDraw(canvas);
            }

            var fillColor = needsGradient ? _unsungColor : originalTextColor;
            SetTextColor(fillColor);
            Paint.SetStyle(Android.Graphics.Paint.Style.Fill);
            Paint.StrokeWidth = 0;
            base.OnDraw(canvas);

            if (needsGradient && _lyricProgress > 0f)
            {
                float textWidth = 0f;
                for (int i = 0; i < Layout.LineCount; i++)
                    textWidth = Math.Max(textWidth, Layout.GetLineWidth(i));

                float textStartX = Layout.GetLineLeft(0);
                float clipX = textStartX + textWidth * Math.Clamp(_lyricProgress, 0f, 1f);

                var saved = canvas.Save();
                canvas.ClipRect(0, 0, clipX, Height);
                SetTextColor(_sungColor);
                base.OnDraw(canvas);
                canvas.RestoreToCount(saved);
            }
        }
        finally
        {
            SetTextColor(originalTextColor);
            Paint.SetStyle(Android.Graphics.Paint.Style.Fill);
            Paint.StrokeWidth = 0;
            _suppressInvalidate = false;
        }
    }
}
