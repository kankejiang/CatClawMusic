using Android.Content;
using Android.Graphics;
using Android.OS;
using Microsoft.Maui.Platform;

namespace Vitrum.Android;

/// <summary>
/// Native Android view that renders the blurred texture produced by <see cref="BlurEngine"/>.
/// </summary>
/// <remarks>
/// On each draw pass it asks the engine for the host's window position, translates the
/// canvas so the blur region aligns with the consumer's screen position, draws the blur
/// <c>RenderNode</c>, then draws the tint overlay. Children (the consumer's MAUI content)
/// are drawn on top by <c>base.DispatchDraw</c>.
///
/// When <see cref="SetLiquidGlass"/> is enabled (Android 13+), the blurred content is first
/// recorded into a lens <c>RenderNode</c> that carries an AGSL RuntimeShader as its
/// RenderEffect. The shader applies a SDF-based edge refraction (Kyant0 backdrop, Apache 2.0)
/// producing an iOS-style liquid glass look on top of the existing blur.
/// </remarks>
public class NativeBlurConsumerView : ContentViewGroup
{
    // -----------------------------------------------------------------------
    // Liquid glass shader — based on QmDeve/AndroidLiquidGlassView (MIT)
    // https://github.com/QmDeve/AndroidLiquidGlassView
    //
    // Changes from original:
    //   - radiusAt() receives centeredCoord so per-corner radii map correctly
    //   - gradRadius uses max(radius*1.5, 30) clamp from QmDeve for smooth edges
    //   - tint uniforms removed (tint is drawn as a separate rect by the host)
    // -----------------------------------------------------------------------
    const string LensShaderSource = @"
uniform shader content;
uniform float2 size;
uniform float2 offset;
uniform float4 cornerRadii;
uniform float refractionHeight;
uniform float refractionAmount;
uniform float depthEffect;
uniform float chromaticAberration;
uniform float contrast;
uniform float whitePoint;
uniform float chromaMultiplier;

const half3 rgbToY = half3(0.2126, 0.7152, 0.0722);

float radiusAt(float2 centeredCoord, float4 radii) {
    if (centeredCoord.x >= 0.0) {
        if (centeredCoord.y <= 0.0) return radii.y;
        else return radii.z;
    } else {
        if (centeredCoord.y <= 0.0) return radii.x;
        else return radii.w;
    }
}

float sdRoundedRect(float2 coord, float2 halfSize, float radius) {
    float2 cornerCoord = abs(coord) - (halfSize - float2(radius));
    float outside = length(max(cornerCoord, 0.0)) - radius;
    float inside = min(max(cornerCoord.x, cornerCoord.y), 0.0);
    return outside + inside;
}

float2 gradSdRoundedRect(float2 coord, float2 halfSize, float radius) {
    float2 cornerCoord = abs(coord) - (halfSize - float2(radius));
    if (cornerCoord.x >= 0.0 || cornerCoord.y >= 0.0) {
        return sign(coord) * normalize(max(cornerCoord, 0.0));
    } else {
        float gradX = step(cornerCoord.y, cornerCoord.x);
        return sign(coord) * float2(gradX, 1.0 - gradX);
    }
}

float circleMap(float x) {
    return 1.0 - sqrt(1.0 - x * x);
}

half4 gradeColor(half4 color) {
    half3 lin = toLinearSrgb(color.rgb);
    float y = dot(lin, rgbToY);
    half3 gray = half3(y);
    half3 sat = fromLinearSrgb(mix(gray, lin, chromaMultiplier));
    half4 result = half4(sat, color.a);
    float3 target = (whitePoint > 0.0) ? float3(1.0) : float3(0.0);
    result.rgb = mix(result.rgb, target, abs(whitePoint));
    result.rgb = (result.rgb - 0.5) * (1.0 + contrast) + 0.5;
    return result;
}

half4 main(float2 coord) {
    float2 halfSize = size * 0.5;
    float2 centeredCoord = (coord + offset) - halfSize;
    float radius = radiusAt(centeredCoord, cornerRadii);

    float sd = sdRoundedRect(centeredCoord, halfSize, radius);
    if (-sd >= refractionHeight) {
        return gradeColor(content.eval(coord));
    }

    sd = min(sd, 0.0);
    float d = circleMap(1.0 - -sd / refractionHeight) * refractionAmount;
    float smoothRadius = max(radius * 1.5, 30.0);
    float gradRadius = min(smoothRadius, min(halfSize.x, halfSize.y));
    float2 grad = normalize(gradSdRoundedRect(centeredCoord, halfSize, gradRadius) + depthEffect * normalize(centeredCoord));

    float2 refractedCoord = coord + d * grad;
    // Disperse perpendicular to the nearest edge (SDF gradient direction) so the
    // rainbow fringe is visible on straight top/bottom edges, not only at corners.
    float2 dispersedCoord = d * grad * chromaticAberration;

    half4 color = half4(0.0);
    half4 red    = content.eval(refractedCoord + dispersedCoord);
    color.r += red.r / 3.5;  color.a += red.a / 7.0;
    half4 orange = content.eval(refractedCoord + dispersedCoord * (2.0 / 3.0));
    color.r += orange.r / 3.5;  color.g += orange.g / 7.0;  color.a += orange.a / 7.0;
    half4 yellow = content.eval(refractedCoord + dispersedCoord * (1.0 / 3.0));
    color.r += yellow.r / 3.5;  color.g += yellow.g / 3.5;  color.a += yellow.a / 7.0;
    half4 green  = content.eval(refractedCoord);
    color.g += green.g / 3.5;  color.a += green.a / 7.0;
    half4 cyan   = content.eval(refractedCoord - dispersedCoord * (1.0 / 3.0));
    color.g += cyan.g / 3.5;  color.b += cyan.b / 3.0;  color.a += cyan.a / 7.0;
    half4 blue   = content.eval(refractedCoord - dispersedCoord * (2.0 / 3.0));
    color.b += blue.b / 3.0;  color.a += blue.a / 7.0;
    half4 purple = content.eval(refractedCoord - dispersedCoord);
    color.r += purple.r / 7.0;  color.b += purple.b / 3.0;  color.a += purple.a / 7.0;

    return gradeColor(color);
}";

    // -----------------------------------------------------------------------
    // State
    // -----------------------------------------------------------------------

    int _tintColor = unchecked((int)0x661C1C25);
    BlurEngine? _engine;

    bool _blurEnabled = true;
    bool _liquidGlass;
    float _cornerRadiusPx;

    // API 33+ liquid glass resources — null on older devices
    RuntimeShader? _lensShader;
    RenderNode? _lensNode;
    int _lensNodeWidth;
    int _lensNodeHeight;
    bool _lensShaderDirty;

    // Captured glass surface (lens + tint) shared with pill consumers.
    RenderNode? _glassLayerNode;

    /// <summary>The fully-composited glass surface recorded each frame. Null until first liquid-glass draw.</summary>
    public RenderNode? GlassLayerNode => _glassLayerNode;

    /// <summary>The blur engine powering this consumer. Used by pill consumers to access raw/blur nodes.</summary>
    public BlurEngine? Engine => _engine;

    // -----------------------------------------------------------------------

    public NativeBlurConsumerView(Context context) : base(context)
    {
        SetWillNotDraw(false);
    }

    public void SetTintColor(int argb) { _tintColor = argb; Invalidate(); }
    public void SetBlurEnabled(bool enabled) { _blurEnabled = enabled; Invalidate(); }
    public void AttachEngine(BlurEngine engine) => _engine = engine;
    public void DetachEngine() => _engine = null;

    // True when the engine was found via the native walk in OnAttachedToWindow
    // rather than via the MAUI handler ConnectHandler path.
    bool _engineFromNativeWalk;

    protected override void OnAttachedToWindow()
    {
        base.OnAttachedToWindow();
        if (_engine != null) return;

        // MAUI ConnectHandler fired before this view was in the element tree so
        // FindHostHandler() returned null. Walk the native Android hierarchy now
        // that we are guaranteed to be attached: scan ancestors and their siblings
        // for a NativeBlurHostView, mirroring what FindHostHandler does in MAUI.
        global::Android.Views.View? current = this;
        while (current?.Parent is global::Android.Views.ViewGroup parent)
        {
            for (int i = 0; i < parent.ChildCount; i++)
            {
                if (parent.GetChildAt(i) is NativeBlurHostView host)
                {
                    AttachEngine(host.Engine);
                    host.Engine.RegisterConsumer(this);
                    _engineFromNativeWalk = true;
                    return;
                }
            }
            current = parent as global::Android.Views.View;
        }
    }

    protected override void OnDetachedFromWindow()
    {
        base.OnDetachedFromWindow();
        if (_engineFromNativeWalk && _engine != null)
        {
            _engine.UnregisterConsumer(this);
            DetachEngine();
            _engineFromNativeWalk = false;
        }
    }

    /// <summary>
    /// Enables or disables the liquid glass lens effect.
    /// <paramref name="cornerRadiusPx"/> is the corner radius in physical pixels,
    /// converted from dp by the handler before calling this.
    /// </summary>
    public void SetLiquidGlass(bool enabled, float cornerRadiusPx)
    {
        _liquidGlass = enabled;
        _cornerRadiusPx = cornerRadiusPx;
        _lensShaderDirty = true;
        Invalidate();
    }

    // Returns the view's visual top-left in window px and its own scale factors.
    // Uses ScaleX/Y + PivotX/Y rather than canvas.GetMatrix() because HW-accelerated
    // recording canvases do not reliably expose the composite CTM.
    (float left, float top, float sx, float sy) VisualOrigin()
    {
        int[] loc = new int[2];
        GetLocationInWindow(loc);
        float sx = ScaleX, sy = ScaleY;
        // Pivot is in view-local px; shift layout origin to account for scale around pivot.
        return (loc[0] + PivotX * (1f - sx),
                loc[1] + PivotY * (1f - sy),
                sx, sy);
    }

    protected override void DispatchDraw(Canvas canvas)
    {
        if (_engine != null && _blurEnabled)
        {
            if (_liquidGlass
                && canvas.IsHardwareAccelerated
                && Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            {
                DrawLiquidGlass(canvas);
            }
            else
            {
                var (vLeft, vTop, sx, sy) = VisualOrigin();
                int[] hostLoc = new int[2];
                _engine.GetHostLocationInWindow(hostLoc);
                _engine.DrawBlurOnto(canvas,
                    (int)(vLeft - hostLoc[0]),
                    (int)(vTop  - hostLoc[1]),
                    Width, Height,
                    _tintColor,
                    sx, sy);
            }
        }

        base.DispatchDraw(canvas);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android33.0")]
    void DrawLiquidGlass(Canvas canvas)
    {
        if (Width == 0 || Height == 0) return;

        var (vLeft, vTop, sx, sy) = VisualOrigin();
        int[] hostLoc = new int[2];
        _engine!.GetHostLocationInWindow(hostLoc);
        int offsetX = (int)(vLeft - hostLoc[0]);
        int offsetY = (int)(vTop  - hostLoc[1]);
        float density = Context!.Resources!.DisplayMetrics!.Density;

        // Capture the background into the blur RenderNode.
        _engine.EnsureCapture(canvas);

        EnsureLensNode(Width, Height, density);

        // Record raw (unblurred) content into the lens node.
        // The lens node's RenderEffect chain applies blur first, then the lens shader,
        // so the shader sees focused edges and can produce visible refraction distortion.
        _lensNode!.SetPosition(0, 0, Width, Height);
        var rc = _lensNode.BeginRecording(Width, Height);
        _engine.DrawRawNodeOnto(rc, offsetX, offsetY, density, sx, sy);
        _lensNode.EndRecording();

        // Record lens + tint together into _glassLayerNode so pill consumers can sample
        // this glass surface as their backdrop (glass-within-glass depth effect).
        _glassLayerNode ??= new RenderNode("vitrum_glass_layer");
        _glassLayerNode.SetPosition(0, 0, Width, Height);
        var glassRc = _glassLayerNode.BeginRecording(Width, Height);
        glassRc.DrawRenderNode(_lensNode);
        int tintAlpha = ((_tintColor >> 24) & 0xFF);
        if (tintAlpha != 0)
        {
            using var tintPaint = new global::Android.Graphics.Paint(PaintFlags.AntiAlias);
            tintPaint.Color = new global::Android.Graphics.Color(_tintColor);
            glassRc.DrawRect(0, 0, Width, Height, tintPaint);
        }
        _glassLayerNode.EndRecording();

        canvas.Save();
        canvas.ClipRect(0, 0, Width, Height);
        canvas.DrawRenderNode(_glassLayerNode);
        canvas.Restore();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android33.0")]
    void EnsureLensNode(int width, int height, float density)
    {
        bool nodeCreated = _lensNode == null;

        _lensShader ??= new RuntimeShader(LensShaderSource);
        _lensNode ??= new RenderNode("vitrum_lens");

        if (nodeCreated || width != _lensNodeWidth || height != _lensNodeHeight || _lensShaderDirty)
        {
            _lensNodeWidth = width;
            _lensNodeHeight = height;
            _lensShaderDirty = false;

            float r = _cornerRadiusPx;
            _lensShader.SetFloatUniform("size", width, height);
            _lensShader.SetFloatUniform("offset", 0f, 0f);
            _lensShader.SetFloatUniform("cornerRadii", r, r, r, r);
            _lensShader.SetFloatUniform("refractionHeight", 20f * density);
            _lensShader.SetFloatUniform("refractionAmount", -70f * density);
            _lensShader.SetFloatUniform("depthEffect", 0.3f);
            _lensShader.SetFloatUniform("chromaticAberration", 0.18f);
            _lensShader.SetFloatUniform("contrast", 0f);
            _lensShader.SetFloatUniform("whitePoint", 0.08f);
            _lensShader.SetFloatUniform("chromaMultiplier", 1.2f);

            // Chain: raw content -> blur (inner) -> lens shader (outer).
            // Blur is applied first by the GPU so the shader sees blurred pixels with
            // intact sharp edges — refraction distortion becomes clearly visible.
            if (nodeCreated)
                _lensNode.SetRenderEffect(
                    RenderEffect.CreateChainEffect(
                        RenderEffect.CreateRuntimeShaderEffect(_lensShader, "content"),
                        RenderEffect.CreateBlurEffect(30f, 30f, Shader.TileMode.Clamp!)));
        }
    }
}
