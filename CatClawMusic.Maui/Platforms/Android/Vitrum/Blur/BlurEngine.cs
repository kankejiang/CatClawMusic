using Android.Graphics;
using Android.OS;
using Android.Views;

namespace Vitrum.Android;

/// <summary>
/// Core GPU blur engine using Android 12+ <c>RenderNode</c> + <c>RenderEffect</c>.
/// </summary>
/// <remarks>
/// Scale and blur radius are tuned for high-performance devices:
/// <list type="bullet">
///   <item>Node size = screen in dp (width / density, height / density)</item>
///   <item>Content drawn at 1/density scale into the node</item>
///   <item>BlurRadius 60 applied in dp coordinate space</item>
///   <item>Node drawn back at density scale — visual blur = 60dp on screen</item>
/// </list>
/// On older devices the consumer falls back to drawing a flat tint color rectangle.
/// </remarks>
public class BlurEngine
{
    /// <summary>Saturation multiplier chained after the blur for the frosted glass look.</summary>
    const float Saturation = 2f;

    readonly NativeBlurHostView _host;
    readonly List<WeakReference<NativeBlurConsumerView>> _consumers = new();

    RenderNode? _blurNode;
    RenderNode? _rawNode;
    float _lastDensity;
    float _blurRadiusDp = 60f;
    int _captureBackground = 0;
    // 捕获节流：宿主 DispatchDraw 与每个消费者 DispatchDraw 都会触发 CaptureLive，
    // 同一帧内可能重复捕获 3 次（1 宿主 + 2 消费者）。按时间节流后每 33ms 至多捕获一次，
    // 模糊以 ~30fps 更新（对模糊效果视觉无差别），显著降低滚动时主线程 CPU 开销。
    long _lastCaptureTime;
    const long CaptureIntervalMs = 33;

    public BlurEngine(NativeBlurHostView host) => _host = host;

    /// <summary>
    /// Color pre-filled into the recording canvas before drawing content.
    /// Set to the page background color for a denser frosted effect.
    /// </summary>
    public void SetCaptureBackground(int argb)
    {
        _captureBackground = argb;
        _blurNode = null;
        _lastCaptureTime = 0;
        InvalidateConsumers();
    }

    /// <summary>Updates the blur radius (in dp) and invalidates the cached RenderNode.</summary>
    public void SetBlurRadius(float dp)
    {
        _blurRadiusDp = dp;
        _blurNode = null;
        _lastCaptureTime = 0;
        InvalidateConsumers();
    }

    /// <summary>True when at least one live consumer is registered.</summary>
    public bool HasConsumers => _consumers.Any(w => w.TryGetTarget(out _));

    /// <summary>Registers a consumer to receive the blurred texture.</summary>
    public void RegisterConsumer(NativeBlurConsumerView consumer)
        => _consumers.Add(new WeakReference<NativeBlurConsumerView>(consumer));

    /// <summary>Unregisters a consumer (called on handler disconnect).</summary>
    public void UnregisterConsumer(NativeBlurConsumerView consumer)
        => _consumers.RemoveAll(w => !w.TryGetTarget(out var v) || v == consumer);

    RenderNode EnsureNode(float density)
    {
        if (_blurNode == null || density != _lastDensity)
        {
            _lastDensity = density;
            _blurNode = new RenderNode("vitrum_blur");
            var sat = new ColorMatrix();
            sat.SetSaturation(Saturation);
            _blurNode.SetRenderEffect(
                RenderEffect.CreateChainEffect(
                    RenderEffect.CreateBlurEffect(_blurRadiusDp, _blurRadiusDp, Shader.TileMode.Clamp!),
                    RenderEffect.CreateColorFilterEffect(new ColorMatrixColorFilter(sat))));
        }
        return _blurNode;
    }

    /// <summary>
    /// Records the host's child content into the blur RenderNode.
    /// Uses <c>ViewGroup.drawChild()</c> which records live hardware-accelerated
    /// display list references for all child views — safe to call from
    /// <see cref="DrawBlurOnto"/> without touching consumer visibility.
    /// </summary>
    void CaptureLive(float density)
    {
        long now = SystemClock.UptimeMillis();
        if (now - _lastCaptureTime < CaptureIntervalMs) return;
        if (_host.ChildCount == 0) return;
        var content = _host.GetChildAt(0);
        if (content == null || content.Width == 0 || content.Height == 0) return;
        _lastCaptureTime = now;

        int bw = Math.Max(1, (int)(_host.Width / density));
        int bh = Math.Max(1, (int)(_host.Height / density));

        var node = EnsureNode(density);
        node.SetPosition(0, 0, bw, bh);

        var rc = node.BeginRecording(bw, bh);
        rc.Scale(1f / density, 1f / density);
        if (_captureBackground != 0)
            rc.DrawColor(new global::Android.Graphics.Color(_captureBackground));
        _host.DrawChildInto(rc, content);
        node.EndRecording();
    }

    void CaptureRaw(float density)
    {
        if (_host.ChildCount == 0) return;
        var content = _host.GetChildAt(0);
        if (content == null || content.Width == 0 || content.Height == 0) return;

        int bw = Math.Max(1, (int)(_host.Width / density));
        int bh = Math.Max(1, (int)(_host.Height / density));

        _rawNode ??= new RenderNode("vitrum_raw");
        _rawNode.SetPosition(0, 0, bw, bh);

        var rc = _rawNode.BeginRecording(bw, bh);
        rc.Scale(1f / density, 1f / density);
        if (_captureBackground != 0)
            rc.DrawColor(new global::Android.Graphics.Color(_captureBackground));
        _host.DrawChildInto(rc, content);
        _rawNode.EndRecording();
    }

    /// <summary>
    /// Called from <see cref="NativeBlurHostView.DispatchDraw"/> on every frame.
    /// Records the background content into the blur RenderNode, then invalidates
    /// consumers so they redraw with the latest blurred texture.
    /// </summary>
    /// <remarks>
    /// Consumers are siblings of the host (never inside its content subtree), so
    /// <c>DrawChildInto</c> never records them — no visibility toggling is needed.
    /// Toggling visibility here caused an infinite redraw loop on complex layouts
    /// (ViewPager + multiple CollectionViews) that froze the UI thread.
    /// </remarks>
    public void CaptureChild(Canvas hostCanvas)
    {
        if (!hostCanvas.IsHardwareAccelerated) return;
        if (Build.VERSION.SdkInt < BuildVersionCodes.S) return;

        float density = _host.Context!.Resources!.DisplayMetrics!.Density;

        CaptureLive(density);
        InvalidateConsumers();
    }

    /// <summary>
    /// Called from <see cref="NativeBlurConsumerView.DispatchDraw"/>.
    /// Clips the canvas, aligns to host origin, draws the blur node, then draws the tint overlay.
    /// <paramref name="parentScaleX"/> and <paramref name="parentScaleY"/> are the consumer's
    /// current ScaleX/ScaleY. When non-1, the draw scale is reduced to density/parentScale so
    /// the texture covers the correct visual area at 1:1 pixels rather than stretching.
    /// </summary>
    public void DrawBlurOnto(Canvas canvas, int consLeft, int consTop,
                             int consWidth, int consHeight, int tintColor,
                             float parentScaleX = 1f, float parentScaleY = 1f)
    {
        using var scrimPaint = new global::Android.Graphics.Paint(PaintFlags.AntiAlias);
        scrimPaint.Color = new global::Android.Graphics.Color(tintColor);

        canvas.Save();
        canvas.ClipRect(0, 0, consWidth, consHeight);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.S && canvas.IsHardwareAccelerated)
        {
            float density = _host.Context!.Resources!.DisplayMetrics!.Density;

            CaptureLive(density);

            if (_blurNode != null)
            {
                _blurNode.SetAlpha(1f);
                canvas.Save();
                // Divide by parentScale so the effective draw scale stays at density regardless
                // of any scale animation applied to the consumer view.
                canvas.Scale(density / parentScaleX, density / parentScaleY);
                canvas.Translate(-(consLeft / density), -(consTop / density));
                canvas.DrawRenderNode(_blurNode);
                canvas.Restore();
            }
        }

        canvas.DrawRect(0, 0, consWidth, consHeight, scrimPaint);
        canvas.Restore();
    }

    /// <summary>Returns the host view's position in the window coordinate space.</summary>
    public void GetHostLocationInWindow(int[] outLoc)
        => _host.GetLocationInWindow(outLoc);

    /// <summary>
    /// Captures both the blurred and raw background frames for the liquid glass path.
    /// Call this before <see cref="DrawRawNodeOnto"/> from the consumer's DispatchDraw.
    /// </summary>
    public void EnsureCapture(Canvas canvas)
    {
        if (!canvas.IsHardwareAccelerated) return;
        if (Build.VERSION.SdkInt < BuildVersionCodes.S) return;
        float density = _host.Context!.Resources!.DisplayMetrics!.Density;
        CaptureLive(density);
        CaptureRaw(density);
    }

    /// <summary>
    /// Draws the raw (unblurred) <c>RenderNode</c> into <paramref name="canvas"/> aligned to
    /// the consumer's visual position. Used inside the lens node's recording canvas so the
    /// lens shader receives blur-then-refract ordering via the lens node's own ChainEffect.
    /// <paramref name="parentScaleX/Y"/> compensate for the consumer's scale animation: the
    /// recording is drawn at density/parentScale so that when the lens node is rendered at
    /// parentScale on screen, the background content fills the visual bounds at 1:1 pixels.
    /// </summary>
    /// <returns><c>false</c> if the raw node has not been captured yet.</returns>
    public bool DrawRawNodeOnto(Canvas canvas, int consLeft, int consTop, float density,
                                float parentScaleX = 1f, float parentScaleY = 1f)
    {
        if (_rawNode == null) return false;
        canvas.Save();
        canvas.Scale(density / parentScaleX, density / parentScaleY);
        canvas.Translate(-(consLeft / density), -(consTop / density));
        canvas.DrawRenderNode(_rawNode);
        canvas.Restore();
        return true;
    }

    /// <summary>
    /// Draws the cached blur <c>RenderNode</c> into <paramref name="canvas"/> at the correct
    /// offset so it aligns with the consumer's screen position. No tint or clip is applied.
    /// </summary>
    /// <returns><c>false</c> if the blur node has not been captured yet.</returns>
    public bool DrawBlurNodeOnto(Canvas canvas, int consLeft, int consTop, float density)
    {
        if (_blurNode == null) return false;
        canvas.Save();
        canvas.Scale(density, density);
        canvas.Translate(-(consLeft / density), -(consTop / density));
        canvas.DrawRenderNode(_blurNode);
        canvas.Restore();
        return true;
    }

    void InvalidateConsumers()
    {
        foreach (var w in _consumers)
            if (w.TryGetTarget(out var c))
                c.Invalidate();
    }
}
