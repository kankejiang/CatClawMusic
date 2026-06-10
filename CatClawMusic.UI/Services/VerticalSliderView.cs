using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.Views;

namespace CatClawMusic.UI.Services;

/// <summary>
/// 自定义垂直滑块：真正垂直绘制（无需 Rotation），触摸事件正确处理。
/// 顶部=最大值，底部=最小值。
/// </summary>
public class VerticalSliderView : View
{
    private readonly Paint _trackPaint;
    private readonly Paint _activePaint;
    private readonly Paint _thumbPaint;
    private readonly Paint _thumbBorderPaint;
    private readonly Paint _textPaint;

    private float _min;
    private float _max = 100f;
    private float _value;

    private float _thumbRadius;
    private float _trackWidth;
    private float _trackLeft, _trackRight, _trackTop, _trackBottom;
    private bool _isDragging;

    private int _activeColor = Color.Rgb(0, 188, 212);    // 青色
    private int _trackColor = Color.Argb(80, 255, 255, 255);
    private int _thumbColor = Color.White;
    private int _thumbBorderColor = Color.Argb(180, 0, 188, 212);
    private int _textColor = Color.Argb(180, 255, 255, 255);

    /// <summary>值变化事件</summary>
    public event Action<VerticalSliderView, float>? ValueChanged;

    public VerticalSliderView(Context context) : this(context, null) { }
    public VerticalSliderView(Context context, IAttributeSet? attrs) : base(context, attrs)
    {
        _trackPaint = new Paint(PaintFlags.AntiAlias);
        _activePaint = new Paint(PaintFlags.AntiAlias);
        _thumbPaint = new Paint(PaintFlags.AntiAlias);
        _thumbBorderPaint = new Paint(PaintFlags.AntiAlias);
        _thumbBorderPaint.SetStyle(Paint.Style.Stroke);
        _thumbBorderPaint.StrokeWidth = 2f;
        _textPaint = new Paint(PaintFlags.AntiAlias) { TextSize = 24f, TextAlign = Paint.Align.Center };
        SetWillNotDraw(false);
    }

    public float Min
    {
        get => _min;
        set { _min = value; if (_value < _min) _value = _min; Invalidate(); }
    }

    public float Max
    {
        get => _max;
        set { _max = value; if (_value > _max) _value = _max; Invalidate(); }
    }

    public float Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, _min, _max);
            if (Math.Abs(clamped - _value) < 0.001f) return;
            _value = clamped;
            Invalidate();
            ValueChanged?.Invoke(this, _value);
        }
    }

    public int ActiveColor
    {
        get => _activeColor;
        set { _activeColor = value; _thumbBorderColor = Color.Argb(180, Color.GetRedComponent(value), Color.GetGreenComponent(value), Color.GetBlueComponent(value)); Invalidate(); }
    }

    public int TrackColor { get => _trackColor; set { _trackColor = value; Invalidate(); } }
    public int ThumbColor { get => _thumbColor; set { _thumbColor = value; Invalidate(); } }

    protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
    {
        base.OnSizeChanged(w, h, oldw, oldh);
        float density = Resources?.DisplayMetrics?.Density ?? 2f;
        _thumbRadius = 7f * density;
        _trackWidth = 4f * density;
        _trackLeft = w / 2f - _trackWidth / 2f;
        _trackRight = w / 2f + _trackWidth / 2f;
        _trackTop = _thumbRadius + 2f * density;
        _trackBottom = h - _thumbRadius - 2f * density;
        _textPaint.TextSize = 10f * density;
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (Width == 0 || Height == 0) return;

        float centerX = Width / 2f;
        float trackLength = _trackBottom - _trackTop;
        if (trackLength <= 0) return;

        float ratio = (_value - _min) / (_max - _min);
        float thumbY = _trackBottom - ratio * trackLength; // 顶部=最大值

        // 轨道背景
        _trackPaint.Color = new Color(_trackColor);
        canvas.DrawRoundRect(_trackLeft, _trackTop, _trackRight, _trackBottom,
            _trackWidth / 2, _trackWidth / 2, _trackPaint);

        // 中间零线
        float zeroRatio = (0 - _min) / (_max - _min);
        float zeroY = _trackBottom - zeroRatio * trackLength;
        var zeroPaint = new Paint { Color = Color.Argb(60, 255, 255, 255), StrokeWidth = 1f };
        canvas.DrawLine(_trackLeft - 3f * Resources!.DisplayMetrics!.Density, zeroY,
            _trackRight + 3f * Resources.DisplayMetrics.Density, zeroY, zeroPaint);

        // 活动轨道（从中心到当前值）
        _activePaint.Color = new Color(_activeColor);
        float activeTop = Math.Min(thumbY, zeroY);
        float activeBottom = Math.Max(thumbY, zeroY);
        canvas.DrawRoundRect(_trackLeft, activeTop, _trackRight, activeBottom,
            _trackWidth / 2, _trackWidth / 2, _activePaint);

        // 滑块圆点
        _thumbPaint.Color = new Color(_thumbColor);
        _thumbBorderPaint.Color = new Color(_thumbBorderColor);
        canvas.DrawCircle(centerX, thumbY, _thumbRadius, _thumbPaint);
        canvas.DrawCircle(centerX, thumbY, _thumbRadius, _thumbBorderPaint);
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e == null) return false;
        float y = e.GetY();

        switch (e.Action)
        {
            case MotionEventActions.Down:
                _isDragging = true;
                Parent?.RequestDisallowInterceptTouchEvent(true);
                UpdateValueFromTouch(y);
                break;
            case MotionEventActions.Move:
                if (_isDragging) UpdateValueFromTouch(y);
                break;
            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                _isDragging = false;
                Parent?.RequestDisallowInterceptTouchEvent(false);
                break;
        }
        return true;
    }

    private void UpdateValueFromTouch(float touchY)
    {
        float trackLength = _trackBottom - _trackTop;
        if (trackLength <= 0) return;

        float ratio = 1f - (touchY - _trackTop) / trackLength;
        ratio = Math.Clamp(ratio, 0f, 1f);
        Value = _min + ratio * (_max - _min);
    }
}