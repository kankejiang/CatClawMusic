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
/// 真毛玻璃背景视图：实时抓取其下层（Target 视图）中与本视图重叠的内容区域，
/// 通过 RenderEffect 做 GPU 高斯模糊，实现"悬浮栏透出下层列表内容"的效果。
/// 依赖下层 Target 的实际内容（如滚动的列表），因此必须让内容区延伸到栏位背后。
/// </summary>
public class BackdropBlurView : View
{
    private View? _target;          // 下层内容视图（同一窗口内的兄弟/祖先）
    private ViewTreeObserver.IOnDrawListener? _drawListener;
    private bool _invalidating;     // 防止递归失效
    private float _blurRadiusDp = 24f;
    private readonly int[] _selfLoc = new int[2];
    private readonly int[] _targetLoc = new int[2];

    public BackdropBlurView(Context context) : base(context)
    {
        SetLayerType(LayerType.Hardware, null);
        SetWillNotDraw(false);
    }

    protected override void OnAttachedToWindow()
    {
        base.OnAttachedToWindow();
        ApplyNativeBlur();
        // 监听下层内容的重绘：列表滚动/内容变化时刷新毛玻璃快照
        var root = FindRootContent();
        if (root != null && _drawListener == null)
        {
            _drawListener = new BlurDrawListener(this);
            root.ViewTreeObserver?.AddOnDrawListener(_drawListener);
        }
    }

    protected override void OnDetachedFromWindow()
    {
        if (_drawListener != null)
        {
            var root = FindRootContent();
            root?.ViewTreeObserver?.RemoveOnDrawListener(_drawListener);
            _drawListener = null;
        }
        base.OnDetachedFromWindow();
    }

    /// <summary>在层次树中找一个合适的根 ViewGroup 挂 OnDrawListener（优先目标视图的父级，跨 handler 监听）</summary>
    private ViewGroup? FindRootContent()
    {
        // 先尝试从 Target 出发向上找其父容器（内容区的布局根）
        if (_target != null)
        {
            var p = _target.Parent as ViewGroup;
            if (p != null) return p;
        }
        // 兜底：从自身向上找顶层 ViewGroup
        var cur = Parent as ViewGroup;
        if (cur == null) return null;
        while (cur.Parent is ViewGroup p2)
            cur = p2;
        return cur;
    }

    public void SetTarget(View? target)
    {
        _target = target;
        if (target != null)
        {
            // 目标与毛玻璃可能分属不同 handler 层，确保监听挂在能感知目标重绘的父级上
            var root = FindRootContent();
            if (_drawListener != null && root != null)
            {
                root.ViewTreeObserver?.AddOnDrawListener(_drawListener);
            }
            Invalidate();
        }
    }

    /// <summary>设置模糊半径（dp）</summary>
    public void SetBlurRadius(double dp)
    {
        if (_blurRadiusDp == (float)dp) return;
        _blurRadiusDp = (float)Math.Max(1, dp);
        ApplyNativeBlur();
    }

    private void ApplyNativeBlur()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.S) return; // minSdk=31，通常恒真
        try
        {
            var density = Resources?.DisplayMetrics?.Density ?? 2f;
            var radius = _blurRadiusDp * density;
            SetRenderEffect(RenderEffect.CreateBlurEffect(radius, radius, Shader.TileMode.Clamp)!);
        }
        catch
        {
            // 兼容性降级：不做模糊，仅透出内容
            try { SetRenderEffect(null); } catch { }
        }
    }

    protected override void OnDraw(Canvas? canvas)
    {
        base.OnDraw(canvas);
        if (canvas == null || _target == null) return;

        // 只有在下层内容存在且与本视图有重叠时才绘制
        GetLocationInWindow(_selfLoc);
        _target.GetLocationInWindow(_targetLoc);

        canvas.Save();
        // 把目标视图在其窗口坐标处的内容，平移到本视图坐标系绘制
        canvas.Translate(-(_selfLoc[0] - _targetLoc[0]), -(_selfLoc[1] - _targetLoc[1]));
        _target.Draw(canvas);
        canvas.Restore();
    }

    /// <summary>刷新毛玻璃快照（由 OnDrawListener 在内容每次绘制前触发）</summary>
    internal void RequestRefresh()
    {
        if (_invalidating) return;
        _invalidating = true;
        try { Invalidate(); }
        finally { _invalidating = false; }
    }

    private sealed class BlurDrawListener : Java.Lang.Object, ViewTreeObserver.IOnDrawListener
    {
        private readonly BackdropBlurView _owner;
        public BlurDrawListener(BackdropBlurView owner) => _owner = owner;
        public void OnDraw() => _owner.RequestRefresh();
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

    /// <summary>订阅目标元素的 HandlerChanged，目标原生视图延迟就绪时自动重映射透明视图。</summary>
    private void SubscribeTarget()
    {
        if (VirtualView.Target is not Element t) return;
        UnsubscribeTarget();
        _targetSub = t;
        // 目标 handler 已就绪则无需订阅（ConnectHandler 时已直连）
        if (t.Handler != null) return;
        _targetHandlerChangedSub = (_, _) =>
        {
            View? nv = _targetSub?.Handler?.PlatformView is View v ? v : null;
            PlatformView.SetTarget(nv);
        };
        t.HandlerChanged += _targetHandlerChangedSub;
    }

    /// <summary>解除目标订阅。返回 true 表示存在需要清理的订阅。</summary>
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