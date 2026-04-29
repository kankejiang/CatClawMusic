using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.Views;
using AColor = Android.Graphics.Color;

namespace CatClawMusic.UI.Views;

/// <summary>
/// 音频频谱可视化控件 — 绘制频率柱状图
/// </summary>
public class SpectrumView : View
{
    private float[] _fftData = Array.Empty<float>();
    private readonly int _barCount = 24;
    private float _barWidth;
    private readonly float _barGap = 3f;
    private readonly float _minHeight = 2f;

    private readonly Paint _barPaint = new(PaintFlags.AntiAlias)
        { StrokeCap = Paint.Cap.Round };
    private readonly Color _barColor = AColor.ParseColor("#9B7ED8");
    private readonly Color _barColorDim = AColor.ParseColor("#C4B5F0");

    public SpectrumView(Context context) : base(context) { }
    public SpectrumView(Context context, IAttributeSet? attrs) : base(context, attrs) { }

    /// <summary>刷新 FFT 频谱数据（0~1 归一化值数组）</summary>
    public void UpdateFftData(float[] data)
    {
        _fftData = data ?? Array.Empty<float>();
        PostInvalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        var w = Width;
        var h = Height;
        if (w <= 0 || h <= 0) return;

        _barWidth = (w - _barGap * (_barCount + 1)) / _barCount;
        if (_barWidth <= 0) return;

        var samples = SampleData(_fftData, _barCount);

        for (int i = 0; i < _barCount; i++)
        {
            float amplitude = samples.Length > i ? Math.Clamp(samples[i], 0f, 1f) : 0.05f;
            float barHeight = amplitude * (h - 8f) + _minHeight;

            float left = _barGap + i * (_barWidth + _barGap);
            float top = h - barHeight;

            _barPaint.Color = amplitude > 0.5f ? _barColor : _barColorDim;
            _barPaint.Alpha = (int)(120 + amplitude * 135);

            canvas.DrawRoundRect(left, top, left + _barWidth, h, 4f, 4f, _barPaint);
        }
    }

    private static float[] SampleData(float[] data, int count)
    {
        if (data.Length == 0) return new float[count];
        var result = new float[count];
        int step = Math.Max(1, data.Length / count);
        for (int i = 0; i < count; i++)
        {
            int idx = Math.Min(i * step, data.Length - 1);
            result[i] = data[idx];
        }
        return result;
    }
}
