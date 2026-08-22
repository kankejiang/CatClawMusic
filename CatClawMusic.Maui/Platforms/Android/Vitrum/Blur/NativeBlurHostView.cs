using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Microsoft.Maui.Platform;

namespace Vitrum.Android;

/// <summary>
/// Native Android view that owns the <see cref="BlurEngine"/>.
/// Intercepts <c>DispatchDraw</c> to capture the background content before
/// the normal draw pass renders the consumers on top.
/// </summary>
public class NativeBlurHostView : ContentViewGroup
{
    public BlurEngine Engine { get; }

    // 防止 OnDescendantInvalidated 触发宿主失效形成重绘风暴/死循环：
    // - _invalidateScheduled：同一绘制周期内只失效一次（DispatchDraw 时清除）
    // - 时间节流：宿主失效频率上限 ~30fps，与引擎捕获节流一致，避免主线程过载
    bool _invalidateScheduled;
    long _lastInvalidateTime;
    const long InvalidateIntervalMs = 33;

    public NativeBlurHostView(Context context) : base(context)
    {
        Engine = new BlurEngine(this);
        SetWillNotDraw(false);
    }

    protected override void DispatchDraw(Canvas? canvas)
    {
        _invalidateScheduled = false;
        if (canvas != null)
            Engine.CaptureChild(canvas);

        base.DispatchDraw(canvas);
    }

    /// <summary>
    /// 内容滚动时只有子视图的 RenderNode 被重绘，宿主本身不会被重绘（Android HW 渲染只重绘脏节点），
    /// 导致毛玻璃捕获不到滚动后的新内容。这里在任意后代失效时让宿主也失效，触发重新捕获。
    /// 捕获本身有 33ms 节流，不会造成过度开销。
    /// </summary>
    public override void OnDescendantInvalidated(global::Android.Views.View child, global::Android.Views.View target)
    {
        base.OnDescendantInvalidated(child, target);
        long now = SystemClock.UptimeMillis();
        if (_invalidateScheduled || now - _lastInvalidateTime < InvalidateIntervalMs) return;
        _invalidateScheduled = true;
        _lastInvalidateTime = now;
        // 滚动时只有叶子视图的 RenderNode 被重绘，内容容器本身的 display list 仍是缓存的旧内容，
        // 导致 DrawChildInto 捕获不到滚动后的新内容。这里显式失效内容容器，强制其 display list 重新记录。
        //if (ChildCount > 0)
        //    GetChildAt(0)?.Invalidate();
        Invalidate();
    }

    /// <summary>
    /// Draws a child view into an external canvas using <c>ViewGroup.drawChild()</c> —
    /// Android's internal HW-accelerated path that records live display list references
    /// for all descendant views rather than going through the public software draw path.
    /// </summary>
    public void DrawChildInto(Canvas canvas, global::Android.Views.View child)
        => DrawChild(canvas, child, SystemClock.UptimeMillis());
}
