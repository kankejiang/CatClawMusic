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
    private int _inactiveColor = Color.Argb(0x30, 0xFF, 0xFF, 0xFF);
    private int _activeColor = Color.Argb(0xCC, 0xFF, 0xFF, 0xFF);

    private const int BarCount = 64;
    private const float BarRadius = 2f;
    private const float MaxBarHeightRatio = 0.88f;
    private const float AttackSpeed = 0.95f;
    private const float DecaySpeed = 0.28f;
    private const float PeakGravity = 0.003f;
    private const float PeakBounce = 0.15f;
    private const float MinBarRatio = 0.01f;

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
        for (int i = 0; i < len; i++)
            _targetSpectrum[i] = spectrum[i];
    }

    public void Clear()
    {
        Array.Clear(_targetSpectrum);
    }

    protected override void OnAttachedToWindow()
    {
        base.OnAttachedToWindow();
        _isAttached = true;
    }

    protected override void OnDetachedFromWindow()
    {
        base.OnDetachedFromWindow();
        _isAttached = false;
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (Width <= 0 || Height <= 0) return;

        var w = (float)Width;
        var h = (float)Height;
        var totalBarWidth = w / BarCount;
        var barWidth = totalBarWidth * 0.6f;
        var gap = totalBarWidth * 0.4f;
        var maxBarH = h * MaxBarHeightRatio;
        var minBarH = h * MinBarRatio;

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

            _barPaint.Color = InterpolateColor(_inactiveColor, _activeColor, current);
            canvas.DrawRoundRect(new RectF(left, top, right, bottom), BarRadius, BarRadius, _barPaint);

            float peakY = bottom - _peakLevel[i] * maxBarH;
            _peakPaint.Color = InterpolateColor(_inactiveColor, _activeColor, 1f);
            canvas.DrawRoundRect(new RectF(left, peakY - 1.5f, right, peakY + 0.5f), 0.5f, 0.5f, _peakPaint);
        }

        if (_isAttached)
            PostInvalidateDelayed(16);
    }

    private static Color InterpolateColor(int from, int to, float ratio)
    {
        var fa = (byte)((from >> 24) & 0xFF);
        var fr = (byte)((from >> 16) & 0xFF);
        var fg = (byte)((from >> 8) & 0xFF);
        var fb = (byte)(from & 0xFF);
        var ta = (byte)((to >> 24) & 0xFF);
        var tr = (byte)((to >> 16) & 0xFF);
        var tg = (byte)((to >> 8) & 0xFF);
        var tb = (byte)(to & 0xFF);
        var a = ClampByte(fa + (ta - fa) * ratio);
        var r = ClampByte(fr + (tr - fr) * ratio);
        var g = ClampByte(fg + (tg - fg) * ratio);
        var b = ClampByte(fb + (tb - fb) * ratio);
        return new Color(r, g, b, a);
    }

    private static int ClampByte(float v) => (int)Math.Clamp(v, 0, 255);
}
