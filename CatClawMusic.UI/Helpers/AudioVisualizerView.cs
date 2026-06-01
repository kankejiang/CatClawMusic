using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.Views;

namespace CatClawMusic.UI.Helpers;

/// <summary>
/// 音频频谱可视化视图，将音频频谱数据渲染为动态柱状图
/// <para>使用 Choreographer 同步帧回调实现 60fps 流畅动画</para>
/// <para>支持攻击/衰减平滑过渡、峰值指示器（带重力下落和弹跳效果）、空闲淡出</para>
/// </summary>
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
    private const float AttackSpeed = 0.88f;     // 攻击速度：频谱上升时的插值系数，越大越快
    private const float DecaySpeed = 0.28f;      // 衰减速度：频谱下降时的插值系数，越小越慢
    private const float PeakGravity = 0.0003f;   // 峰值重力：每帧增加的下落速度
    private const float PeakBounce = 0.06f;      // 峰值弹跳系数：触底后反弹的比例
    private const float MinBarRatio = 0.005f;    // 最小柱高比例，防止柱子完全消失

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

    /// <summary>设置频谱柱的活跃颜色，自动生成对应的半透明非活跃颜色</summary>
    public void SetColors(int activeColor)
    {
        _activeColor = activeColor;
        int r = Color.GetRedComponent(activeColor);
        int g = Color.GetGreenComponent(activeColor);
        int b = Color.GetBlueComponent(activeColor);
        _inactiveColor = Color.Argb(0x30, r, g, b);
    }

    /// <summary>更新频谱数据源，传入 FFT 频谱数组</summary>
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

    /// <summary>清除频谱数据，柱状图将逐渐衰减至零</summary>
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

    /// <summary>使用 Choreographer 注册下一帧回调，实现与 VSync 同步的动画</summary>
    private void PostFrameCallback()
    {
        if (!_isAttached || _isFrameCallbackPosted) return;
        _isFrameCallbackPosted = true;
        _cachedFrameCallback ??= new FrameCallback(this);
        Choreographer.Instance.PostFrameCallback(_cachedFrameCallback);
    }

    /// <summary>帧回调内部类，每2帧触发一次重绘以平衡性能与流畅度</summary>
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

    /// <summary>
    /// 核心绘制逻辑：遍历每个频谱柱，计算攻击/衰减插值、峰值下落、颜色渐变并绘制
    /// </summary>
    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (Width <= 0 || Height <= 0) return;

        if (_isIdle)
        {
            _idleFrameCount++;
            // 空闲超过 30 帧后停止绘制，节省电量
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

            // 攻击/衰减插值：上升快（AttackSpeed），下降慢（DecaySpeed）
            if (target > current)
                current += (target - current) * AttackSpeed;
            else
                current += (target - current) * DecaySpeed;

            current = Math.Max(current, MinBarRatio);
            if (current > 1f) current = 1f;
            _spectrum[i] = current;

            if (current > 0.01f) stillActive = true;

            // 峰值指示器逻辑：新值超过峰值时重置，否则峰值受重力下落
            if (current > _peakLevel[i])
            {
                _peakLevel[i] = current;
                _peakVelocity[i] = 0f;
            }
            else
            {
                // 重力加速下落
                _peakVelocity[i] += PeakGravity;
                _peakLevel[i] -= _peakVelocity[i];
                // 峰值触底弹跳
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

            // 根据柱高在非活跃色和活跃色之间插值
            _barPaint.Color = new Color(InterpolateColor(_inactiveColor, _activeColor, current));
            _rect.Set(left, top, right, bottom);
            canvas.DrawRoundRect(_rect, BarRadius, BarRadius, _barPaint);

            // 绘制峰值指示器（小横条）
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

    /// <summary>在两种颜色之间按比例线性插值（按通道分别计算 ARGB）</summary>
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
