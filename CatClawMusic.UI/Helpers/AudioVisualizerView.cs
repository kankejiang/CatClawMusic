using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.Views;

namespace CatClawMusic.UI.Helpers;

public class AudioVisualizerView : View
{
    private readonly Paint _barPaint = new(PaintFlags.AntiAlias);
    private float[] _spectrum = Array.Empty<float>();
    private float[] _targetSpectrum = Array.Empty<float>();
    private float[] _velocity = Array.Empty<float>();
    private bool _isAttached;
    private int _inactiveColor = Color.Argb(0x30, 0xFF, 0xFF, 0xFF);
    private int _activeColor = Color.Argb(0xCC, 0xFF, 0xFF, 0xFF);
    private LinearGradient? _gradient;

    private const int BarCount = 32;
    private const float BarRadius = 2.5f;
    private const float MaxBarHeightRatio = 0.85f;
    private const float Smoothing = 0.15f;

    public AudioVisualizerView(Context context) : base(context) => Init();
    public AudioVisualizerView(Context context, IAttributeSet attrs) : base(context, attrs) => Init();
    public AudioVisualizerView(Context context, IAttributeSet attrs, int defStyle) : base(context, attrs, defStyle) => Init();

    private void Init()
    {
        _barPaint.SetStyle(Paint.Style.Fill);
        _spectrum = new float[BarCount];
        _targetSpectrum = new float[BarCount];
        _velocity = new float[BarCount];
    }

    public void SetColors(int activeColor)
    {
        _activeColor = activeColor;
        int r = Color.GetRedComponent(activeColor);
        int g = Color.GetGreenComponent(activeColor);
        int b = Color.GetBlueComponent(activeColor);
        _inactiveColor = Color.Argb(0x30, r, g, b);
        _gradient = null;
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

        if (_gradient == null)
        {
            _gradient = new LinearGradient(0, h, 0, h - maxBarH,
                _inactiveColor, _activeColor, Shader.TileMode.Clamp);
        }

        for (int i = 0; i < BarCount; i++)
        {
            _velocity[i] = (_targetSpectrum[i] - _spectrum[i]) * Smoothing;
            _spectrum[i] += _velocity[i];
            if (_spectrum[i] < 0.01f) _spectrum[i] = 0.01f;
            if (_spectrum[i] > 1f) _spectrum[i] = 1f;

            var barH = Math.Max(2f, _spectrum[i] * maxBarH);
            var left = i * totalBarWidth + gap * 0.5f;
            var bottom = h;
            var top = bottom - barH;
            var right = left + barWidth;

            _barPaint.Color = InterpolateColor(_inactiveColor, _activeColor, _spectrum[i]);

            var rect = new RectF(left, top, right, bottom);
            canvas.DrawRoundRect(rect, BarRadius, BarRadius, _barPaint);
        }

        if (_isAttached)
            PostInvalidateDelayed(30);
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
