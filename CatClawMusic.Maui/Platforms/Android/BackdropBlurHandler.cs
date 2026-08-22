using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;
using CatClawMusic.Maui.Controls;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using View = Android.Views.View;

namespace CatClawMusic.Maui.Platforms.Android;

/// <summary>
/// 真毛玻璃背景视图：在 OnDraw 中平移画布后直接让下层目标视图把自己画到本视图的 canvas 上，
/// 再用 RenderEffect 对本视图做 GPU 高斯模糊，实现"悬浮栏透出下层列表内容"的效果。
///
/// 关键设计：
/// 1. 不加任何 OnDraw/OnScroll 监听 → 不会形成 Invalidate 递归
/// 2. 只在 SetTarget / OnSizeChanged 时触发一次抓帧式重绘
/// 3. 列表滚动等需要更新场景由外部显式调用 Refresh()
/// 4. 目标不在 XAML 里用 x:Reference 绑定，而是代码后置延迟赋值 → 避免布局阶段递归 measure
/// </summary>
public class BackdropBlurView : View
{
    private View? _target;
    private float _blurRadiusDp = 24f;
    private readonly int[] _selfLoc = new int[2];
    private readonly int[] _targetLoc = new int[2];
    private bool _attached;

    public BackdropBlurView(Context context) : base(context)
    {
        SetLayerType(LayerType.Hardware, null);
        SetWillNotDraw(false);
    }

    protected override void OnAttachedToWindow()
    {
        _attached = true;
        base.OnAttachedToWindow();
        ApplyBlur();
        if (_target != null) Invalidate();
    }

    protected override void OnDetachedFromWindow()
    {
        _attached = false;
        base.OnDetachedFromWindow();
    }

    public void SetTarget(View? target)
    {
        if (ReferenceEquals(_target, target)) return;
        _target = target;
        if (_attached) Invalidate();
    }

    /// <summary>设置模糊半径（dp）</summary>
    public void SetBlurRadius(double dp)
    {
        if (_blurRadiusDp == (float)dp) return;
        _blurRadiusDp = (float)Math.Max(1, dp);
        ApplyBlur();
        if (_attached) Invalidate();
    }

    /// <summary>手动触发重绘（列表滚动/内容变化时调用）</summary>
    public void Refresh()
    {
        if (_attached && _target != null)
            PostInvalidate();
    }

    private void ApplyBlur()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.S) return;
        try
        {
            var density = Resources?.DisplayMetrics?.Density ?? 2f;
            var radius = _blurRadiusDp * density;
            SetRenderEffect(RenderEffect.CreateBlurEffect(radius, radius, Shader.TileMode.Clamp)!);
        }
        catch
        {
            try { SetRenderEffect(null); } catch { }
        }
    }

    protected override void OnDraw(Canvas? canvas)
    {
        base.OnDraw(canvas);
        if (canvas == null || _target == null) return;

        // 把画布平移到目标视图坐标系，然后让目标视图把自己画到本画布上
        GetLocationInWindow(_selfLoc);
        _target.GetLocationInWindow(_targetLoc);

        canvas.Save();
        canvas.Translate(_targetLoc[0] - _selfLoc[0], _targetLoc[1] - _selfLoc[1]);
        _target.Draw(canvas);
        canvas.Restore();
    }

    protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
    {
        base.OnSizeChanged(w, h, oldw, oldh);
        if (w != oldw || h != oldh)
            Invalidate();
    }
}

/// <summary>BackdropBlur 的 MAUI Handler，映射到原生 BackdropBlurView</summary>
public class BackdropBlurHandler : ViewHandler<BackdropBlur, BackdropBlurView>
{
    public static readonly IPropertyMapper<BackdropBlur, BackdropBlurHandler> PropertyMapper =
        new PropertyMapper<BackdropBlur, BackdropBlurHandler>(ViewHandler.ViewMapper)
        {
            [nameof(BackdropBlur.Target)] = MapTarget,
            [nameof(BackdropBlur.BlurRadius)] = MapBlurRadius,
        };

    private Element? _targetSub;
    private EventHandler? _targetHandlerChangedSub;

    public BackdropBlurHandler() : base(PropertyMapper) { }

    protected override BackdropBlurView CreatePlatformView() => new(Context!);

    protected override void ConnectHandler(BackdropBlurView platformView)
    {
        base.ConnectHandler(platformView);
        MapTarget(this, VirtualView);
    }

    protected override void DisconnectHandler(BackdropBlurView platformView)
    {
        UnsubscribeTarget();
        base.DisconnectHandler(platformView);
    }

    private void SubscribeTarget()
    {
        if (VirtualView.Target is not Element t) return;
        UnsubscribeTarget();
        _targetSub = t;
        if (t.Handler != null) return;
        _targetHandlerChangedSub = (_, _) =>
        {
            var nv = _targetSub?.Handler?.PlatformView as View;
            PlatformView.SetTarget(nv);
        };
        t.HandlerChanged += _targetHandlerChangedSub;
    }

    private bool UnsubscribeTarget()
    {
        if (_targetSub != null && _targetHandlerChangedSub != null)
            _targetSub.HandlerChanged -= _targetHandlerChangedSub;
        var had = _targetSub != null;
        _targetSub = null;
        _targetHandlerChangedSub = null;
        return had;
    }

    private static void MapTarget(BackdropBlurHandler handler, BackdropBlur view)
    {
        var native = view.Target?.Handler?.PlatformView as View;
        handler.PlatformView.SetTarget(native);
        handler.SubscribeTarget();
    }

    private static void MapBlurRadius(BackdropBlurHandler handler, BackdropBlur view)
    {
        handler.PlatformView.SetBlurRadius(view.BlurRadius);
    }
}
