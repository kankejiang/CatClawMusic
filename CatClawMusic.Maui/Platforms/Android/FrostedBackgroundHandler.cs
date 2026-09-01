using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Animation;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Services.Frosted;
using Microsoft.Maui.Handlers;
using Color = Microsoft.Maui.Graphics.Color;
using Colors = Microsoft.Maui.Graphics.Colors;
using Paint = Android.Graphics.Paint;
using Canvas = Android.Graphics.Canvas;
using Bitmap = Android.Graphics.Bitmap;
using RectF = Android.Graphics.RectF;
using ValueAnimatorRepeatMode = Android.Animation.ValueAnimatorRepeatMode;

namespace CatClawMusic.Maui.Platforms.Android;

/// <summary>
/// 流光喷发背景（Halcyon / Apple Music 风格，程序化生成，不读封面）。
/// 用共享 FrostedFlowProcessor 在低分辨率缓冲上逐帧渲染同一套数学，再放大铺满视图；
/// 配合 ValueAnimator 驱动光点漂移与颜色阶段过渡。所有像素计算走 CPU 后台小分辨率，
/// 主线程仅负责把缓冲画到画布并叠加色调/暗化层。
/// </summary>
public class FrostedBackgroundView : global::Android.Views.View
{
    private bool _isEnabled = true;      // 用户开关（可见性）
    private bool _isPlaying = false;     // 播放状态（是否跑动画）
    private volatile bool _isScrolling = false;
    private bool _isDark = false;
    private Color _tintColor = Colors.Transparent;
    private double _tintOpacity = 0.35;
    private double _dimAmount = 0.35;
    private bool _disposed;

    private FrostedFlowAnimator? _animator;
    private ValueAnimator? _valueAnimator;
    private long _lastTickNanos;

    // 运行时帧缓冲（低分辨率，切尺寸时重建）
    private Bitmap? _flowBitmap;
    private float[]? _colors = new float[16];
    private int _flowW;
    private int _flowH;
    private FrostedFlowPreset? _preset;
    private CoverFlowProcessor.CoverSource _coverSrc = default;   // 有封面时背景切为封面流
    private long _renderTimeMs;                                     // 封面流动画时钟（单调递增）

    // ⚠ 后台渲染双缓冲：CoverFlowProcessor/FrostedFlowProcessor 是纯计算（BoxBlur、旋转平铺、
    // 像素循环），丢到 Task.Run 后台线程，主线程只做一次 SetPixels + invalidate。
    // 用 _renderBusy/_renderPending 标志串行化：后台算完主线程上屏 → 上屏完才允许下一帧计算。
    private readonly object _renderGate = new();
    private volatile bool _renderBusy;         // 后台正在算（禁止并发起帧）
    private bool _renderPending;               // 后台已算完、等待主线程上屏
    private int[]? _pendingPixels;             // 后台算好的这一帧像素
    private int _pendingW, _pendingH;          // 该帧实际尺寸（overscan 取整后可能 ≠ _flowW/_flowH）
    private long _pendingRenderSnapshot;       // 后台渲染时的时钟快照
    private int _pendingDpi;                   // 后台渲染时的 dpi 快照
    private CoverFlowProcessor.CoverSource _pendingCover; // 后台渲染时的封面快照
    private bool _pendingIsDark;               // 后台渲染时的主题快照
    private int _pendingAccent;                // 后台渲染时的强调色快照

    // ⚠ 进程级共享：封面流渲染时钟 + 最后一帧。播放页/全屏歌词页共用同一时钟与最后帧：
    // 切页时动画相位无缝衔接（Halcyon sharedClock 同思路），新页面首帧直接复用播放页画面，
    // 不闪黑、不重复渲染首两帧。
    private static long s_sharedRenderMs;
    private static readonly object s_sharedFrameGate = new();
    private static int[]? s_sharedFramePixels;
    private static int s_sharedFrameW, s_sharedFrameH;
    private static CoverFlowProcessor.CoverSource s_sharedFrameKey;
    private static bool s_sharedFrameDark;

    private readonly Paint _bitmapPaint;
    private readonly Paint _tintPaint;
    private readonly Paint _dimPaint;
    private readonly RectF _destRect = new();

    // 流光区：占画面下方 78%（与 Halcyon drawHeight/size.height 一致）
    private const float FlowRatio = 0.78f;
    // 有封面色时，流光作为盖在封面色底上的半透明叠层（贴合 Halcyon：封面色是底色）
    private const float FlowOverCoverAlpha = 0.55f;
    // 内部缓冲长边上限：CPU 逐帧渲染，分辨率够低才能兼顾流畅与观感。
    // 封面流本质是放大模糊图，256 与 360 视觉几乎无差，但渲染成本省约一半（BoxBlur O(n×radius)）。
    private const int MaxInternalEdge = 256;

    public FrostedBackgroundView(Context context) : base(context)
    {
        SetLayerType(LayerType.Hardware, null);
        Visibility = ViewStates.Visible;
        _bitmapPaint = new Paint { AntiAlias = true, FilterBitmap = true };
        _tintPaint = new Paint { AntiAlias = true };
        _dimPaint = new Paint { AntiAlias = true };
    }

    /// <summary>更新激活状态（仅控制动画，非播放不动画但保留最后一帧）</summary>
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

    /// <summary>滑动时暂停动画，释放主线程/CPU</summary>
    public void SetScrolling(bool scrolling)
    {
        if (_isScrolling == scrolling) return;
        _isScrolling = scrolling;
        UpdateAnimationState();
    }

    /// <summary>切换深浅流光预设</summary>
    public void SetDark(bool isDark)
    {
        if (_isDark == isDark) return;
        _isDark = isDark;
        UpdatePreset();
        Invalidate();   // 立即重绘：封面流洗色/底色/scrim 颜色都随深浅切换
    }

    private void UpdatePreset()
    {
        _preset = FrostedFlowPreset.Choose(_isDark);
        _animator = new FrostedFlowAnimator(_preset.ColorInterpPeriod);
    }

    /// <summary>更新色调与暗化</summary>
    public void UpdateTint(Color tintColor, double tintOpacity, double dimAmount)
    {
        _tintColor = tintColor;
        _tintOpacity = Math.Clamp(tintOpacity, 0.0, 1.0);
        _dimAmount = Math.Clamp(dimAmount, 0.0, 1.0);
        Invalidate();
    }

    private bool ShouldAnimate => _isEnabled && _isPlaying && !_isScrolling;

    /// <summary>把 MAUI 颜色按明度系数缩放为不透明 ARGB（Halcyon darken 阶梯用）。</summary>
    private static int Argb(Color c, float factor)
        => global::Android.Graphics.Color.Argb(255,
            (byte)Math.Clamp(c.Red * factor * 255, 0, 255),
            (byte)Math.Clamp(c.Green * factor * 255, 0, 255),
            (byte)Math.Clamp(c.Blue * factor * 255, 0, 255));

    private void UpdateAnimationState()
    {
        if (ShouldAnimate)
            StartAnimation();
        else
            StopAnimation();
    }

    protected override void OnAttachedToWindow()
    {
        base.OnAttachedToWindow();
        // 重新挂载（页面重建/切回）：优先复用进程共享的最后一帧，避免首帧黑场/纯色等待
        TryRestoreSharedFrame();
        if (ShouldAnimate) StartAnimation();
    }

    /// <summary>复用进程共享的最后一帧：同封面源、同尺寸、同深浅色时直接把共享帧设为当前位图。
    /// 全屏歌词页打开 / 播放页切回时，首帧即与另一页面当前画面一致，无闪黑、无重新渲染等待。</summary>
    private void TryRestoreSharedFrame()
    {
        if (_disposed || !CoverFlowMode || _flowBitmap != null) return;
        lock (s_sharedFrameGate)
        {
            if (s_sharedFramePixels == null || s_sharedFrameW <= 0 || s_sharedFrameH <= 0) return;
            if (!ReferenceEquals(s_sharedFrameKey.Argb, _coverSrc.Argb)
                || s_sharedFrameDark != _isDark)
                return; // 封面源/主题已变：共享帧过期，等后台正常渲染
            if (s_sharedFrameW != _flowW || s_sharedFrameH != _flowH)
            {
                _flowW = s_sharedFrameW;
                _flowH = s_sharedFrameH;
            }
            var bmp = Bitmap.CreateBitmap(s_sharedFrameW, s_sharedFrameH, Bitmap.Config.Argb8888)!;
            bmp.SetPixels(s_sharedFramePixels, 0, s_sharedFrameW, 0, 0, s_sharedFrameW, s_sharedFrameH);
            _flowBitmap?.Recycle();
            _flowBitmap = bmp;
        }
        Invalidate();
    }

    protected override void OnDetachedFromWindow()
    {
        base.OnDetachedFromWindow();
        StopAnimation();
    }

    private void StartAnimation()
    {
        if (_animator == null) UpdatePreset();
        if (_valueAnimator != null) return;
        _lastTickNanos = System.Diagnostics.Stopwatch.GetTimestamp();

        _valueAnimator = ValueAnimator.OfFloat(0f, 1f);
        _valueAnimator.SetDuration(125);   // ~8fps，雾面背景低帧率省电
        _valueAnimator.RepeatCount = ValueAnimator.Infinite;
        _valueAnimator.RepeatMode = ValueAnimatorRepeatMode.Restart;
        _valueAnimator.SetInterpolator(new global::Android.Views.Animations.LinearInterpolator());
        _valueAnimator.Update += OnAnimationTick;
        _valueAnimator.Start();
    }

    private void StopAnimation()
    {
        _valueAnimator?.Cancel();
        _valueAnimator?.Dispose();
        _valueAnimator = null;
    }

    // 动画节拍：主线程版本，只推进时钟 + 派发后台渲染任务
    private void OnAnimationTick(object? sender, ValueAnimator.AnimatorUpdateEventArgs e)
    {
        if (_disposed) return;
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var deltaMs = (now - _lastTickNanos) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        _lastTickNanos = now;
        // 封面流旋转时钟：进程级共享增量（同一时刻仅一个页面在动画，切页相位无缝衔接）
        s_sharedRenderMs += (long)(deltaMs * FrostedFlowAnimator.TimeScale);
        _renderTimeMs = s_sharedRenderMs;
        if (_animator != null)
            _animator.Advance((float)(deltaMs / 1000.0), true);

        TryRestoreSharedFrame();
        TryDispatchBackgroundRender();
        // 若能取到一帧已算好的后台结果，本次节拍直接上屏（无需等下一次节拍，减少延迟）
        PostPresentPendingFrame();
    }

    /// <summary>若当前无后台渲染进行中，把这一帧的渲染派发到线程池（快照全部输入，纯计算）。</summary>
    private void TryDispatchBackgroundRender()
    {
        var w = Width;
        var h = Height;
        if (w <= 0 || h <= 0) return;
        if (_animator == null || _preset == null) return;

        // 复用/重建低分辨率缓冲（主线程，仅可能在尺寸变化时重建）
        EnsureFlowBuffer(w, h);

        lock (_renderGate)
        {
            if (_renderBusy || _renderPending) return;   // 上一帧还在后台算/等待上屏 → 本帧跳过（丢帧铺满 8fps 节奏）
            _renderBusy = true;
        }

        // 快照：后台线程只读这些输入，绝不触碰平台 UI 对象
        var snapshotTime = _renderTimeMs;
        var snapW = _flowW;
        var snapH = _flowH;
        var snapCover = _coverSrc;
        var snapDark = _isDark;
        int snapAccent = _tintColor == Colors.Transparent ? 0 : PackRgb(_tintColor);
        int snapDpi = (int)(Context?.Resources?.DisplayMetrics?.DensityDpi ?? global::Android.Util.DisplayMetricsDensity.Default);
        var snapPreset = _preset;
        var snapColors = _colors;
        var snapColorStage = _animator.ColorStage;   // 流光颜色阶段（快照，主线程推进）
        // 记录后台渲染时的封面源/主题快照，供上屏后写入进程共享帧缓存（key 用）
        _pendingCover = snapCover;
        _pendingIsDark = snapDark;

        _ = Task.Run(() =>
        {
            int[]? pixels = null;
            int pw = 0, ph = 0;
            try
            {
                if (!snapCover.IsEmpty)
                {
                    var frame = CoverFlowProcessor.Render(snapCover, snapW, snapH, snapshotTime,
                        snapDpi, 60f, snapAccent, snapDark);
                    if (!frame.IsEmpty) { pixels = frame.Pixels; pw = frame.Width; ph = frame.Height; }
                }
                else
                {
                    // 流光回退：渲染进独立缓冲（后台单独一份，避免与主线程上屏竞态）
                    var argb = new int[snapW * snapH];
                    FrostedFlowProcessor.InterpolateColors(snapPreset, snapColorStage, snapColors!);
                    int baseArgb = FrostedFlowProcessor.Pack(12, 14, 30, 255);
                    FrostedFlowProcessor.Render(argb, snapW, snapH, snapPreset, snapColors!, snapColorStage,
                        0f, 0f, 1f, 1f, baseArgb);
                    pixels = argb; pw = snapW; ph = snapH;
                }
            }
            catch { }
            finally
            {
                lock (_renderGate)
                {
                    if (pixels != null && pw > 0 && ph > 0)
                    {
                        _pendingPixels = pixels;
                        _pendingW = pw;
                        _pendingH = ph;
                        _renderPending = true;
                    }
                    _renderBusy = false;
                }
                PostPresentPendingFrame(); // 后台完成 → 通知 UI 线程上屏
            }
        });
    }

    /// <summary>主线程上屏：把后台算好的像素一次性 SetPixels 到位图并触发重绘。</summary>
    private void PostPresentPendingFrame()
    {
        if (_disposed) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_disposed) return;
            int[]? px; int pw; int ph;
            lock (_renderGate)
            {
                if (!_renderPending) return;
                px = _pendingPixels!;
                pw = _pendingW; ph = _pendingH;
                _renderPending = false;
                _pendingPixels = null;
            }
            if (_flowBitmap == null || pw <= 0 || ph <= 0) return;
            try
            {
                if (pw != _flowW || ph != _flowH)
                {
                    // 尺寸不一致：按帧实际尺寸重建位图（铺满由 DrawBitmap 缩放完成）
                    _flowBitmap.Recycle();
                    _flowBitmap = Bitmap.CreateBitmap(pw, ph, Bitmap.Config.Argb8888)!;
                }
                _flowBitmap.SetPixels(px, 0, pw, 0, 0, pw, ph);
                // 进程级共享最后帧：供全屏歌词页等页面打开时秒级复用，且画面与播放页完全一致
                lock (s_sharedFrameGate)
                {
                    s_sharedFramePixels = px;
                    s_sharedFrameW = pw;
                    s_sharedFrameH = ph;
                    s_sharedFrameKey = _pendingCover;
                    s_sharedFrameDark = _pendingIsDark;
                }
            }
            catch { }
            Invalidate();
        });
    }

    /// <summary>更新封面源（封面流模式）。空源回退流光。</summary>
    public void UpdateCover(CoverFlowProcessor.CoverSource src)
    {
        if (_coverSrc.IsEmpty == src.IsEmpty
            && (_coverSrc.IsEmpty || _coverSrc.Argb == src.Argb)) return;
        _coverSrc = src;
        // 封面源就绪且共享帧同源：直接上屏（首次/重建时不黑屏不等渲染）
        TryRestoreSharedFrame();
        Invalidate();
    }

    private bool CoverFlowMode => !_coverSrc.IsEmpty;

    // ⚠ 帧渲染已移到后台线程（TryDispatchBackgroundRender → Task.Run），
    // 主线程只做 SetPixels 上屏（PostPresentPendingFrame）。此方法已移除，避免主线程 CPU 峰值。

    private static int PackRgb(Color c)
        => ((int)Math.Clamp(c.Red * 255, 0, 255) << 16)
         | ((int)Math.Clamp(c.Green * 255, 0, 255) << 8)
         | (int)Math.Clamp(c.Blue * 255, 0, 255);

    /// <summary>Halcyon 式纵向镜面遮罩：上轻 / 中透明 / 下轻，仅用于前景文字可读，不遮挡中间封面流。
    /// 固定使用 Halcyon 原版 alpha（不随 DimAmount 缩放，否则被压到 0.06/0.10 失去氛围）：
    /// 深色 = 黑纱 0.18/0.30；浅色 = 白纱 0.14/0.22。</summary>
    private void DrawLensScrim(Canvas canvas, int w, int h)
    {
        int sc = _isDark ? 0 : 255;
        int topAlpha = (int)((_isDark ? 0.18f : 0.14f) * 255);
        int bottomAlpha = (int)((_isDark ? 0.30f : 0.22f) * 255);

        var colors = new[] {
                global::Android.Graphics.Color.Argb(topAlpha, sc, sc, sc).ToArgb(),
                global::Android.Graphics.Color.Argb(0, sc, sc, sc).ToArgb(),
                global::Android.Graphics.Color.Argb(bottomAlpha, sc, sc, sc).ToArgb()
            };
        var stops = new[] { 0f, 0.5f, 1f };
        var shader = new LinearGradient(0, 0, 0, h, colors, stops, Shader.TileMode.Clamp);
        _dimPaint.SetShader(shader);
        canvas.DrawRect(0, 0, w, h, _dimPaint);
        _dimPaint.SetShader(null);
        shader.Dispose();
    }

    private void EnsureFlowBuffer(int viewW, int viewH)
    {
        // Halcyon 下采样：高 dpi 视口 1/24、否则 1/16（封面流是放大铺满的重模糊图，超低分辨率即可，
        // 且模糊半径占画面比例与原版一致，极光块才够大够柔）。
        int dpi = (int)(Context?.Resources?.DisplayMetrics?.DensityDpi
                        ?? global::Android.Util.DisplayMetricsDensity.Default);
        var (w, h) = CoverFlowProcessor.SuggestBufferSize(viewW, viewH, dpi);
        if (w == _flowW && h == _flowH && _flowBitmap != null) return;

        _flowW = w; _flowH = h;
        _flowBitmap?.Recycle();
        _flowBitmap = null;
        _flowBitmap = Bitmap.CreateBitmap(w, h, Bitmap.Config.Argb8888)!;
    }

    protected override void OnDraw(Canvas? canvas)
    {
        base.OnDraw(canvas);
        if (canvas == null) return;

        var w = Width;
        var h = Height;
        if (w <= 0 || h <= 0) return;

        _destRect.Set(0, 0, w, h);

        if (CoverFlowMode)
        {
            // 封面流模式（Halcyon）：封面流帧本身即背景（过饱和封面模糊、鲜亮贴合封面）。
            // 不叠色调渐变、不做全屏暗化；仅在最上方画"上下轻/中间透明"的镜面渐变保证文字可读。
            if (_flowBitmap != null && !_flowBitmap.IsRecycled)
            {
                _bitmapPaint.Alpha = 255;
                canvas.DrawBitmap(_flowBitmap, null, _destRect, _bitmapPaint);
            }
            else
            {
                // 首帧尚未就绪：用 palette.middle 不透明铺底（对应 Halcyon Box.background(middle)），
                // 避免封面流第一帧出来之前露黑场。
                int accent = _tintColor == Colors.Transparent ? 0 : PackRgb(_tintColor);
                var (mr, mg, mb) = CoverFlowProcessor.MiddleColor(accent, _isDark);
                _dimPaint.SetShader(null);
                _dimPaint.Color = global::Android.Graphics.Color.Rgb(mr, mg, mb);
                canvas.DrawRect(0, 0, w, h, _dimPaint);
            }
            DrawLensScrim(canvas, w, h);
            return;
        }

        bool hasTint = _tintOpacity > 0 && _tintColor != Colors.Transparent && _tintColor.Alpha > 0;

        if (hasTint)
        {
            // 1) 封面渐变作为不透明底色（Halcyon：top/mid/bottom 由 emphasis 明度求得），
            //    这才是"背景看起来像封面"的关键——封面颜色不再是半透明洗在流光上。
            _tintPaint.Alpha = 255;
            var shader = new LinearGradient(0, 0, 0, h,
                new[] { Argb(_tintColor, 0.60f), Argb(_tintColor, 0.34f), Argb(_tintColor, 0.14f) },
                new[] { 0f, 0.5f, 1f },
                Shader.TileMode.Clamp);
            _tintPaint.SetShader(shader);
            canvas.DrawRect(0, 0, w, h, _tintPaint);
            _tintPaint.SetShader(null);

            // 2) 流光盖在封面色底之上，半透明保留动效，封面主色仍一眼可见
            if (_flowBitmap != null && !_flowBitmap.IsRecycled)
            {
                _bitmapPaint.Alpha = (int)(FlowOverCoverAlpha * 255);
                canvas.DrawBitmap(_flowBitmap, null, _destRect, _bitmapPaint);
            }
        }
        else
        {
            // 无封面色：流光直接作为不透明底色
            if (_flowBitmap != null && !_flowBitmap.IsRecycled)
            {
                _bitmapPaint.Alpha = 255;
                canvas.DrawBitmap(_flowBitmap, null, _destRect, _bitmapPaint);
            }
        }

        // 暗化叠加层
        if (_dimAmount > 0)
        {
            _dimPaint.Color = global::Android.Graphics.Color.Argb((int)(_dimAmount * 255), 0, 0, 0);
            canvas.DrawRect(0, 0, w, h, _dimPaint);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            StopAnimation();
            _flowBitmap?.Recycle();
            _flowBitmap = null;
            _bitmapPaint?.Dispose();
            _tintPaint?.Dispose();
            _dimPaint?.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>FrostedBackground 的 MAUI Handler，将虚拟控件映射到 FrostedBackgroundView。</summary>
public class FrostedBackgroundHandler : ViewHandler<FrostedBackground, FrostedBackgroundView>
{
    public static readonly IPropertyMapper<FrostedBackground, FrostedBackgroundHandler> PropertyMapper =
        new PropertyMapper<FrostedBackground, FrostedBackgroundHandler>(ViewHandler.ViewMapper)
        {
            [nameof(FrostedBackground.IsActive)] = MapIsActive,
            [nameof(FrostedBackground.TintColor)] = MapTint,
            [nameof(FrostedBackground.TintOpacity)] = MapTint,
            [nameof(FrostedBackground.DimAmount)] = MapTint,
            [nameof(FrostedBackground.IsDark)] = MapIsDark,
            [nameof(FrostedBackground.IsScrolling)] = MapIsScrolling,
            [nameof(FrostedBackground.CoverSource)] = MapCover,
            [nameof(FrostedBackground.IsVisible)] = MapIsVisible,
            // IsVisible 的变更通过 IView.Visibility 键下发，需一并覆盖，否则开关切换不生效
            [nameof(Microsoft.Maui.IView.Visibility)] = MapIsVisible,
        };

    public FrostedBackgroundHandler() : base(PropertyMapper) { }

    protected override FrostedBackgroundView CreatePlatformView() => new(Context!);

    private static void MapIsActive(FrostedBackgroundHandler handler, FrostedBackground view)
        => handler.PlatformView.SetActive(view.IsActive);

    private static void MapTint(FrostedBackgroundHandler handler, FrostedBackground view)
        => handler.PlatformView.UpdateTint(view.TintColor, view.TintOpacity, view.DimAmount);

    private static void MapIsDark(FrostedBackgroundHandler handler, FrostedBackground view)
        => handler.PlatformView.SetDark(view.IsDark);

    private static void MapIsScrolling(FrostedBackgroundHandler handler, FrostedBackground view)
        => handler.PlatformView.SetScrolling(view.IsScrolling);

    private static void MapCover(FrostedBackgroundHandler handler, FrostedBackground view)
        => handler.PlatformView.UpdateCover(view.CoverSource);

    private static void MapIsVisible(FrostedBackgroundHandler handler, FrostedBackground view)
    {
        // 平台视图构造函数强制 Visibility=Visible，需显式同步 IsVisible
        handler.PlatformView.SetEnabled(view.IsVisible);
    }
}