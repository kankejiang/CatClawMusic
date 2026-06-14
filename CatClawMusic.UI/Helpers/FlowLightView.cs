using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Util;
using Android.Views;

namespace CatClawMusic.UI.Helpers;

/// <summary>
/// 流光背景动画视图：在模糊封面之上叠加缓慢漂移的半透明色带，
/// 营造沉浸式流光氛围效果。色带从封面提取主色调，通过多重正弦波
/// 叠加模拟自然有机运动。
/// </summary>
public class FlowLightView : View
{
    private readonly Paint _bandPaint = new() { Dither = true, AntiAlias = true };

    // 3 条色带，每条独立相位
    private readonly FlowBand[] _bands = new FlowBand[3];
    private int[]? _coverColors; // 封面主色调（最多3色，ARGB int）
    private bool _running;
    private long _startTime;
    private long _pauseTime;
    private long _pauseOffset;
    private Choreographer.IFrameCallback? _frameCallback;

    // 帧率节流：8fps 兼顾流畅与省电
    private const long FrameIntervalNanos = 1_000_000_000L / 8; // ~125ms
    private long _lastFrameNanos;

    public FlowLightView(Context context) : base(context) => InitBands();
    public FlowLightView(Context context, IAttributeSet attrs) : base(context, attrs) => InitBands();
    public FlowLightView(Context context, IAttributeSet attrs, int defStyleAttr) : base(context, attrs, defStyleAttr) => InitBands();

    private void InitBands()
    {
        for (int i = 0; i < _bands.Length; i++)
            _bands[i] = new FlowBand { Phase = i * 2.1f, Speed = 0.15f + i * 0.06f };
    }

    /// <summary>设置封面主色调（最多3色，ARGB int），用于生成流光色带</summary>
    public void SetCoverColors(int[] colors)
    {
        _coverColors = colors;
        UpdateBandColors();
    }

    /// <summary>设置默认流光色（无封面时使用）</summary>
    public void SetDefaultColors()
    {
        _coverColors = new[] {
            (int)Color.Argb(0x60, 0x9B, 0x7E, 0xD8),
            (int)Color.Argb(0x50, 0x7C, 0x4D, 0xFF),
            (int)Color.Argb(0x55, 0xFF, 0xAB, 0x40)
        };
        UpdateBandColors();
    }

    private void UpdateBandColors()
    {
        if (_coverColors == null) return;
        for (int i = 0; i < _bands.Length; i++)
        {
            var src = i < _coverColors.Length ? _coverColors[i] : _coverColors[0];
            float[] hsv = new float[3];
            Color.RGBToHSV(Color.GetRedComponent(src), Color.GetGreenComponent(src), Color.GetBlueComponent(src), hsv);
            hsv[1] = Math.Clamp(hsv[1] * 0.7f + 0.2f, 0.25f, 0.7f);
            hsv[2] = Math.Clamp(hsv[2] * 0.5f + 0.5f, 0.5f, 0.95f);
            _bands[i].Color = Color.HSVToColor(0x88, hsv);
        }
    }

    /// <summary>启动流光动画</summary>
    public void Start()
    {
        if (_running) return;
        _running = true;
        if (_startTime == 0)
            _startTime = SystemClock.ElapsedRealtime();
        _frameCallback ??= new FrameCallbackImpl(this);
        _lastFrameNanos = 0;
        Choreographer.Instance!.PostFrameCallback(_frameCallback);
    }

    /// <summary>暂停流光动画</summary>
    public void Pause()
    {
        if (!_running) return;
        _running = false;
        _pauseTime = SystemClock.ElapsedRealtime();
        if (_frameCallback != null)
            Choreographer.Instance!.RemoveFrameCallback(_frameCallback);
    }

    /// <summary>恢复流光动画</summary>
    public void Resume()
    {
        if (_running) return;
        _pauseOffset += SystemClock.ElapsedRealtime() - _pauseTime;
        _running = true;
        _lastFrameNanos = 0;
        if (_frameCallback != null)
            Choreographer.Instance!.PostFrameCallback(_frameCallback);
    }

    /// <summary>停止并重置</summary>
    public void Stop()
    {
        _running = false;
        if (_frameCallback != null)
            Choreographer.Instance!.RemoveFrameCallback(_frameCallback);
        _startTime = 0;
        _pauseOffset = 0;
        _lastFrameNanos = 0;
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (_coverColors == null || Width <= 0 || Height <= 0) return;

        var t = (SystemClock.ElapsedRealtime() - _startTime - _pauseOffset) / 1000f;
        var maxDim = Math.Max(Width, Height);

        for (int i = 0; i < _bands.Length; i++)
        {
            var band = _bands[i];
            float x = Width * (0.5f + 0.45f * (float)Math.Sin(t * band.Speed + band.Phase));
            float y = Height * (0.4f + 0.35f * (float)Math.Sin(t * band.Speed * 0.7f + band.Phase + 1.5f));
            float radius = maxDim * (0.45f + 0.15f * (float)Math.Sin(t * band.Speed * 0.5f + band.Phase + 3f));

            _bandPaint.SetShader(new RadialGradient(x, y, radius, band.Color, Color.Transparent, Shader.TileMode.Clamp));
            canvas.DrawCircle(x, y, radius, _bandPaint);
        }
    }

    private void DoFrame(long frameTimeNanos)
    {
        if (!_running) return;

        // 帧率节流：跳过间隔过短的帧
        if (frameTimeNanos - _lastFrameNanos >= FrameIntervalNanos)
        {
            _lastFrameNanos = frameTimeNanos;
            Invalidate();
        }

        if (_frameCallback != null)
            Choreographer.Instance!.PostFrameCallback(_frameCallback);
    }

    private class FrameCallbackImpl : Java.Lang.Object, Choreographer.IFrameCallback
    {
        private readonly FlowLightView _view;
        public FrameCallbackImpl(FlowLightView view) => _view = view;
        public void DoFrame(long frameTimeNanos) => _view.DoFrame(frameTimeNanos);
    }

    private class FlowBand
    {
        public float Phase;
        public float Speed;
        public Color Color;
    }
}
