using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.Widget;

namespace CatClawMusic.UI.Helpers;

/// <summary>
/// 支持描边和歌词渐变进度的自定义 TextView
/// <para>通过 OnDraw 重写实现两种效果：1) 文字描边（Stroke）；2) 歌词已唱/未唱双色渐变</para>
/// <para>渐变原理：先绘制未唱颜色全文，再通过 Canvas 裁剪区域（ClipRect）覆盖绘制已唱颜色，实现左右渐变过渡</para>
/// </summary>
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

    /// <summary>
    /// 歌词渐变进度（0~1），-1 表示不启用渐变
    /// <para>0 = 完全未唱，1 = 完全已唱；进度值决定已唱颜色的裁剪宽度</para>
    /// </summary>
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

    /// <summary>重置歌词进度为未启用状态</summary>
    public void ResetLyricProgress()
    {
        _lyricProgress = -1f;
        Invalidate();
    }

    /// <summary>
    /// 原子更新 Spannable 文本和歌词进度，避免中间状态闪烁。
    /// 抑制 SetText 产生的中间重绘，设置完进度后统一刷新一次。
    /// </summary>
    public void SetSpannableWithProgress(Java.Lang.ICharSequence spannable, float progress)
    {
        _suppressInvalidate = true;
        try
        {
            SetText(spannable, BufferType.Spannable);
            _lyricProgress = progress;
        }
        finally
        {
            _suppressInvalidate = false;
            Invalidate();
        }
    }

    /// <summary>
    /// 原子设置纯文本并重置歌词进度，避免 ResetLyricProgress + Text 两次重绘导致的闪烁。
    /// </summary>
    public void SetPlainTextNoGradient(string text)
    {
        _suppressInvalidate = true;
        try
        {
            _lyricProgress = -1f;
            Text = text;
        }
        finally
        {
            _suppressInvalidate = false;
            Invalidate();
        }
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

    /// <summary>
    /// 自定义绘制逻辑：先绘制描边层，再绘制填充层，最后通过裁剪区域叠加已唱颜色
    /// </summary>
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
            // 第一层：描边（如果启用），使用 Stroke 样式绘制文字轮廓
            if (needsStroke)
            {
                SetTextColor(_strokeColor);
                Paint.SetStyle(Android.Graphics.Paint.Style.Stroke);
                Paint.StrokeWidth = _strokeWidth;
                Paint.StrokeJoin = Android.Graphics.Paint.Join.Round;
                base.OnDraw(canvas);
            }

            // 第二层：填充底色（未唱颜色或原始文字色）
            var fillColor = needsGradient ? _unsungColor : originalTextColor;
            SetTextColor(fillColor);
            Paint.SetStyle(Android.Graphics.Paint.Style.Fill);
            Paint.StrokeWidth = 0;
            base.OnDraw(canvas);

            // 第三层：已唱颜色覆盖，逐行裁剪实现逐行着色（第一行唱完再着色第二行）
            // 有译文时，仅对原文行着色，译文行保持底色不被渐变染色
            if (needsGradient && _lyricProgress > 0f)
            {
                // 计算每行宽度和总宽度（逐行累加），进度按总宽度比例分配
                int lineCount = Layout.LineCount;
                // 查找原文行数量（换行符之前），译文行不参与渐变着色
                int mainLineCount = lineCount;
                int nlIndex = Text?.IndexOf('\n') ?? -1;
                if (nlIndex >= 0)
                    mainLineCount = Layout.GetLineForOffset(nlIndex) + 1;

                var lineWidths = new float[lineCount];
                float totalWidth = 0f;
                for (int i = 0; i < lineCount; i++)
                {
                    lineWidths[i] = Layout.GetLineWidth(i);
                    if (i < mainLineCount)
                        totalWidth += lineWidths[i];
                }

                // 将进度映射到逐行累计宽度上的位置（仅基于原文行宽度）
                float progressWidth = totalWidth * Math.Clamp(_lyricProgress, 0f, 1f);

                float accumulated = 0f;
                for (int i = 0; i < mainLineCount; i++)
                {
                    if (accumulated >= progressWidth) break;

                    float lineLeft = Layout.GetLineLeft(i);
                    float lineTop = Layout.GetLineTop(i);
                    float lineBottom = Layout.GetLineBottom(i);
                    float remainingProgress = progressWidth - accumulated;

                    // 本行着色右边界：整行已唱则到行尾，否则按剩余进度截断
                    float lineClipRight = remainingProgress >= lineWidths[i]
                        ? lineLeft + lineWidths[i]
                        : lineLeft + remainingProgress;

                    var saved = canvas.Save();
                    canvas.ClipRect(lineLeft, lineTop, lineClipRight, lineBottom);
                    SetTextColor(_sungColor);
                    base.OnDraw(canvas);
                    canvas.RestoreToCount(saved);

                    accumulated += lineWidths[i];
                }
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
