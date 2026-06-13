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
    private readonly Paint _blurPaint = new() { Dither = true, AntiAlias = true };

    // 3 条色带，每条独立相位
    private readonly FlowBand[] _bands = new FlowBand[3];
    private int[]? _coverColors; // 封面主色调（最多3色，ARGB int）
    private bool _running;
    private long _startTime;
    private long _pauseTime;
    private long _pauseOffset;
    private Choreographer.IFrameCallback? _frameCallback;

    public FlowLightView(Context context) : base(context) => InitBands();
    public FlowLightView(Context context, IAttributeSet attrs) : base(context, attrs) => InitBands();
    public FlowLightView(Context context, IAttributeSet attrs, int defStyleAttr) : base(context, attrs, defStyleAttr) => InitBands();

    private void InitBands()
    {
        for (int i = 0; i < _bands.Length; i++)
            _bands[i] = new FlowBand { Phase = i * 2.1f, Speed = 0.15f + i * 0.06f };
        SetLayerType(LayerType.Hardware, null);
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
            (int)Color.Argb(0x30, 0x9B, 0x7E, 0xD8),
            (int)Color.Argb(0x25, 0x7C, 0x4D, 0xFF),
            (int)Color.Argb(0x28, 0xFF, 0xAB, 0x40)
        };
        UpdateBandColors();
    }

    private void UpdateBandColors()
    {
        if (_coverColors == null) return;
        for (int i = 0; i < _bands.Length; i++)
        {
            var src = i < _coverColors.Length ? _coverColors[i] : _coverColors[0];
            // 降低饱和度+透明度，生成柔和流光色
            float[] hsv = new float[3];
            Color.RGBToHSV(Color.GetRedComponent(src), Color.GetGreenComponent(src), Color.GetBlueComponent(src), hsv);
            hsv[1] = Math.Clamp(hsv[1] * 0.5f + 0.1f, 0.1f, 0.4f);
            hsv[2] = Math.Clamp(hsv[2] * 0.4f + 0.5f, 0.5f, 0.85f);
            _bands[i].Color = Color.HSVToColor(0x35, hsv);
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
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (_coverColors == null || Width <= 0 || Height <= 0) return;

        var t = (SystemClock.ElapsedRealtime() - _startTime - _pauseOffset) / 1000f;

        for (int i = 0; i < _bands.Length; i++)
        {
            var band = _bands[i];
            // 多重正弦波叠加：x/y 位置 + 缩放呼吸
            float x = Width * (0.3f + 0.4f * (float)Math.Sin(t * band.Speed + band.Phase));
            float y = Height * (0.2f + 0.3f * (float)Math.Sin(t * band.Speed * 0.7f + band.Phase + 1.5f));
            float radius = Math.Min(Width, Height) * (0.35f + 0.1f * (float)Math.Sin(t * band.Speed * 0.5f + band.Phase + 3f));

            _blurPaint.SetShader(new RadialGradient(x, y, radius, band.Color, Color.Transparent, Shader.TileMode.Clamp));
            canvas.DrawPaint(_blurPaint);
        }
    }

    private void DoFrame()
    {
        if (!_running) return;
        Invalidate();
        if (_frameCallback != null)
            Choreographer.Instance!.PostFrameCallback(_frameCallback);
    }

    private class FrameCallbackImpl : Java.Lang.Object, Choreographer.IFrameCallback
    {
        private readonly FlowLightView _view;
        public FrameCallbackImpl(FlowLightView view) => _view = view;
        public void DoFrame(long frameTimeNanos) => _view.DoFrame();
    }

    private class FlowBand
    {
        public float Phase;
        public float Speed;
        public Color Color;
    }
}
