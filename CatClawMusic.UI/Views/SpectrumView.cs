using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.Views;
using AColor = Android.Graphics.Color;

namespace CatClawMusic.UI.Views;

public class SpectrumView : View
{
    private float[] _bars = Array.Empty<float>();
    private float[] _peaks = Array.Empty<float>();
    private const int BarCount = 32;
    private const float BarGap = 3f;

    private readonly Paint _barPaint = new(PaintFlags.AntiAlias);
    private readonly Paint _bgPaint = new(PaintFlags.AntiAlias);
    private readonly Color _colorPeak = AColor.ParseColor("#9B7ED8");
    private readonly Color _colorMid = AColor.ParseColor("#B8A0E8");
    private readonly Color _colorLow = AColor.ParseColor("#D4C8F2");

    public SpectrumView(Context context) : base(context) { }
    public SpectrumView(Context context, IAttributeSet? attrs) : base(context, attrs) { }

    public void UpdateFftData(float[] bars, float[] peaks)
    {
        _bars = bars ?? Array.Empty<float>();
        _peaks = peaks ?? Array.Empty<float>();
        PostInvalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        int w = Width, h = Height;
        if (w <= 0 || h <= 0) return;

        float barW = (w - BarGap * (BarCount + 1)) / BarCount;
        if (barW < 2) barW = 2;

        float usableH = h * 0.85f; // 用 85% 高度，更高
        var b = Resample(_bars);
        var p = Resample(_peaks);

        // 1. 背景柱（峰值，半透明 + 淡色）
        _bgPaint.Alpha = 40;
        _bgPaint.Color = _colorLow;
        for (int i = 0; i < BarCount; i++)
        {
            float h2 = Math.Max(2, p[i] * usableH);
            float x = BarGap + i * (barW + BarGap);
            canvas.DrawRoundRect(x, h - h2, x + barW, h, 3f, 3f, _bgPaint);
        }

        // 2. 主柱（快色 + 渐变）
        for (int i = 0; i < BarCount; i++)
        {
            float amp = Math.Clamp(b[i], 0.02f, 1f);
            float barH = amp * usableH;

            float t = amp;
            if (t < 0.3f) _barPaint.Color = Blend(_colorLow, _colorMid, t / 0.3f);
            else _barPaint.Color = Blend(_colorMid, _colorPeak, (t - 0.3f) / 0.7f);
            _barPaint.Alpha = (int)(120 + amp * 135);

            float x = BarGap + i * (barW + BarGap);
            canvas.DrawRoundRect(x, h - barH, x + barW, h, 3f, 3f, _barPaint);
        }
    }

    private static float[] Resample(float[] data)
    {
        if (data.Length == 0)
        {
            var e = new float[BarCount];
            Array.Fill(e, 0.04f);
            return e;
        }
        var r = new float[BarCount];
        int step = Math.Max(1, data.Length / BarCount);
        for (int i = 0; i < BarCount; i++)
            r[i] = data[Math.Min(i * step, data.Length - 1)];
        return r;
    }

    private static Color Blend(Color a, Color b, float t)
    {
        int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;
        return AColor.Argb(255, Clamp((int)(a.R + (b.R - a.R) * t)),
            Clamp((int)(a.G + (b.G - a.G) * t)), Clamp((int)(a.B + (b.B - a.B) * t)));
    }
}
