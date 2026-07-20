using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Animation;
using CatClawMusic.Maui.Controls;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Color = Microsoft.Maui.Graphics.Color;
using Colors = Microsoft.Maui.Graphics.Colors;
using Math = System.Math;
using Paint = Android.Graphics.Paint;
using Canvas = Android.Graphics.Canvas;
using Bitmap = Android.Graphics.Bitmap;
using Rect = Android.Graphics.Rect;
using RectF = Android.Graphics.RectF;
using Matrix = Android.Graphics.Matrix;
using View = Android.Views.View;
using Aspect = Microsoft.Maui.Aspect;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Maui.Services;

namespace CatClawMusic.Maui.Platforms.Android;

/// <summary>
/// 流光溢彩背景（参考 Apple Music 实现）：
/// 处理流程：缩小→高斯模糊→mesh变换→放大→mesh变换→放大→高斯模糊→色调处理
/// 配合 Ken Burns 随机缩放平移动画，形成流动的雾面背景。
/// 所有位图处理在后台线程执行，主线程仅负责绘制。
/// </summary>
public class FrostedBackgroundView : View
{
    /// <summary>
    /// Apple Music 风格的 mesh 参数（72个值，6x6 顶点网格）。
    /// 定义 mesh 变换的顶点位置（归一化坐标），去除重复感和撕裂感。
    /// </summary>
    private static readonly float[] AppleMusicMesh1 = new float[]
    {
        -0.2351f, -0.0967f, 0.2135f, -0.1414f, 0.9221f, -0.0908f, 0.9221f, -0.0685f, 1.3027f, 0.0253f, 1.2351f, 0.1786f,
        -0.3768f, 0.1851f, 0.2f, 0.2f, 0.6615f, 0.3146f, 0.9543f, 0.0f, 0.6969f, 0.1911f, 1.0f, 0.2f,
        0.0f, 0.4f, 0.2f, 0.4f, 0.0776f, 0.2318f, 0.6f, 0.4f, 0.6615f, 0.3851f, 1.0f, 0.4f,
        0.0f, 0.6f, 0.1291f, 0.6f, 0.4f, 0.6f, 0.4f, 0.4304f, 0.4264f, 0.5792f, 1.2029f, 0.8188f,
        -0.1192f, 1.0f, 0.6f, 0.8f, 0.4264f, 0.8104f, 0.6f, 0.8f, 0.8f, 0.8f, 1.0f, 0.8f,
        0.0f, 1.0f, 0.0776f, 1.0283f, 0.4f, 1.0f, 0.6f, 1.0f, 0.8f, 1.0f, 1.1868f, 1.0283f
    };

    /// <summary>第二组 mesh 参数（用于放大后的二次 mesh，与第一组略有差异）</summary>
    private static readonly float[] AppleMusicMesh2 = new float[]
    {
        -0.15f, -0.12f, 0.18f, -0.05f, 0.5f, -0.08f, 0.82f, -0.05f, 1.18f, -0.12f, 1.2f, 0.08f,
        -0.2f, 0.15f, 0.1f, 0.22f, 0.45f, 0.15f, 0.78f, 0.25f, 1.1f, 0.12f, 1.25f, 0.25f,
        -0.08f, 0.38f, 0.25f, 0.45f, 0.55f, 0.38f, 0.75f, 0.48f, 0.95f, 0.4f, 1.15f, 0.5f,
        -0.1f, 0.55f, 0.15f, 0.62f, 0.4f, 0.55f, 0.7f, 0.6f, 0.9f, 0.58f, 1.1f, 0.7f,
        -0.05f, 0.75f, 0.22f, 0.82f, 0.48f, 0.75f, 0.72f, 0.85f, 0.88f, 0.78f, 1.05f, 0.88f,
        -0.12f, 1.05f, 0.1f, 1.1f, 0.35f, 1.05f, 0.65f, 1.12f, 0.85f, 1.05f, 1.15f, 1.15f
    };

    private Bitmap? _sourceBitmap;
    private Bitmap? _processedBitmap;       // 当前显示的位图（过渡完成后）
    private string? _processedCacheKey;     // _processedBitmap 对应的缓存键（null 表示非缓存）
    private Bitmap? _previousBitmap;        // 过渡期间的旧位图（过渡完成后释放）
    private string? _previousCacheKey;      // _previousBitmap 对应的缓存键（null 表示非缓存）
    private ValueAnimator? _animator;       // 主动画（驱动所有动态效果）
    private ValueAnimator? _crossFadeAnimator;  // 切换歌曲时的交叉淡入淡出动画
    private float _crossFadeProgress = 1f;      // 0=完全旧位图，1=完全新位图
    private float _animTime;                 // 动画累计时间（秒）
    private readonly Random _random = new();
    private bool _isActive = true;  // 兼容旧代码，表示背景是否激活
    private Color _tintColor = Colors.Transparent;
    private double _tintOpacity = 0.35;
    private double _dimAmount = 0.35;
    private int _processingVersion = 0;  // 处理版本号：新请求递增，旧任务完成后发现版本不匹配则丢弃结果
    private Aspect _aspect = Aspect.AspectFill;
    private int _loadingVersion = 0;  // 实例级别的加载版本号（避免多实例间互相取消）
    private string? _cacheKey;  // 共享缓存键（如封面路径），用于跨实例共享处理后位图

    // 有机运动参数（每个实例随机化，避免重复感）
    private readonly float _driftAX;
    private readonly float _driftAY;
    private readonly float _driftBX;
    private readonly float _driftBY;
    private readonly float _driftSpeed;
    private readonly float _rotationSpeed;
    private readonly float _breathSpeed;
    private readonly float _breathAmount;

    // 预创建复用的 Paint（避免 OnDraw 中分配）
    private readonly Paint _bitmapPaint;
    private readonly Paint _tintPaint;
    private readonly Paint _dimPaint;
    private readonly Rect _srcRect = new();
    private readonly RectF _destRect = new();
    private long _lastAnimNanos;  // 上一帧的时间戳（用于计算 delta time）

    // 复用的像素缓冲区（避免 BoxBlur 中每次分配 int[w*h] 大数组导致 LOS GC 风暴）
    private int[]? _blurBufA;
    private int[]? _blurBufB;

    /// <summary>递增加载版本号，并返回递增后的值</summary>
    public int IncrementLoadingVersion() => Interlocked.Increment(ref _loadingVersion);

    /// <summary>获取当前的加载版本号</summary>
    public int CurrentLoadingVersion => Volatile.Read(ref _loadingVersion);

    public FrostedBackgroundView(Context context) : base(context)
    {
        SetLayerType(LayerType.Hardware, null);
        Visibility = ViewStates.Visible;

        _driftAX = 0.12f + (float)_random.NextDouble() * 0.08f;
        _driftAY = 0.10f + (float)_random.NextDouble() * 0.07f;
        _driftBX = 0.08f + (float)_random.NextDouble() * 0.06f;
        _driftBY = 0.10f + (float)_random.NextDouble() * 0.07f;
        _driftSpeed = 0.12f + (float)_random.NextDouble() * 0.06f;
        _rotationSpeed = (2.0f + (float)_random.NextDouble() * 1.5f) * ((_random.Next(2) == 0) ? 1f : -1f);
        _breathSpeed = 0.15f + (float)_random.NextDouble() * 0.1f;
        _breathAmount = 0.04f + (float)_random.NextDouble() * 0.03f;

        _bitmapPaint = new Paint { AntiAlias = true, FilterBitmap = true };
        _tintPaint = new Paint { AntiAlias = true };
        _dimPaint = new Paint { AntiAlias = true };
    }

    /// <summary>更新封面源位图（在后台线程处理）。使用版本号让进行中的旧任务自动失效。</summary>
    /// <param name="bitmap">源位图</param>
    /// <param name="cacheKey">缓存键（如封面路径），用于跨实例共享处理后位图</param>
    public void SetSource(Bitmap? bitmap, string? cacheKey)
    {
        if (ReferenceEquals(_sourceBitmap, bitmap) && _cacheKey == cacheKey) return;
        var oldSource = _sourceBitmap;
        _sourceBitmap = bitmap;
        _cacheKey = cacheKey;
        // 递增处理版本号，让正在进行的旧任务完成后丢弃结果
        Interlocked.Increment(ref _processingVersion);
        // 捕获当前 bitmap 引用到闭包，防止后续 SetSource 回收后后台线程访问已回收位图
        var capturedSource = bitmap;
        var capturedCacheKey = cacheKey;
        _ = Task.Run(() => RegenerateProcessedBitmap(capturedSource, capturedCacheKey));

        // 回收旧源位图（每个实例独立加载，不共享）
        if (oldSource != null && !oldSource.IsRecycled)
        {
            oldSource.Recycle();
            oldSource.Dispose();
        }
    }

    private bool _isEnabled = true;  // 用户开关（控制背景是否显示）
    private bool _isPlaying = false; // 播放状态（控制动画是否运行）
    private volatile bool _isScrolling = false;  // 用户正在滑动列表（暂停动画以释放主线程）
    private bool _useNativeBlur = false;  // 原生 GPU 模糊（RenderEffect，静态磨砂，无流体动画）

    /// <summary>更新激活状态（仅控制动画，不隐藏背景）</summary>
    public void SetActive(bool active)
    {
        _isPlaying = active;
        UpdateAnimationState();
    }

    /// <summary>更新启用状态（控制背景是否显示）</summary>
    public void SetEnabled(bool enabled)
    {
        _isEnabled = enabled;
        Visibility = enabled ? ViewStates.Visible : ViewStates.Gone;
        UpdateAnimationState();
        Invalidate();
    }

    /// <summary>更新滑动状态：滑动时暂停流体动画以释放主线程/GPU 资源</summary>
    public void SetScrolling(bool scrolling)
    {
        if (_isScrolling == scrolling) return;
        _isScrolling = scrolling;
        UpdateAnimationState();
    }

    /// <summary>
    /// 切换原生 GPU 模糊（Android 12+ 的 RenderEffect）。
    /// 开启后：GPU 直接对绘制结果做高斯模糊，静态磨砂、无流体动画，省去 CPU 的 mesh/模糊/调色管线。
    /// 系统版本不足时自动回退到原有流光溢彩管线。
    /// </summary>
    public void SetUseNativeBlur(bool useNative)
    {
        var supported = useNative && Build.VERSION.SdkInt >= BuildVersionCodes.S;
        if (_useNativeBlur == supported) return;
        _useNativeBlur = supported;

        if (supported)
        {
            ApplyNativeBlur();
            StopAnimation();  // 静态磨砂无需流体动画
        }
        else
        {
            SetRenderEffect(null);
        }

        // 重新生成位图：原生模式走轻量路径（仅缩放），流体模式走原管线
        var captured = _sourceBitmap;
        if (captured != null && !captured.IsRecycled)
        {
            Interlocked.Increment(ref _processingVersion);
            var capturedKey = _cacheKey;
            _ = Task.Run(() => RegenerateProcessedBitmap(captured, capturedKey));
        }
        Invalidate();
    }

    /// <summary>应用原生 RenderEffect 高斯模糊（GPU 加速，模糊半径约 22dp）</summary>
    private void ApplyNativeBlur()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.S) return;
        try
        {
            CrashReporter.MarkStage("FrostedBackground.Android: ApplyNativeBlur 开始");
            var density = Resources?.DisplayMetrics?.Density ?? 2f;
            var radius = 22f * density;
            SetRenderEffect(RenderEffect.CreateBlurEffect(radius, radius, Shader.TileMode.Mirror)!);
            CrashReporter.MarkStage("FrostedBackground.Android: ApplyNativeBlur 完成");
        }
        catch (Exception ex)
        {
            // 任意 Android 版本/设备对 RenderEffect 的兼容性问题都不应导致崩溃，降级为不使用原生模糊
            Log.Debug("FrostedBackgroundHandler", $"[FrostedBackground] 原生模糊失败（已降级为流光溢彩管线）: {ex.Message}");
            try { SetRenderEffect(null); } catch { }
            _useNativeBlur = false;
        }
    }

    /// <summary>根据启用状态、播放状态、滑动状态更新动画运行状态</summary>
    private void UpdateAnimationState()
    {
        // 滑动时暂停动画（背景静态显示，不重绘），释放主线程给列表渲染
        bool shouldAnimate = _isEnabled && _isPlaying && !_isScrolling;
        if (shouldAnimate && _processedBitmap != null)
            StartAnimation();
        else
            StopAnimation();
    }

    /// <summary>更新填充模式</summary>
    public void SetAspect(Aspect aspect)
    {
        _aspect = aspect;
        Invalidate();
    }

    /// <summary>更新色调和暗化参数</summary>
    public void UpdateTint(Color tintColor, double tintOpacity, double dimAmount)
    {
        _tintColor = tintColor;
        _tintOpacity = Math.Clamp(tintOpacity, 0.0, 1.0);
        _dimAmount = Math.Clamp(dimAmount, 0.0, 1.0);
        Invalidate();
    }

    protected override void OnAttachedToWindow()
    {
        base.OnAttachedToWindow();
        if (_isEnabled && _isPlaying && _processedBitmap != null)
            StartAnimation();
    }

    protected override void OnDetachedFromWindow()
    {
        base.OnDetachedFromWindow();
        StopAnimation();
        StopCrossFadeAnimation();
    }

    /// <summary>
    /// 启动流体动画（参考 Apple Music 风格）：
    /// - 多层正弦波叠加的有机漂移运动
    /// - 缓慢旋转（约 0.4-0.8 度/秒）
    /// - 呼吸缩放脉动
    /// - 两层视差叠加（底层更大更慢，半透明）
    /// </summary>
    private void StartAnimation()
    {
        if (_useNativeBlur) return;  // 原生模式：静态磨砂，不跑流体动画
        StopAnimation();
        if (Width <= 0 || Height <= 0 || _processedBitmap == null) return;

        _lastAnimNanos = System.Diagnostics.Stopwatch.GetTimestamp();
        _animator = ValueAnimator.OfFloat(0f, 1f);
        _animator.SetDuration(125);  // ~8fps，雾面背景不需要高帧率，省电优先
        _animator.RepeatCount = ValueAnimator.Infinite;
        _animator.RepeatMode = ValueAnimatorRepeatMode.Restart;
        _animator.SetInterpolator(new global::Android.Views.Animations.LinearInterpolator());
        _animator.Update += (_, e) =>
        {
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            var deltaMs = (now - _lastAnimNanos) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            _lastAnimNanos = now;
            _animTime += (float)(deltaMs / 1000.0);
            Invalidate();
        };
        _animator.Start();
    }

    private void StopAnimation()
    {
        _animator?.Cancel();
        _animator?.Dispose();
        _animator = null;
    }

    protected override void OnDraw(Canvas? canvas)
    {
        base.OnDraw(canvas);
        if (canvas == null || _processedBitmap == null || _processedBitmap.IsRecycled) return;

        var w = Width;
        var h = Height;
        if (w <= 0 || h <= 0) return;

        _destRect.Set(0, 0, w, h);

        // 交叉淡入淡出过渡：progress=0 完全旧位图，progress=1 完全新位图
        var isCrossFading = _crossFadeProgress < 1f && _previousBitmap != null && !_previousBitmap.IsRecycled;

        if (isCrossFading)
        {
            var oldAlpha = (int)((1f - _crossFadeProgress) * 255);
            DrawFluidLayer(canvas, _previousBitmap, w, h, oldAlpha, 1f, 0f);
        }

        var newAlpha = isCrossFading ? (int)(_crossFadeProgress * 255) : 255;

        // 主视觉层（移除底层视差层以减少 GPU 开销，视觉效果变化很小）
        DrawFluidLayer(canvas, _processedBitmap, w, h, newAlpha, 1f, 0f);

        // 色调叠加层
        if (_tintOpacity > 0 && _tintColor != Colors.Transparent && _tintColor.Alpha > 0)
        {
            _tintPaint.Color = global::Android.Graphics.Color.Argb(
                (int)(_tintOpacity * _tintColor.Alpha * 255),
                (byte)(_tintColor.Red * 255),
                (byte)(_tintColor.Green * 255),
                (byte)(_tintColor.Blue * 255));
            canvas.DrawRect(0, 0, w, h, _tintPaint);
        }

        // 暗化叠加层
        if (_dimAmount > 0)
        {
            _dimPaint.Color = global::Android.Graphics.Color.Argb((int)(_dimAmount * 255), 0, 0, 0);
            canvas.DrawRect(0, 0, w, h, _dimPaint);
        }
    }

    /// <summary>
    /// 绘制流体动画层。使用多层正弦波叠加计算漂移，模拟 FBM 流体效果。
    /// </summary>
    /// <param name="scaleMul">缩放倍率（>1 表示放大更多，用于底层）</param>
    /// <param name="speedMul">速度倍率（负值反向旋转，用于底层视差）</param>
    private void DrawFluidLayer(Canvas canvas, Bitmap? bitmap, int w, int h, int alpha, float scaleMul, float speedMul)
    {
        if (bitmap == null || bitmap.IsRecycled) return;
        var bw = bitmap.Width;
        var bh = bitmap.Height;
        if (bw <= 0 || bh <= 0) return;

        float viewRatio = w / (float)h;
        float bmpRatio = bw / (float)bh;

        float baseScale;
        if (_aspect == Aspect.AspectFill)
            baseScale = Math.Max(w / (float)bw, h / (float)bh);
        else if (_aspect == Aspect.AspectFit)
            baseScale = Math.Min(w / (float)bw, h / (float)bh);
        else
            baseScale = 1.0f;

        // 基础缩放 + 呼吸缩放
        float breath = 1f + _breathAmount * (float)Math.Sin(_animTime * _breathSpeed * 2.0 * Math.PI);
        float scale = baseScale * 1.25f * scaleMul * breath;
        float srcW = w / scale;
        float srcH = h / scale;

        // 流体漂移：双层正弦波叠加（模拟 FBM 低频+中频）
        float t = _animTime * _driftSpeed;
        float driftX = (float)(
            _driftAX * Math.Sin(t * 0.7 + _driftBX) +
            _driftBX * Math.Sin(t * 1.8 + _driftAX));
        float driftY = (float)(
            _driftAY * Math.Cos(t * 0.6 + _driftBY) +
            _driftBY * Math.Cos(t * 1.6 + _driftAY));

        driftX *= speedMul != 0 ? (speedMul * 0.5f + 0.5f) : 1f;
        driftY *= speedMul != 0 ? (speedMul * 0.5f + 0.5f) : 1f;

        float maxX = Math.Max(0, bw - srcW);
        float maxY = Math.Max(0, bh - srcH);
        float cx = bw * 0.5f + driftX * bw;
        float cy = bh * 0.5f + driftY * bh;
        cx = Math.Clamp(cx, srcW * 0.5f, bw - srcW * 0.5f);
        cy = Math.Clamp(cy, srcH * 0.5f, bh - srcH * 0.5f);

        float srcLeft = cx - srcW * 0.5f;
        float srcTop = cy - srcH * 0.5f;

        // 旋转角度（度/秒 → 当前角度）
        float rotation = _rotationSpeed * _animTime * speedMul;

        canvas.Save();

        if (Math.Abs(rotation) > 0.01f)
        {
            canvas.Translate(w / 2f, h / 2f);
            canvas.Rotate(rotation);
            canvas.Translate(-w / 2f, -h / 2f);
        }

        _srcRect.Set(
            (int)Math.Clamp(srcLeft, 0, bw - 1),
            (int)Math.Clamp(srcTop, 0, bh - 1),
            (int)Math.Clamp(srcLeft + srcW, 1, bw),
            (int)Math.Clamp(srcTop + srcH, 1, bh));

        _bitmapPaint.Alpha = Math.Clamp(alpha, 0, 255);
        canvas.DrawBitmap(bitmap, _srcRect, _destRect, _bitmapPaint);
        _bitmapPaint.Alpha = 255;

        canvas.Restore();
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>
    /// 重新生成处理后的流光溢彩位图（在后台线程执行）。
    /// 使用版本号机制：每次 SetSource 递增版本号，处理完成后发现版本不匹配则丢弃结果，
    /// 避免旧的长时间处理覆盖新的快速切换结果（即"切换歌曲时背景不变色"问题）。
    /// 通过 ProcessedBitmapCache 让多个 FrostedBackground 实例共享同一处理结果，
    /// 避免 NowPlayingPage 和 FullLyricsPage 重复进行 CPU 密集的 mesh+模糊处理。
    /// 处理成功完成后启动交叉淡入淡出过渡动画。
    /// </summary>
    private void RegenerateProcessedBitmap(Bitmap? source, string? cacheKey)
    {
        var myVersion = Interlocked.Increment(ref _processingVersion);

        // 早期版本检查：如果已有更新的请求在排队，直接放弃（避免无用的 CPU 处理）
        if (myVersion != Volatile.Read(ref _processingVersion)) return;

        Bitmap? result = null;
        string? resultCacheKey = null;  // 非 null 表示来自缓存

        try
        {
            // 使用捕获的本地引用而非 _sourceBitmap 字段，避免竞态访问已被回收的位图
            if (source != null && !source.IsRecycled && source.Width > 0 && source.Height > 0)
            {
                if (_useNativeBlur)
                {
                    // 原生模式：模糊交给 GPU 的 RenderEffect，这里仅缩小尺寸降低开销
                    result = DownscaleForNativeBlur(source);
                }
                else if (!string.IsNullOrEmpty(cacheKey))
                {
                    // 通过缓存获取：若另一个实例已处理过同一封面，直接复用
                    result = ProcessedBitmapCache.GetOrCreate(cacheKey, source, ProcessFlowingLightBitmap);
                    resultCacheKey = cacheKey;
                }
                else
                {
                    // 无 cacheKey：直接处理（不缓存）
                    result = ProcessFlowingLightBitmap(source);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("FrostedBackgroundHandler", $"[FrostedBackground] Regenerate failed: {ex.Message}");
        }

        // 版本不匹配 → 释放引用
        if (myVersion != Volatile.Read(ref _processingVersion))
        {
            ReleaseProcessedBitmap(result, resultCacheKey);
            return;
        }

        // 主线程应用新位图并启动交叉淡入淡出
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (myVersion != Volatile.Read(ref _processingVersion))
            {
                ReleaseProcessedBitmap(result, resultCacheKey);
                return;
            }

            ApplyNewProcessedBitmap(result, resultCacheKey);
        });
    }

    /// <summary>在主线程应用新生成的位图，启动交叉淡入淡出过渡</summary>
    /// <param name="newBitmap">新处理后的位图</param>
    /// <param name="cacheKey">缓存键（null 表示非缓存，直接回收）</param>
    private void ApplyNewProcessedBitmap(Bitmap? newBitmap, string? cacheKey)
    {
        // 若正在过渡中，先停止过渡：保留当前 _processedBitmap（上次的新位图）作为本次过渡的源，
        // 释放 _previousBitmap（更旧的位图，已经不需要了）
        if (_crossFadeAnimator != null)
        {
            StopCrossFadeAnimation();  // 会释放 _previousBitmap 并设置 _crossFadeProgress=1
        }

        var oldBitmap = _processedBitmap;
        var oldCacheKey = _processedCacheKey;

        // 没有旧位图（首次显示）或新旧相同 → 直接设置，无需过渡
        if (oldBitmap == null || ReferenceEquals(oldBitmap, newBitmap))
        {
            _processedBitmap = newBitmap;
            _processedCacheKey = cacheKey;
            _crossFadeProgress = 1f;
            // 释放旧位图引用
            ReleaseProcessedBitmap(oldBitmap, oldCacheKey);
            PostInvalidate();
            if (newBitmap != null && _isEnabled && _isPlaying && _animator == null)
                StartAnimation();
            return;
        }

        // 启动交叉淡入淡出过渡
        _processedBitmap = newBitmap;
        _processedCacheKey = cacheKey;
        _previousBitmap = oldBitmap;  // 保留旧位图用于过渡
        _previousCacheKey = oldCacheKey;
        _crossFadeProgress = 0f;
        StartCrossFadeAnimation();

        // 过渡期间确保动画运行（使用新位图）
        if (_isEnabled && _isPlaying && _animator == null)
            StartAnimation();
    }

    /// <summary>释放已处理的位图（cacheKey 非空则缓存 Release，否则直接回收）</summary>
    private static void ReleaseProcessedBitmap(Bitmap? bitmap, string? cacheKey)
    {
        if (bitmap == null) return;
        if (cacheKey != null) ProcessedBitmapCache.Release(cacheKey, bitmap);
        else if (!bitmap.IsRecycled) { bitmap.Recycle(); bitmap.Dispose(); }
    }

    /// <summary>启动交叉淡入淡出动画（600ms 渐变过渡）</summary>
    private void StartCrossFadeAnimation()
    {
        // 仅取消已有动画（不释放 _previousBitmap，由新动画继续使用）
        if (_crossFadeAnimator != null)
        {
            _crossFadeAnimator.Cancel();
            _crossFadeAnimator.Dispose();
            _crossFadeAnimator = null;
        }

        _crossFadeAnimator = ValueAnimator.OfFloat(0f, 1f);
        _crossFadeAnimator.SetDuration(600);
        _crossFadeAnimator.SetInterpolator(new global::Android.Views.Animations.AccelerateDecelerateInterpolator());
        _crossFadeAnimator.Update += (_, e) =>
        {
            _crossFadeProgress = (float)e.Animation.AnimatedValue;
            Invalidate();
        };
        _crossFadeAnimator.AnimationEnd += (_, _) =>
        {
            // 过渡完成：释放旧位图（根据来源选择释放方式）
            ReleaseProcessedBitmap(_previousBitmap, _previousCacheKey);
            _previousBitmap = null;
            _previousCacheKey = null;
            _crossFadeProgress = 1f;
            Invalidate();
        };
        _crossFadeAnimator.Start();
    }

    /// <summary>停止交叉淡入淡出动画（立即完成，释放旧位图）</summary>
    private void StopCrossFadeAnimation()
    {
        if (_crossFadeAnimator != null)
        {
            _crossFadeAnimator.Cancel();
            _crossFadeAnimator.Dispose();
            _crossFadeAnimator = null;
        }
        // 立即释放旧位图（根据来源选择释放方式）
        ReleaseProcessedBitmap(_previousBitmap, _previousCacheKey);
        _previousBitmap = null;
        _previousCacheKey = null;
        _crossFadeProgress = 1f;
    }

    /// <summary>
    /// 流光溢彩位图处理（参考 Apple Music 实现）：
    /// 1. 将图片缩小到 120 长边（让高斯模糊影响更大区域）
    /// 2. 高斯模糊（半径 20，迭代 3 次）
    /// 3. mesh 变换（去除重复感和撕裂感）
    /// 4. 色调处理（饱和度 1.8，亮度提升 1.15，让取色更亮丽）
    /// 5. 放大到 800 长边
    /// 6. mesh 变换（第二次，增加不规则性）
    /// 7. 高斯模糊（半径 8，迭代 2 次，去除放大波纹）
    /// 8. 二次色调增强（饱和度 1.5，保持鲜艳）
    /// </summary>
    private Bitmap ProcessFlowingLightBitmap(Bitmap src)
    {
        // 步骤1：缩小到 120 长边（更小=模糊影响范围更大，雾化效果更强）
        int smallSize = 120;
        int srcW = src.Width;
        int srcH = src.Height;
        float ratio = srcW / (float)srcH;
        int smallW, smallH;
        if (ratio >= 1)
        {
            smallW = smallSize;
            smallH = (int)(smallSize / ratio);
        }
        else
        {
            smallH = smallSize;
            smallW = (int)(smallSize * ratio);
        }
        smallW = Math.Max(1, smallW);
        smallH = Math.Max(1, smallH);

        var small = Zoom(src, smallW, smallH);

        // 步骤2：高斯模糊（半径 20，迭代 3 次，增强雾化效果）
        var blurred1 = BoxBlur(small, Math.Max(3, smallW / 6));
        if (blurred1 != small) small.Recycle();

        // 步骤3：第一次 mesh 变换（去除重复感和撕裂感）
        var meshed1 = ApplyMesh(blurred1, AppleMusicMesh1);
        blurred1.Recycle();

        // 步骤4：色调处理（饱和度 1.8 + 亮度 1.15，让取色更亮丽鲜艳）
        var toned1 = AdjustTone(meshed1, 1.8f, 1.15f);
        meshed1.Recycle();

        // 步骤5：放大到 800 长边
        int largeSize = 800;
        int largeW, largeH;
        if (ratio >= 1)
        {
            largeW = largeSize;
            largeH = (int)(largeSize / ratio);
        }
        else
        {
            largeH = largeSize;
            largeW = (int)(largeSize * ratio);
        }
        var large = Zoom(toned1, largeW, largeH);
        toned1.Recycle();

        // 步骤6：第二次 mesh 变换（增加不规则性，消除放大后的网格感）
        var meshed2 = ApplyMesh(large, AppleMusicMesh2);
        large.Recycle();

        // 步骤7：高斯模糊（半径 8，迭代 2 次，去除放大波纹和锯齿）
        var blurred2 = BoxBlurIterations(meshed2, Math.Max(2, largeW / 100), 2);
        if (blurred2 != meshed2) meshed2.Recycle();

        // 步骤8：二次色调增强（饱和度 1.5，亮度 1.08，保持鲜艳明亮）
        var final = AdjustTone(blurred2, 1.5f, 1.08f);
        blurred2.Recycle();

        return final;
    }

    /// <summary>缩放位图到指定尺寸</summary>
    private Bitmap Zoom(Bitmap src, int newWidth, int newHeight)
    {
        newWidth = Math.Max(1, newWidth);
        newHeight = Math.Max(1, newHeight);
        var matrix = new Matrix();
        float scaleWidth = newWidth / (float)src.Width;
        float scaleHeight = newHeight / (float)src.Height;
        matrix.PostScale(scaleWidth, scaleHeight);
        var result = Bitmap.CreateBitmap(src, 0, 0, src.Width, src.Height, matrix, true)!;
        matrix.Dispose();
        return result;
    }

    /// <summary>
    /// 原生模糊模式的位图准备：仅缩小到长边 400（降低 GPU 模糊与绘制成本），
    /// 不做 CPU 模糊/mesh/调色——真正的模糊由 View 上的 RenderEffect 在 GPU 完成。
    /// 返回新位图（可安全回收），不复用源位图。
    /// </summary>
    private Bitmap DownscaleForNativeBlur(Bitmap src)
    {
        const int maxEdge = 400;
        int w = src.Width, h = src.Height;
        if (w <= maxEdge && h <= maxEdge)
            return src.Copy(src.GetConfig() ?? Bitmap.Config.Argb8888, false)!;
        float scale = maxEdge / (float)Math.Max(w, h);
        return Zoom(src, (int)(w * scale), (int)(h * scale));
    }

    /// <summary>
    /// 使用 Canvas.drawBitmapMesh 进行 mesh 变换。
    /// 5x5 网格（6x6 顶点），参数为归一化坐标（0-1），乘以宽高得到实际坐标。
    /// </summary>
    private Bitmap ApplyMesh(Bitmap src, float[] normalizedMesh)
    {
        // 将归一化坐标转换为实际坐标
        var verts = new float[72];
        for (int i = 0; i <= 5; i++)
        {
            for (int j = 0; j <= 5; j++)
            {
                int idx = i * 12 + j * 2;
                verts[idx] = normalizedMesh[idx] * src.Width;
                verts[idx + 1] = normalizedMesh[idx + 1] * src.Height;
            }
        }

        var config = src.GetConfig() ?? Bitmap.Config.Argb8888;
        var output = Bitmap.CreateBitmap(src.Width, src.Height, config)!;
        using var canvas = new Canvas(output);
        canvas.DrawBitmapMesh(src, 5, 5, verts, 0, null, 0, null);
        return output;
    }

    /// <summary>
    /// 分离式箱式模糊（水平+垂直两次遍历，迭代 3 次近似高斯）。
    /// </summary>
    private Bitmap BoxBlur(Bitmap src, int radius)
    {
        return BoxBlurIterations(src, radius, 3);
    }

    /// <summary>
    /// 分离式箱式模糊（指定迭代次数）。
    /// </summary>
    private Bitmap BoxBlurIterations(Bitmap src, int radius, int iterations)
    {
        if (radius <= 0 || iterations <= 0) return src;

        var config = src.GetConfig() ?? Bitmap.Config.Argb8888;
        var current = src.Copy(config, true)!;

        for (int i = 0; i < iterations; i++)
        {
            var horizontal = BoxBlurHorizontal(current, radius);
            if (horizontal != current) { current.Recycle(); }
            current = horizontal;

            var both = BoxBlurVertical(current, radius);
            if (both != current) { current.Recycle(); }
            current = both;
        }

        return current;
    }

    /// <summary>获取复用的像素缓冲区，尺寸不匹配时重新分配</summary>
    private int[] GetBlurBuffer(ref int[]? buf, int w, int h)
    {
        int len = w * h;
        if (buf == null || buf.Length < len)
            buf = new int[len];
        return buf;
    }

    /// <summary>水平方向箱式模糊</summary>
    private unsafe Bitmap BoxBlurHorizontal(Bitmap src, int radius)
    {
        int w = src.Width;
        int h = src.Height;
        var config = src.GetConfig() ?? Bitmap.Config.Argb8888;
        var output = Bitmap.CreateBitmap(w, h, config)!;

        int[] pixels = GetBlurBuffer(ref _blurBufA, w, h);
        src.GetPixels(pixels, 0, w, 0, 0, w, h);
        int[] result = GetBlurBuffer(ref _blurBufB, w, h);

        int windowSize = radius * 2 + 1;

        fixed (int* pSrc = pixels, pDst = result)
        {
            for (int y = 0; y < h; y++)
            {
                int sumR = 0, sumG = 0, sumB = 0, sumA = 0;
                int rowOffset = y * w;

                for (int x = -radius; x <= radius; x++)
                {
                    int px = Math.Clamp(x, 0, w - 1);
                    int c = pSrc[rowOffset + px];
                    sumA += (c >> 24) & 0xFF;
                    sumR += (c >> 16) & 0xFF;
                    sumG += (c >> 8) & 0xFF;
                    sumB += c & 0xFF;
                }

                for (int x = 0; x < w; x++)
                {
                    pDst[rowOffset + x] = ((sumA / windowSize) << 24)
                        | ((sumR / windowSize) << 16)
                        | ((sumG / windowSize) << 8)
                        | (sumB / windowSize);

                    int leftX = Math.Clamp(x - radius, 0, w - 1);
                    int rightX = Math.Clamp(x + radius + 1, 0, w - 1);
                    int leftC = pSrc[rowOffset + leftX];
                    int rightC = pSrc[rowOffset + rightX];
                    sumA += ((rightC >> 24) & 0xFF) - ((leftC >> 24) & 0xFF);
                    sumR += ((rightC >> 16) & 0xFF) - ((leftC >> 16) & 0xFF);
                    sumG += ((rightC >> 8) & 0xFF) - ((leftC >> 8) & 0xFF);
                    sumB += (rightC & 0xFF) - (leftC & 0xFF);
                }
            }
        }

        output.SetPixels(result, 0, w, 0, 0, w, h);
        return output;
    }

    /// <summary>垂直方向箱式模糊</summary>
    private unsafe Bitmap BoxBlurVertical(Bitmap src, int radius)
    {
        int w = src.Width;
        int h = src.Height;
        var config = src.GetConfig() ?? Bitmap.Config.Argb8888;
        var output = Bitmap.CreateBitmap(w, h, config)!;

        int[] pixels = GetBlurBuffer(ref _blurBufA, w, h);
        src.GetPixels(pixels, 0, w, 0, 0, w, h);
        int[] result = GetBlurBuffer(ref _blurBufB, w, h);

        int windowSize = radius * 2 + 1;

        fixed (int* pSrc = pixels, pDst = result)
        {
            for (int x = 0; x < w; x++)
            {
                int sumR = 0, sumG = 0, sumB = 0, sumA = 0;

                for (int y = -radius; y <= radius; y++)
                {
                    int py = Math.Clamp(y, 0, h - 1);
                    int c = pSrc[py * w + x];
                    sumA += (c >> 24) & 0xFF;
                    sumR += (c >> 16) & 0xFF;
                    sumG += (c >> 8) & 0xFF;
                    sumB += c & 0xFF;
                }

                for (int y = 0; y < h; y++)
                {
                    pDst[y * w + x] = ((sumA / windowSize) << 24)
                        | ((sumR / windowSize) << 16)
                        | ((sumG / windowSize) << 8)
                        | (sumB / windowSize);

                    int topY = Math.Clamp(y - radius, 0, h - 1);
                    int botY = Math.Clamp(y + radius + 1, 0, h - 1);
                    int topC = pSrc[topY * w + x];
                    int botC = pSrc[botY * w + x];
                    sumA += ((botC >> 24) & 0xFF) - ((topC >> 24) & 0xFF);
                    sumR += ((botC >> 16) & 0xFF) - ((topC >> 16) & 0xFF);
                    sumG += ((botC >> 8) & 0xFF) - ((topC >> 8) & 0xFF);
                    sumB += (botC & 0xFF) - (topC & 0xFF);
                }
            }
        }

        output.SetPixels(result, 0, w, 0, 0, w, h);
        return output;
    }

    /// <summary>使用 ColorMatrix 调整色调（饱和度 + 亮度）</summary>
    private Bitmap AdjustTone(Bitmap src, float saturation, float brightness)
    {
        var satMatrix = new ColorMatrix();
        satMatrix.SetSaturation(saturation);

        var brightMatrix = new ColorMatrix(new float[]
        {
            brightness, 0, 0, 0, 0,
            0, brightness, 0, 0, 0,
            0, 0, brightness, 0, 0,
            0, 0, 0, 1, 0
        });

        var combined = new ColorMatrix();
        combined.SetConcat(satMatrix, brightMatrix);

        var config = src.GetConfig() ?? Bitmap.Config.Argb8888;
        var output = Bitmap.CreateBitmap(src.Width, src.Height, config)!;
        using var canvas = new Canvas(output);
        using var paint = new Paint { AntiAlias = true, FilterBitmap = true };
        paint.SetColorFilter(new ColorMatrixColorFilter(combined));
        canvas.DrawBitmap(src, 0, 0, paint);
        return output;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopAnimation();
            StopCrossFadeAnimation();
            _bitmapPaint?.Dispose();
            _tintPaint?.Dispose();
            _dimPaint?.Dispose();
            // 释放当前位图（根据来源选择释放方式）
            ReleaseProcessedBitmap(_processedBitmap, _processedCacheKey);
            ReleaseProcessedBitmap(_previousBitmap, _previousCacheKey);
            _processedBitmap = null;
            _processedCacheKey = null;
            _previousBitmap = null;
            _previousCacheKey = null;
            // 源位图由 UpdateSourceFromImageSource 管理（每个实例独立加载）
            // 不在这里回收，仅置空引用
            _sourceBitmap = null;
            _blurBufA = null;
            _blurBufB = null;
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// 处理后位图的全局缓存：NowPlayingPage 和 FullLyricsPage 两个 FrostedBackground 实例
/// 绑定到同一个 CoverImage，避免两个实例分别进行 CPU 密集的 mesh+模糊处理。
/// 以封面路径/ID（string）为 key 缓存处理结果，使用引用计数管理生命周期。
/// </summary>
internal static class ProcessedBitmapCache
{
    private sealed class Entry
    {
        public Bitmap Bitmap;
        public int RefCount;
        public Entry(Bitmap bmp) { Bitmap = bmp; RefCount = 1; }
    }

    private static readonly Dictionary<string, Entry> _cache = new();
    private static readonly object _lock = new();

    /// <summary>获取或创建处理后的位图。若缓存命中则增加引用计数并返回缓存实例。</summary>
    /// <param name="cacheKey">缓存键（如封面文件路径）</param>
    /// <param name="source">源位图（缓存未命中时用于处理）</param>
    /// <param name="process">处理函数（缓存未命中时调用）</param>
    /// <returns>处理后的位图（调用方不负责释放，由 Release 释放）</returns>
    public static Bitmap GetOrCreate(string cacheKey, Bitmap source, Func<Bitmap, Bitmap> process)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(cacheKey, out var existing) && !existing.Bitmap.IsRecycled)
            {
                existing.RefCount++;
                return existing.Bitmap;
            }
        }

        // 处理在锁外执行（CPU 密集）
        var result = process(source);

        lock (_lock)
        {
            // 处理期间可能已有其他线程加入了缓存，再次检查
            if (_cache.TryGetValue(cacheKey, out var existing) && !existing.Bitmap.IsRecycled)
            {
                // 丢弃本次处理结果，复用缓存
                if (!result.IsRecycled) { result.Recycle(); result.Dispose(); }
                existing.RefCount++;
                return existing.Bitmap;
            }
            _cache[cacheKey] = new Entry(result);
            return result;
        }
    }

    /// <summary>释放引用计数，归零时回收位图并从缓存移除。</summary>
    public static void Release(string? cacheKey, Bitmap bitmap)
    {
        if (bitmap == null || cacheKey == null) return;
        lock (_lock)
        {
            if (!_cache.TryGetValue(cacheKey, out var entry)) return;
            entry.RefCount--;
            if (entry.RefCount <= 0)
            {
                if (!entry.Bitmap.IsRecycled) { entry.Bitmap.Recycle(); entry.Bitmap.Dispose(); }
                _cache.Remove(cacheKey);
            }
        }
    }

    /// <summary>清空所有缓存（如低内存时调用）</summary>
    public static void ClearAll()
    {
        lock (_lock)
        {
            foreach (var entry in _cache.Values)
            {
                if (!entry.Bitmap.IsRecycled) { entry.Bitmap.Recycle(); entry.Bitmap.Dispose(); }
            }
            _cache.Clear();
        }
    }
}

/// <summary>
/// FrostedBackground 的 MAUI Handler，将虚拟控件映射到 FrostedBackgroundView。
/// </summary>
public class FrostedBackgroundHandler : ViewHandler<FrostedBackground, FrostedBackgroundView>
{
    public static readonly IPropertyMapper<FrostedBackground, FrostedBackgroundHandler> PropertyMapper =
        new PropertyMapper<FrostedBackground, FrostedBackgroundHandler>(ViewHandler.ViewMapper)
        {
            [nameof(FrostedBackground.Source)] = MapSource,
            [nameof(FrostedBackground.IsActive)] = MapIsActive,
            [nameof(FrostedBackground.TintColor)] = MapTint,
            [nameof(FrostedBackground.TintOpacity)] = MapTint,
            [nameof(FrostedBackground.DimAmount)] = MapTint,
            [nameof(FrostedBackground.Aspect)] = MapAspect,
            [nameof(FrostedBackground.IsScrolling)] = MapIsScrolling,
            [nameof(FrostedBackground.UseNativeBlur)] = MapUseNativeBlur,
            [nameof(FrostedBackground.IsVisible)] = MapIsVisible,
            // IsVisible 的变更通过 IView.Visibility 键下发，需一并覆盖，否则开关切换不生效
            [nameof(Microsoft.Maui.IView.Visibility)] = MapIsVisible,
        };

    public FrostedBackgroundHandler() : base(PropertyMapper) { }

    protected override FrostedBackgroundView CreatePlatformView() => new(Context!);

    private static void MapSource(FrostedBackgroundHandler handler, FrostedBackground view)
    {
        handler.PlatformView.UpdateSourceFromImageSource(view.Source, view.CacheKey);
    }

    private static void MapIsActive(FrostedBackgroundHandler handler, FrostedBackground view)
    {
        handler.PlatformView.SetActive(view.IsActive);
    }

    private static void MapTint(FrostedBackgroundHandler handler, FrostedBackground view)
    {
        handler.PlatformView.UpdateTint(view.TintColor, view.TintOpacity, view.DimAmount);
    }

    private static void MapAspect(FrostedBackgroundHandler handler, FrostedBackground view)
    {
        handler.PlatformView.SetAspect(view.Aspect);
    }

    private static void MapIsScrolling(FrostedBackgroundHandler handler, FrostedBackground view)
    {
        handler.PlatformView.SetScrolling(view.IsScrolling);
    }

    private static void MapUseNativeBlur(FrostedBackgroundHandler handler, FrostedBackground view)
    {
        handler.PlatformView.SetUseNativeBlur(view.UseNativeBlur);
    }

    private static void MapIsVisible(FrostedBackgroundHandler handler, FrostedBackground view)
    {
        // 平台视图构造函数强制 Visibility=Visible，需显式同步 IsVisible，否则关闭雾面背景开关后仍显示
        handler.PlatformView.SetEnabled(view.IsVisible);
    }
}

/// <summary>ImageSource 转换为 Android Bitmap 的辅助扩展</summary>
internal static class FrostedBackgroundSourceExtensions
{
    /// <summary>
    /// 从 ImageSource 加载 Bitmap，使用平台视图自身的版本号避免并发加载导致的取消。
    /// 同时对超大图片进行下采样。
    /// </summary>
    /// <param name="platformView">目标视图</param>
    /// <param name="source">图片源</param>
    /// <param name="cacheKey">缓存键（如封面路径），用于跨实例共享处理后位图</param>
    public static async void UpdateSourceFromImageSource(this FrostedBackgroundView platformView, ImageSource? source, string? cacheKey)
    {
        if (source == null)
        {
            platformView.IncrementLoadingVersion();
            platformView.SetSource(null, null);
            return;
        }

        var version = platformView.IncrementLoadingVersion();

        try
        {
            Bitmap? bitmap = null;

            // 优先处理 FileImageSource：直接从文件解码，避免 StreamImageSource 的内部取消机制
            if (source is FileImageSource fileSource)
            {
                var path = fileSource.File;
                if (!string.IsNullOrEmpty(path))
                {
                    bitmap = await Task.Run(() =>
                    {
                        try
                        {
                            if (!File.Exists(path)) return null;
                            var options = new BitmapFactory.Options { InJustDecodeBounds = true };
                            BitmapFactory.DecodeFile(path, options);

                            int maxEdge = 512;
                            int sampleSize = 1;
                            while ((options.OutWidth / sampleSize) > maxEdge || (options.OutHeight / sampleSize) > maxEdge)
                            {
                                sampleSize *= 2;
                            }

                            options.InJustDecodeBounds = false;
                            options.InSampleSize = sampleSize;
                            options.InPreferredConfig = Bitmap.Config.Argb8888;
                            return BitmapFactory.DecodeFile(path, options);
                        }
                        catch (Exception ex)
                        {
                            Log.Debug("FrostedBackgroundHandler", $"[FrostedBackground] 解码位图失败: {ex.Message}");
                            return null;
                        }
                    });
                }
            }
            else if (source is IStreamImageSource streamSource)
            {
                byte[]? bytes = null;
                using (var stream = await streamSource.GetStreamAsync(System.Threading.CancellationToken.None))
                {
                    if (stream != null)
                    {
                        using var ms = new MemoryStream();
                        await stream.CopyToAsync(ms);
                        bytes = ms.ToArray();
                    }
                }

                if (version != platformView.CurrentLoadingVersion || bytes == null || bytes.Length == 0)
                    return;

                bitmap = await Task.Run(() =>
                {
                    try
                    {
                        var options = new BitmapFactory.Options { InJustDecodeBounds = true };
                        BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, options);

                        int maxEdge = 512;
                        int sampleSize = 1;
                        while ((options.OutWidth / sampleSize) > maxEdge || (options.OutHeight / sampleSize) > maxEdge)
                        {
                            sampleSize *= 2;
                        }

                        options.InJustDecodeBounds = false;
                        options.InSampleSize = sampleSize;
                        options.InPreferredConfig = Bitmap.Config.Argb8888;
                        return BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, options);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("FrostedBackgroundHandler", $"[FrostedBackground] 解码位图失败: {ex.Message}");
                        return null;
                    }
                });
            }

            if (version != platformView.CurrentLoadingVersion)
            {
                bitmap?.Recycle();
                bitmap?.Dispose();
                return;
            }

            platformView.SetSource(bitmap, cacheKey);
        }
        catch (Exception ex)
        {
            Log.Debug("FrostedBackgroundHandler", $"[FrostedBackground] 加载封面失败: {ex.Message}");
        }
    }
}
