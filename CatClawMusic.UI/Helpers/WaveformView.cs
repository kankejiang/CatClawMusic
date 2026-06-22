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
    private View[]? _bars;
    private ObjectAnimator[]? _animators;
    private bool _isPlaying;

    public WaveformView(Context context) : base(context) { Init(); }
    public WaveformView(Context context, IAttributeSet attrs) : base(context, attrs) { Init(); }
    public WaveformView(Context context, IAttributeSet attrs, int defStyleAttr) : base(context, attrs, defStyleAttr) { Init(); }

    /// <summary>初始化波形视图，仅设置基本布局属性，不创建子视图</summary>
    private void Init()
    {
        Orientation = Android.Widget.Orientation.Horizontal;
        SetGravity(GravityFlags.Center);
    }

    /// <summary>延迟创建三根彩色竖条，仅在需要显示时才构建</summary>
    private void EnsureBarsCreated()
    {
        if (_bars != null) return;

        var context = Context!;
        var colors = new[] { "#D87E9B", "#EDB8C9", "#D87E9B" };
        var dp = Resources.DisplayMetrics.Density;

        _bars = new View[3];
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

    /// <summary>延迟创建属性动画，仅在实际播放时才构建</summary>
    private void EnsureAnimatorsCreated()
    {
        if (_animators != null) return;
        if (_bars == null) return;

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
    }

    /// <summary>设置播放状态，控制动画启停和视图可见性</summary>
    public void SetPlaying(bool isPlaying)
    {
        if (_isPlaying == isPlaying) return;
        _isPlaying = isPlaying;

        if (isPlaying)
        {
            EnsureBarsCreated();
            Visibility = ViewStates.Visible;
            EnsureAnimatorsCreated();
            StartAnimations();
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
        if (_animators == null) return;
        foreach (var a in _animators) a.Start();
    }

    /// <summary>停止所有动画并重置竖条比例</summary>
    private void StopAnimations()
    {
        if (_animators != null)
        {
            foreach (var a in _animators) a.Cancel();
        }
        if (_bars != null)
        {
            foreach (var bar in _bars) bar.ScaleY = 0.3f;
        }
    }

    /// <summary>从窗口分离时取消并释放所有动画资源</summary>
    protected override void OnDetachedFromWindow()
    {
        base.OnDetachedFromWindow();
        if (_animators != null)
        {
            foreach (var a in _animators)
            {
                a.Cancel();
                a.Dispose();
            }
            _animators = null;
        }
    }
}
