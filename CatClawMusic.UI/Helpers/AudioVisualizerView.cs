using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.Views;

namespace CatClawMusic.UI.Helpers;

public class AudioVisualizerView : View
{
    private readonly Paint _barPaint = new(PaintFlags.AntiAlias);
    private readonly Paint _peakPaint = new(PaintFlags.AntiAlias);
    private float[] _spectrum = Array.Empty<float>();
    private float[] _targetSpectrum = Array.Empty<float>();
    private float[] _peakLevel = Array.Empty<float>();
    private float[] _peakVelocity = Array.Empty<float>();
    private bool _isAttached;
    private bool _isFrameCallbackPosted;
    private int _inactiveColor = Color.Argb(0x30, 0xFF, 0xFF, 0xFF);
    private int _activeColor = Color.Argb(0xCC, 0xFF, 0xFF, 0xFF);
    private readonly RectF _rect = new();
    private FrameCallback? _cachedFrameCallback;
    private bool _isIdle = true;
    private int _idleFrameCount;

    private const int BarCount = 64;
    private const float BarRadius = 2f;
    private const float MaxBarHeightRatio = 0.95f;
    private const float AttackSpeed = 0.88f;
    private const float DecaySpeed = 0.28f;
    private const float PeakGravity = 0.0003f;
    private const float PeakBounce = 0.06f;
    private const float MinBarRatio = 0.005f;

    public AudioVisualizerView(Context context) : base(context) => Init();
    public AudioVisualizerView(Context context, IAttributeSet attrs) : base(context, attrs) => Init();
    public AudioVisualizerView(Context context, IAttributeSet attrs, int defStyle) : base(context, attrs, defStyle) => Init();

    private void Init()
    {
        _barPaint.SetStyle(Paint.Style.Fill);
        _peakPaint.SetStyle(Paint.Style.Fill);
        _spectrum = new float[BarCount];
        _targetSpectrum = new float[BarCount];
        _peakLevel = new float[BarCount];
        _peakVelocity = new float[BarCount];
    }

    public void SetColors(int activeColor)
    {
        _activeColor = activeColor;
        int r = Color.GetRedComponent(activeColor);
        int g = Color.GetGreenComponent(activeColor);
        int b = Color.GetBlueComponent(activeColor);
        _inactiveColor = Color.Argb(0x30, r, g, b);
    }

    public void UpdateSpectrum(float[] spectrum)
    {
        if (spectrum == null || spectrum.Length == 0) return;
        var len = Math.Min(spectrum.Length, BarCount);
        bool hasSignal = false;
        for (int i = 0; i < len; i++)
        {
            _targetSpectrum[i] = spectrum[i];
            if (spectrum[i] > 0.01f) hasSignal = true;
        }
        if (hasSignal)
        {
            _isIdle = false;
            _idleFrameCount = 0;
        }
        else if (!_isIdle)
        {
            _isIdle = true;
            _idleFrameCount = 20;
        }
    }

    public void Clear()
    {
        Array.Clear(_targetSpectrum);
    }

    protected override void OnAttachedToWindow()
    {
        base.OnAttachedToWindow();
        _isAttached = true;
        SetLayerType(LayerType.Hardware, null);
        PostFrameCallback();
    }

    protected override void OnDetachedFromWindow()
    {
        base.OnDetachedFromWindow();
        _isAttached = false;
        _isFrameCallbackPosted = false;
        SetLayerType(LayerType.None, null);
    }

    private void PostFrameCallback()
    {
        if (!_isAttached || _isFrameCallbackPosted) return;
        _isFrameCallbackPosted = true;
        _cachedFrameCallback ??= new FrameCallback(this);
        Choreographer.Instance.PostFrameCallback(_cachedFrameCallback);
    }

    private class FrameCallback : Java.Lang.Object, Choreographer.IFrameCallback
    {
        private readonly WeakReference<AudioVisualizerView> _view;
        private int _frameCount;
        public FrameCallback(AudioVisualizerView view) => _view = new WeakReference<AudioVisualizerView>(view);
        public void DoFrame(long frameTimeNanos)
        {
            if (_view.TryGetTarget(out var view) && view._isAttached)
            {
                view._isFrameCallbackPosted = false;
                if (++_frameCount % 2 == 0)
                    view.Invalidate();
                view.PostFrameCallback();
            }
        }
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (Width <= 0 || Height <= 0) return;

        if (_isIdle)
        {
            _idleFrameCount++;
            if (_idleFrameCount > 30) return;
        }

        var w = (float)Width;
        var h = (float)Height;
        var totalBarWidth = w / BarCount;
        var barWidth = totalBarWidth * 0.6f;
        var gap = totalBarWidth * 0.4f;
        var maxBarH = h * MaxBarHeightRatio;
        var minBarH = h * MinBarRatio;
        bool stillActive = false;

        for (int i = 0; i < BarCount; i++)
        {
            float target = _targetSpectrum[i];
            float current = _spectrum[i];

            if (target > current)
                current += (target - current) * AttackSpeed;
            else
                current += (target - current) * DecaySpeed;

            current = Math.Max(current, MinBarRatio);
            if (current > 1f) current = 1f;
            _spectrum[i] = current;

            if (current > 0.01f) stillActive = true;

            if (current > _peakLevel[i])
            {
                _peakLevel[i] = current;
                _peakVelocity[i] = 0f;
            }
            else
            {
                _peakVelocity[i] += PeakGravity;
                _peakLevel[i] -= _peakVelocity[i];
                if (_peakLevel[i] < current)
                {
                    _peakLevel[i] = current;
                    _peakVelocity[i] = -_peakVelocity[i] * PeakBounce;
                }
            }

            if (_peakLevel[i] > 1f) _peakLevel[i] = 1f;

            var barH = Math.Max(minBarH, current * maxBarH);
            var left = i * totalBarWidth + gap * 0.5f;
            var bottom = h;
            var top = bottom - barH;
            var right = left + barWidth;

            _barPaint.Color = new Color(InterpolateColor(_inactiveColor, _activeColor, current));
            _rect.Set(left, top, right, bottom);
            canvas.DrawRoundRect(_rect, BarRadius, BarRadius, _barPaint);

            float peakY = bottom - _peakLevel[i] * maxBarH;
            _peakPaint.Color = new Color(_activeColor);
            _rect.Set(left, peakY - 1.5f, right, peakY + 0.5f);
            canvas.DrawRoundRect(_rect, 0.5f, 0.5f, _peakPaint);
        }

        if (!stillActive)
        {
            _isIdle = true;
            _idleFrameCount = 0;
        }
    }

    private static int InterpolateColor(int from, int to, float ratio)
    {
        var fa = (from >> 24) & 0xFF;
        var fr = (from >> 16) & 0xFF;
        var fg = (from >> 8) & 0xFF;
        var fb = from & 0xFF;
        var ta = (to >> 24) & 0xFF;
        var tr = (to >> 16) & 0xFF;
        var tg = (to >> 8) & 0xFF;
        var tb = to & 0xFF;
        var a = fa + (int)((ta - fa) * ratio);
        var r = fr + (int)((tr - fr) * ratio);
        var g = fg + (int)((tg - fg) * ratio);
        var b = fb + (int)((tb - fb) * ratio);
        return (a << 24) | (r << 16) | (g << 8) | b;
    }
}
