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

    // 缓存逐字歌词的行信息，避免 OnDraw 每帧重复计算 Layout 数据
    private string? _cachedText;
    private int _cachedMainLineCount;
    private int[]? _cachedLineCharCounts;
    private int _cachedTotalChars;
    private int _cachedLayoutLineCount;
    // 复用 Path 对象，避免每帧创建新实例导致 GC 压力
    private readonly Android.Graphics.Path _clipPath = new();

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
            // 低阈值配合 33ms 定时器，保证 30fps 平滑着色
            if (Math.Abs(_lyricProgress - value) < 0.003f) return;
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

    protected override void OnTextChanged(Java.Lang.ICharSequence? text, int start, int lengthBefore, int lengthAfter)
    {
        base.OnTextChanged(text, start, lengthBefore, lengthAfter);
        InvalidateLyricLineCache();
    }

    private void InvalidateLyricLineCache()
    {
        _cachedText = null;
        _cachedLineCharCounts = null;
        _cachedMainLineCount = 0;
        _cachedTotalChars = 0;
        _cachedLayoutLineCount = 0;
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

            // 归一化进度，边界值直接用单色绘制，跳过裁剪层
            float clampedProgress = needsGradient ? Math.Clamp(_lyricProgress, 0f, 1f) : 0f;

            if (needsGradient && clampedProgress >= 1f)
            {
                // 全部已唱：直接用 sungColor 绘制，跳过裁剪
                SetTextColor(_sungColor);
                Paint.SetStyle(Android.Graphics.Paint.Style.Fill);
                Paint.StrokeWidth = 0;
                base.OnDraw(canvas);
            }
            else if (needsGradient && clampedProgress <= 0f)
            {
                // 全部未唱：直接用 unsungColor 绘制，跳过裁剪
                SetTextColor(_unsungColor);
                Paint.SetStyle(Android.Graphics.Paint.Style.Fill);
                Paint.StrokeWidth = 0;
                base.OnDraw(canvas);
            }
            else
            {
                // 第二层：填充底色（未唱颜色）
                SetTextColor(_unsungColor);
                Paint.SetStyle(Android.Graphics.Paint.Style.Fill);
                Paint.StrokeWidth = 0;
                base.OnDraw(canvas);

                // 第三层：已唱颜色覆盖，逐行裁剪实现逐行着色（第一行唱完再着色第二行）
                // 有译文时，仅对原文行着色，译文行不参与渐变着色
                if (needsGradient && clampedProgress > 0f)
                {
                    EnsureLyricLineCache();

                    int mainLineCount = _cachedMainLineCount;
                    int totalChars = _cachedTotalChars;
                    var lineCharCounts = _cachedLineCharCounts;
                    if (lineCharCounts == null || totalChars <= 0) return;

                    // 使用浮点累积，避免整数量化导致着色位置在字符边界跳跃
                    float progressCharsF = totalChars * clampedProgress;
                    float accumulatedCharsF = 0f;

                    SetTextColor(_sungColor);

                    // 复用 Path 对象，避免每帧创建新实例
                    _clipPath.Reset();

                    for (int i = 0; i < mainLineCount; i++)
                    {
                        if (i >= Layout.LineCount) break;
                        if (accumulatedCharsF >= progressCharsF) break;

                        float remainingCharsF = progressCharsF - accumulatedCharsF;
                        int lineChars = lineCharCounts[i];
                        if (lineChars <= 0) continue;

                        float ratio = Math.Min(remainingCharsF / lineChars, 1f);

                        float lineLeft = Layout.GetLineLeft(i);
                        float lineTop = Layout.GetLineTop(i);
                        float lineBottom = Layout.GetLineBottom(i);
                        float lineWidth = Layout.GetLineWidth(i);

                        // 本行着色右边界：按本行应唱字符数占本行总字符数的比例
                        float lineClipRight = lineLeft + lineWidth * ratio;

                        _clipPath.AddRect(lineLeft, lineTop, lineClipRight, lineBottom, Android.Graphics.Path.Direction.Cw);

                        accumulatedCharsF += lineChars;
                    }

                    var saved = canvas.Save();
                    canvas.ClipPath(_clipPath);
                    base.OnDraw(canvas);
                    canvas.RestoreToCount(saved);
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

    /// <summary>计算并缓存逐字歌词所需的行信息，仅在文本或 Layout 变化时重新计算</summary>
    private void EnsureLyricLineCache()
    {
        var text = Text;
        if (Layout == null) return;
        if (_cachedText == text && _cachedLineCharCounts != null && _cachedLayoutLineCount == Layout.LineCount) return;

        int lineCount = Layout.LineCount;
        int mainLineCount = lineCount;
        int nlIndex = text?.IndexOf('\n') ?? -1;
        if (nlIndex >= 0)
            mainLineCount = Layout.GetLineForOffset(nlIndex) + 1;

        var counts = new int[mainLineCount];
        int total = 0;
        for (int i = 0; i < mainLineCount; i++)
        {
            int lineStart = Layout.GetLineStart(i);
            int lineEnd = Layout.GetLineEnd(i);
            int count = Math.Max(0, lineEnd - lineStart);
            counts[i] = count;
            total += count;
        }

        _cachedText = text;
        _cachedMainLineCount = mainLineCount;
        _cachedLineCharCounts = counts;
        _cachedTotalChars = total;
        _cachedLayoutLineCount = lineCount;
    }
}