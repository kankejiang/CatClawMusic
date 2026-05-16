using Android.Animation;
using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.Views;
using Android.Views.Animations;
using Android.Widget;

namespace CatClawMusic.UI.Helpers;

/// <summary>音频波形动画视图，显示三根跳动的竖条表示播放状态</summary>
public class WaveformView : LinearLayout
{
    private readonly View[] _bars = new View[3];
    private ObjectAnimator[] _animators = Array.Empty<ObjectAnimator>();
    private bool _isPlaying;
    private bool _initialized;

    public WaveformView(Context context) : base(context) { Init(context); }
    public WaveformView(Context context, IAttributeSet attrs) : base(context, attrs) { Init(context); }
    public WaveformView(Context context, IAttributeSet attrs, int defStyleAttr) : base(context, attrs, defStyleAttr) { Init(context); }

    /// <summary>初始化波形视图，创建三根彩色竖条</summary>
    private void Init(Context context)
    {
        Orientation = Android.Widget.Orientation.Horizontal;
        SetGravity(GravityFlags.Center);

        var colors = new[] { "#D87E9B", "#EDB8C9", "#D87E9B" };
        var dp = Resources.DisplayMetrics.Density;

        for (int i = 0; i < 3; i++)
        {
            var bar = new View(context);
            var lp = new LayoutParams((int)(3 * dp), (int)(18 * dp));
            if (i > 0) lp.MarginStart = (int)(3 * dp);
            bar.LayoutParameters = lp;
            bar.SetBackgroundColor(Color.ParseColor(colors[i]));
            bar.ScaleY = 0.3f;
            _bars[i] = bar;
            AddView(bar);
        }
    }

    /// <summary>尺寸确定后创建属性动画，设置交替弹跳效果</summary>
    protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
    {
        base.OnSizeChanged(w, h, oldw, oldh);
        if (_initialized) return;
        _initialized = true;

        var dp = Resources.DisplayMetrics.Density;
        var delays = new[] { 0L, 120L, 240L };
        _animators = new ObjectAnimator[3];

        for (int i = 0; i < 3; i++)
        {
            _bars[i].PivotY = (int)(18 * dp);

            var anim = ObjectAnimator.OfFloat(_bars[i], "scaleY", 0.3f, 1f, 0.3f);
            anim.SetDuration(700);
            anim.StartDelay = delays[i];
            anim.SetInterpolator(new AccelerateDecelerateInterpolator());
            anim.RepeatCount = ValueAnimator.Infinite;
            anim.RepeatMode = ValueAnimatorRepeatMode.Restart;
            _animators[i] = anim;
        }

        if (_isPlaying) StartAnimations();
    }

    /// <summary>设置播放状态，控制动画启停和视图可见性</summary>
    public void SetPlaying(bool isPlaying)
    {
        if (_isPlaying == isPlaying) return;
        _isPlaying = isPlaying;

        if (isPlaying)
        {
            Visibility = ViewStates.Visible;
            if (_initialized) StartAnimations();
        }
        else
        {
            StopAnimations();
            Visibility = ViewStates.Gone;
        }
    }

    /// <summary>启动所有竖条的弹跳动画</summary>
    private void StartAnimations()
    {
        foreach (var a in _animators) a.Start();
    }

    /// <summary>停止所有动画并重置竖条比例</summary>
    private void StopAnimations()
    {
        foreach (var a in _animators)
        {
            a.Cancel();
        }
        foreach (var bar in _bars)
        {
            bar.ScaleY = 0.3f;
        }
    }

    /// <summary>从窗口分离时取消并释放所有动画资源</summary>
    protected override void OnDetachedFromWindow()
    {
        base.OnDetachedFromWindow();
        foreach (var a in _animators)
        {
            a.Cancel();
            a.Dispose();
        }
        _animators = Array.Empty<ObjectAnimator>();
    }
}
