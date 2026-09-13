#if WINDOWS
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;
using CatClawMusic.Maui.Services.Frosted;
using WColor = Windows.UI.Color;
using WPoint = Windows.Foundation.Point;
using WGrid = Microsoft.UI.Xaml.Controls.Grid;
using WImage = Microsoft.UI.Xaml.Controls.Image;
using WRectangle = Microsoft.UI.Xaml.Shapes.Rectangle;
using WStretch = Microsoft.UI.Xaml.Media.Stretch;
using WHorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment;
using WVerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment;
using WVisibility = Microsoft.UI.Xaml.Visibility;
using WSolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using WLinearGradientBrush = Microsoft.UI.Xaml.Media.LinearGradientBrush;
using WGradientStop = Microsoft.UI.Xaml.Media.GradientStop;
using MColor = Microsoft.Maui.Graphics.Color;

namespace CatClawMusic.Maui.Platforms.Windows;

/// <summary>
/// Windows 端流光喷发背景（Halcyon / Apple Music 风格，程序化生成，不读封面）。
/// 复用 WriteableBitmap 基础设施，每帧把共享 FrostedFlowProcessor 的低分辨率渲染结果
/// 写进同一位图并放大铺满，另叠一层柔和漂移。与 Android 端跑同一套 C# 数学。
///
/// 驱动语义与 Android 端对齐（三处平台差异的移植）：
/// - 节拍源：<see cref="CompositionTarget.Rendering"/>（逐帧、vsync 对齐；窗口不可见时自动停）
///   + 时间戳节流到 ~8fps，替代原 DispatcherTimer（低优先级，重负载/非前台时会被合并延迟）；
/// - 启动时机：平台视图 Loaded 即启动（等价 Android 的 OnAttachedToWindow），Unloaded/断开连接停表；
/// - 暂停门控：IsActive（播放状态）与 IsScrolling 共同决定，等价 Android 的
///   ShouldAnimate = isEnabled &amp;&amp; isPlaying &amp;&amp; !isScrolling；另在逐帧回调中跳过
///   折叠（IsVisible=false）状态，等价 Android 的 SetEnabled 停表。
/// </summary>
public class FrostedBackgroundHandler : ViewHandler<Controls.FrostedBackground, WGrid>
{
    public static IPropertyMapper<Controls.FrostedBackground, FrostedBackgroundHandler> Mapper =
        new PropertyMapper<Controls.FrostedBackground, FrostedBackgroundHandler>(ViewMapper)
        {
            [nameof(Controls.FrostedBackground.IsActive)] = MapIsActive,
            [nameof(Controls.FrostedBackground.TintColor)] = MapTint,
            [nameof(Controls.FrostedBackground.TintOpacity)] = MapTint,
            [nameof(Controls.FrostedBackground.DimAmount)] = MapTint,
            [nameof(Controls.FrostedBackground.IsDark)] = MapIsDark,
            [nameof(Controls.FrostedBackground.IsScrolling)] = MapIsScrolling,
            [nameof(Controls.FrostedBackground.CoverSource)] = MapCover,
        };

    private WImage? _image;
    private WRectangle? _tintOverlay;
    private WRectangle? _dimOverlay;
    /// <summary>是否已订阅 <see cref="CompositionTarget.Rendering"/>（静态事件，必须成对退订防泄漏/重复订阅）</summary>
    private bool _renderingHooked;
    /// <summary>IsActive（播放页/歌词页绑定 IsPlaying）：非激活时停表，对齐 Android 的 ShouldAnimate。
    /// 默认 true，未绑定 IsActive 的装饰性背景（模型页/设置页等）行为不变。</summary>
    private volatile bool _isActive = true;
    private volatile bool _isScrolling;
    private FrostedFlowAnimator? _animator;
    private FrostedFlowPreset? _preset;

    private WriteableBitmap? _flowBitmap;
    private int[]? _flowArgb;
    private float[]? _colors = new float[16];
    private int _flowW;
    private int _flowH;
    private long _lastTickTicks;

    /// <summary>节拍最小间隔（ms）：CompositionTarget.Rendering 逐帧回调，按此节流到 ~8fps，
    /// 与 Android 端 ValueAnimator 125ms 的节奏和"雾面背景低帧率省电"目标保持一致。</summary>
    private const double MinTickIntervalMs = 110.0;

    private const float FlowRatio = 0.78f;
    // 有封面色时，流光作为盖在封面色底上的半透明叠层（贴合 Halcyon：封面色是底色）
    private const float FlowOverCoverAlpha = 0.55f;

    private CoverFlowProcessor.CoverSource _coverSrc = default;   // 有封面时背景切为封面流
    private long _renderTimeMs;
    private bool _isDark;

    // ⚠ 进程级共享：封面流渲染时钟 + 最后一帧（与 Android 端语义一致）。
    // 播放页/全屏歌词页共用同一时钟与最后帧：切页时动画相位无缝衔接，新页面首帧直接复用播放页画面。
    private static long s_sharedRenderMs;
    private static readonly object s_sharedFrameGate = new();
    private static int[]? s_sharedFramePixels;
    private static int s_sharedFrameW, s_sharedFrameH;
    private static CoverFlowProcessor.CoverSource s_sharedFrameKey;
    private static bool s_sharedFrameDark;

    // ⚠ 后台渲染双缓冲：封面流/流光纯计算丢到 Task.Run，UI 线程只做 WriteBitmap 上屏。
    private readonly object _renderGate = new();
    private volatile bool _renderBusy;
    private bool _renderPending;
    private int[]? _pendingPixels;      // 后台算好的这一帧 ARGB
    private int _pendingW, _pendingH;
    private Microsoft.UI.Dispatching.DispatcherQueue? _uiDispatcher;  // 后台任务完成后的 UI 调度锚点

    private MColor _tintColor = MColor.FromRgb(0, 0, 0);

    public FrostedBackgroundHandler() : base(Mapper) { }

    protected override WGrid CreatePlatformView()
    {
        var grid = new WGrid
        {
            Background = new WSolidColorBrush(WColor.FromArgb(255, 11, 13, 32)),
        };
        _image = new WImage
        {
            Stretch = WStretch.UniformToFill,
            // 必须 Stretch 铺满：Center 对齐 + 初始无 Source 时 Image 量出 0×0，
            // 而 EnsureFlowBuffer 需要视口尺寸才建位图 → 位图不建、Source 不设、尺寸永远为 0（死锁，背景永远渲染不出帧）。
            HorizontalAlignment = WHorizontalAlignment.Stretch,
            VerticalAlignment = WVerticalAlignment.Stretch,
            RenderTransformOrigin = new WPoint(0.5, 0.5),
            RenderTransform = new CompositeTransform(),
        };
        _tintOverlay = new WRectangle { IsHitTestVisible = false };
        _dimOverlay = new WRectangle { IsHitTestVisible = false };
        // 层级：封面色调最底 → 流光中层 → 暗化最顶（贴合 Halcyon：封面色是不透明底色）
        grid.Children.Add(_tintOverlay);
        grid.Children.Add(_image);
        grid.Children.Add(_dimOverlay);

        // 上屏调度锚点在平台视图创建时（UI 线程）就取好：后台渲染完成后的上屏必须与
        // 「动画是否在跑」解耦。原先只在 StartAnimation 里赋值，导致未播放（门控停表）时
        // 后台算好的帧永远无法上屏，背景一直空白，直到动画启动才补首帧。
        _uiDispatcher ??= Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        // 加载即启动（等价 Android 的 OnAttachedToWindow 启动点）：
        // 不能只依赖 IsScrolling 变更触发——IsScrolling 初值 false 且属性默认值也是 false，
        // 首次应用绑定不产生变更通知 → 映射器不被调用 → 定时器永不启动（背景静止）。
        grid.Loaded += OnPlatformLoaded;
        grid.Unloaded += OnPlatformUnloaded;
        return grid;
    }

    /// <summary>平台视图加载：复用共享帧避免首帧黑场，先渲染一帧（保证立即有画面），随后按门控启动动画。</summary>
    private void OnPlatformLoaded(object sender, RoutedEventArgs e)
    {
        _uiDispatcher ??= Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        TryRestoreSharedFrame();
        TryDispatchBackgroundRender();   // 一次性出首帧：即使未播放也要立刻显示动态背景的静态画面
        UpdateAnimationState();
    }

    /// <summary>平台视图卸载（切页/移除）：停表，避免对不可见背景继续做后台渲染。</summary>
    private void OnPlatformUnloaded(object sender, RoutedEventArgs e) => StopAnimation();

    protected override void DisconnectHandler(WGrid platformView)
    {
        platformView.Loaded -= OnPlatformLoaded;
        platformView.Unloaded -= OnPlatformUnloaded;
        StopAnimation();
        _image = null;
        _tintOverlay = null;
        _dimOverlay = null;
        _flowBitmap = null;
        _flowArgb = null;
        base.DisconnectHandler(platformView);
    }

    private static void MapIsActive(FrostedBackgroundHandler handler, Controls.FrostedBackground view)
    {
        // 与 Android 对齐：IsActive（播放页/歌词页绑定 IsPlaying）参与动画门控，非播放时停表。
        // 未绑定 IsActive 的装饰性背景默认 true，仍常驻漂移，行为不变。
        handler._isActive = view.IsActive;
        handler.UpdateAnimationState();
    }

    private static void MapIsScrolling(FrostedBackgroundHandler handler, Controls.FrostedBackground view)
    {
        handler._isScrolling = view.IsScrolling;
        handler.UpdateAnimationState();
    }

    private static void MapTint(FrostedBackgroundHandler handler, Controls.FrostedBackground view)
    {
        handler.UpdateTint(view.TintColor, view.TintOpacity, view.DimAmount);
    }

    private static void MapIsDark(FrostedBackgroundHandler handler, Controls.FrostedBackground view)
    {
        handler.UpdateDark(view.IsDark);
    }

    private static void MapCover(FrostedBackgroundHandler handler, Controls.FrostedBackground view)
    {
        handler.UpdateCover(view.CoverSource);
    }

    private void UpdateCover(CoverFlowProcessor.CoverSource src)
    {
        try { CatClawMusic.Maui.Helpers.StartupLog.Log($"[CoverFlow] Handler UpdateCover: srcEmpty={src.IsEmpty}, px={src.Argb?.Length ?? 0}, flowMode={!_coverSrc.IsEmpty}->{!src.IsEmpty}"); } catch { }
        // 去重：数组引用不同（新解码的封面）或尺寸不同即视为新源。
        // 旧版只在"双空 或 旧源空"时短路，非空→非空的引用变化必须继续走到重渲染，
        // 否则切歌时新 CoverSource 被误判同源，背景停留在上一首（重开页面才恢复）。
        if (_coverSrc.IsEmpty && src.IsEmpty) return;
        if (!_coverSrc.IsEmpty && !src.IsEmpty
            && ReferenceEquals(_coverSrc.Argb, src.Argb)
            && _coverSrc.Width == src.Width && _coverSrc.Height == src.Height) return;
        _coverSrc = src;
        RefreshLayers();
        // 封面源就绪且共享帧同源：直接上屏（首次/重建时不黑屏不等渲染）
        TryRestoreSharedFrame();
        TryDispatchBackgroundRender();
    }

    private bool CoverFlowMode => !_coverSrc.IsEmpty;

    /// <summary>封面流模式：封面帧不透明居中铺满；色调叠层隐藏；暗化层改为上下轻/中间透明的镜面渐变。
    /// Grid 底色同步切到 palette.middle（Halcyon Box.background(middle)）：首帧出来前不露黑场。</summary>
    private void RefreshLayers()
    {
        if (_image != null) _image.Opacity = 1.0;
        if (_tintOverlay != null) _tintOverlay.Fill = null;
        if (PlatformView is WGrid g)
            g.Background = new WSolidColorBrush(CoverFlowMode ? MiddleWColor() : WColor.FromArgb(255, 11, 13, 32));
        // 用户要求无遮罩：封面流不再叠 Halcyon 镜面 scrim——浅色模式的白纱（0.14/0.22）
        // 显得刺眼、深色模式的黑纱（0.18/0.30）压得过暗，直接呈现封面流原色。
        // 文字可读性由前景文字阴影/歌词列局部底色承担（WinLyricClip 已有渐隐遮罩）。
        if (_dimOverlay != null) _dimOverlay.Fill = null;
    }

    /// <summary>palette.middle（与 CoverFlowProcessor 底色一致），用于封面流铺底。</summary>
    private WColor MiddleWColor()
    {
        int accent = _tintColor.Alpha <= 0 ? 0 : PackRgb(_tintColor);
        int ar = (accent >> 16) & 0xFF, ag = (accent >> 8) & 0xFF, ab = accent & 0xFF;
        if ((accent & 0xFFFFFF) == 0) { ar = 11; ag = 13; ab = 32; }
        // 深浅主题统一（用户定稿）：封面原色轻微提亮 20%，深色模式不再压暗加黑——
        // 与浅色模式同一套调和，背景呈现封面原色。Android 端仍走 CoverFlowProcessor.MiddleColor。
        return WColor.FromArgb(255,
            (byte)(ar + (255 - ar) * 0.20f),
            (byte)(ag + (255 - ag) * 0.20f),
            (byte)(ab + (255 - ab) * 0.20f));
    }

    /// <summary>确保流光预设与计时器已初始化。IsDark 未绑定到控件时 MapIsDark 不会触发，
    /// 若首个渲染（加载出首帧 / 封面源就绪）早于动画启动，快照会取到 null 的计时器。</summary>
    private void EnsureFlowInitialized()
    {
        if (_animator != null && _preset != null) return;
        _preset = FrostedFlowPreset.Choose(_isDark);
        _animator = new FrostedFlowAnimator(_preset.ColorInterpPeriod);
    }

    private void UpdateDark(bool isDark)
    {
        _isDark = isDark;
        _preset = FrostedFlowPreset.Choose(isDark);
        _animator = new FrostedFlowAnimator(_preset.ColorInterpPeriod);
        RefreshLayers();              // 底色/scrim 随深浅切换
        TryRestoreSharedFrame();
        TryDispatchBackgroundRender(); // 洗色参数变了，立即重渲一帧
    }

    private void UpdateTint(MColor tintColor, double tintOpacity, double dimAmount)
    {
        _tintColor = tintColor;
        if (CoverFlowMode)
        {
            // 封面流模式：不叠色调渐变，暗化层用镜面渐变（上下轻/中间透明）
            RefreshLayers();
            return;
        }
        bool hasTint = tintOpacity > 0 && tintColor.Alpha > 0;
        if (_tintOverlay != null)
        {
            if (hasTint)
            {
                // Halcyon 式纵向明度渐变（top=accent×0.60 / mid×0.34 / bottom×0.14），
                // 作为不透明底色铺满，让背景横向呈现封面深色相位。
                var brush = new WLinearGradientBrush
                {
                    StartPoint = new WPoint(0, 0),
                    EndPoint = new WPoint(0, 1),
                };
                brush.GradientStops.Add(new WGradientStop { Color = TintStep(tintColor, 0.60f), Offset = 0.0 });
                brush.GradientStops.Add(new WGradientStop { Color = TintStep(tintColor, 0.34f), Offset = 0.5 });
                brush.GradientStops.Add(new WGradientStop { Color = TintStep(tintColor, 0.14f), Offset = 1.0 });
                _tintOverlay.Fill = brush;
            }
            else
            {
                _tintOverlay.Fill = null;
            }
        }
        // 流光：有封面色时作为半透明顶层，否则不透明底色
        if (_image != null)
            _image.Opacity = hasTint ? FlowOverCoverAlpha : 1.0;
        // 用户要求无遮罩：不再叠 DimAmount 暗化层（属性保留以兼容 Android 端与绑定）
        if (_dimOverlay != null) _dimOverlay.Fill = null;
    }

    /// <summary>Halcyon 式纵向镜面渐变遮罩（上轻/中透明/下轻），仅用于前景文字可读。
    /// 固定 Halcyon 原版 alpha（不随 DimAmount 缩放）：深色 = 黑纱 0.18/0.30；浅色 = 白纱 0.14/0.22。</summary>
    private Microsoft.UI.Xaml.Media.Brush ScrimBrush()
    {
        byte sc = (byte)(_isDark ? 0 : 255);
        byte top = (byte)((_isDark ? 0.18f : 0.14f) * 255);
        byte bottom = (byte)((_isDark ? 0.30f : 0.22f) * 255);
        var brush = new WLinearGradientBrush
        {
            StartPoint = new WPoint(0, 0),
            EndPoint = new WPoint(0, 1),
        };
        brush.GradientStops.Add(new WGradientStop { Color = WColor.FromArgb(top, sc, sc, sc), Offset = 0.0 });
        brush.GradientStops.Add(new WGradientStop { Color = WColor.FromArgb(0, sc, sc, sc), Offset = 0.5 });
        brush.GradientStops.Add(new WGradientStop { Color = WColor.FromArgb(bottom, sc, sc, sc), Offset = 1.0 });
        return brush;
    }

    /// <summary>按明度系数缩放 MAUI 颜色得到不透明 WinUI 渐变台阶（Halcyon darken 阶梯用）。</summary>
    private static WColor TintStep(MColor c, float factor)
        => WColor.FromArgb(255,
            (byte)Math.Clamp(c.Red * factor * 255, 0, 255),
            (byte)Math.Clamp(c.Green * factor * 255, 0, 255),
            (byte)Math.Clamp(c.Blue * factor * 255, 0, 255));

    private void UpdateAnimationState()
    {
        // 与 Android 的 ShouldAnimate 等价：激活（播放中）且未在滑动列表时跑动画
        if (_isActive && !_isScrolling)
            StartAnimation();
        else
            StopAnimation();
    }

    private void StartAnimation()
    {
        if (_animator == null) UpdateDark(_isDark);   // 首次启动补齐预设/计时器与图层底色
        if (_renderingHooked) return;
        _uiDispatcher ??= Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _lastTickTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        // 逐帧回调（vsync 对齐）替代 DispatcherTimer：后者优先级低，重负载或窗口非前台时
        // 会被合并/延迟，表现为背景动画停顿；CompositionTarget.Rendering 是 WinUI 官方逐帧点，
        // 窗口不可见时自动停止（天然省电），与 Android ValueAnimator 的驱动语义一致。
        CompositionTarget.Rendering += OnRendering;
        _renderingHooked = true;
    }

    private void StopAnimation()
    {
        if (!_renderingHooked) return;
        CompositionTarget.Rendering -= OnRendering;
        _renderingHooked = false;
    }

    /// <summary>逐帧回调：按时间戳节流到 ~8fps（丢弃中间帧），其余逻辑与原定时器节拍一致。</summary>
    private void OnRendering(object? sender, object e)
    {
        if (_image == null || _animator == null || _preset == null) return;

        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var dtMs = (now - _lastTickTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        if (dtMs < MinTickIntervalMs) return;   // 未到节拍间隔：丢弃该帧
        _lastTickTicks = now;

        // 隐藏态不做渲染与漂移：等价 Android 的 SetEnabled 停表语义。
        // 没有这道闸，"加载即启动"会让已关闭（IsVisible=false → Collapsed）的雾面背景
        // 仍在后台 8fps 渲染，白白占用线程池与 GPU 上传。
        if (PlatformView is null || PlatformView.Visibility != WVisibility.Visible) return;

        // 封面流旋转时钟：进程级共享增量（同一时刻仅一个页面在动画，切页相位无缝衔接）
        s_sharedRenderMs += (long)(dtMs * FrostedFlowAnimator.TimeScale);
        _renderTimeMs = s_sharedRenderMs;
        _animator.Advance((float)(dtMs / 1000.0), true);

        TryRestoreSharedFrame();
        TryDispatchBackgroundRender();
        PresentPendingFrame();   // 后台算完则本帧上屏
        ApplyDrift();
    }

    /// <summary>把这一帧的渲染派发到线程池（快照全部输入，纯计算，不碰 UI 对象）。</summary>
    private void TryDispatchBackgroundRender()
    {
        if (_image == null) return;
        // IsDark 未绑定到控件时 MapIsDark 不会触发，首个渲染前需自兜底初始化预设/计时器
        EnsureFlowInitialized();

        var viewW = (float)_image.ActualWidth;
        var viewH = (float)_image.ActualHeight;
        // 兜底：Image 首帧尚未量出尺寸（未设 Source 时可能为 0×0）时用承载 Grid 的尺寸，
        // 否则会陷入「无尺寸→不建位图→不设 Source→永远无尺寸」的死锁，背景永不渲染。
        if ((viewW < 1 || viewH < 1) && PlatformView != null)
        {
            viewW = (float)PlatformView.ActualWidth;
            viewH = (float)PlatformView.ActualHeight;
        }
        if (viewW < 1 || viewH < 1) return;

        EnsureFlowBuffer(viewW, viewH);

        lock (_renderGate)
        {
            if (_renderBusy || _renderPending) return;
            _renderBusy = true;
        }

        var snapshotTime = _renderTimeMs;
        var snapW = _flowW;
        var snapH = _flowH;
        var snapCover = _coverSrc;
        var snapDark = _isDark;
        int snapAccent = _tintColor.Alpha <= 0 ? 0 : PackRgb(_tintColor);
        var snapPreset = _preset;
        var snapColors = _colors;
        var snapColorStage = _animator.ColorStage;

        _ = Task.Run(() =>
        {
            int[]? pixels = null;
            int pw = 0, ph = 0;
            try
            {
                if (!snapCover.IsEmpty)
                {
                    var frame = CoverFlowProcessor.Render(snapCover, snapW, snapH, snapshotTime,
                        0, 60f, snapAccent, snapDark);
                    if (!frame.IsEmpty) { pixels = frame.Pixels; pw = frame.Width; ph = frame.Height; }
                }
                else
                {
                    var argb = new int[snapW * snapH];
                    FrostedFlowProcessor.InterpolateColors(snapPreset, snapColorStage, snapColors!);
                    int baseArgb = FrostedFlowProcessor.Pack(11, 13, 32, 255);
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
                // UI 线程上屏（后台完成的帧交给 UI DispatcherQueue 处理）。
                // 不再要求"动画在跑"：未播放（门控停表）时也要把这一帧贴上，否则背景空白。
                if (_uiDispatcher != null)
                    _uiDispatcher.TryEnqueue(() => PresentPendingFrame());
            }
        });
    }

    /// <summary>UI 线程上屏：取后台算好的像素写入 WriteableBitmap。</summary>
    private void PresentPendingFrame()
    {
        int[]? px; int pw; int ph;
        lock (_renderGate)
        {
            if (!_renderPending) return;
            px = _pendingPixels!;
            pw = _pendingW; ph = _pendingH;
            _renderPending = false;
            _pendingPixels = null;
        }
        if (px == null || pw <= 0 || ph <= 0) return;

        if (pw != _flowW || ph != _flowH)
        {
            _flowW = pw; _flowH = ph;
            _flowBitmap = null;   // WriteBitmap 会按新尺寸重建
            _flowArgb = null;
        }
        _flowArgb = px;
        WriteBitmap();
        // 进程级共享最后帧：供全屏歌词页等页面打开时秒级复用，画面与播放页完全一致
        lock (s_sharedFrameGate)
        {
            s_sharedFramePixels = px;
            s_sharedFrameW = pw;
            s_sharedFrameH = ph;
            s_sharedFrameKey = _coverSrc;
            s_sharedFrameDark = _isDark;
        }
    }

    /// <summary>复用进程共享的最后一帧：同封面源、同尺寸、同深浅色时直接把共享帧设为当前位图。
    /// 全屏歌词页打开 / 播放页切回时，首帧即与另一页面当前画面一致，无闪黑、无重新渲染等待。</summary>
    private void TryRestoreSharedFrame()
    {
        if (!CoverFlowMode || _flowArgb != null || _image == null) return;
        lock (s_sharedFrameGate)
        {
            if (s_sharedFramePixels == null || s_sharedFrameW <= 0 || s_sharedFrameH <= 0) return;
            if (!ReferenceEquals(s_sharedFrameKey.Argb, _coverSrc.Argb)
                || s_sharedFrameDark != _isDark)
                return; // 封面源/主题已变：共享帧过期，等后台正常渲染
            _flowW = s_sharedFrameW;
            _flowH = s_sharedFrameH;
            _flowArgb = s_sharedFramePixels;
            _flowBitmap = null;   // WriteBitmap 按共享帧尺寸重建
            WriteBitmap();
        }
    }

    private static int PackRgb(MColor c)
        => ((int)Math.Clamp(c.Red * 255, 0, 255) << 16)
         | ((int)Math.Clamp(c.Green * 255, 0, 255) << 8)
         | (int)Math.Clamp(c.Blue * 255, 0, 255);

    private void WriteBitmap()
    {
        if (_flowArgb == null || _flowArgb.Length == 0) return;
        // ARGB -> BGRA(premultiplied) 写入可复用 WriteableBitmap。
        // BGRA 转换缓冲跨帧复用（稳态 8fps 下旧版每 125ms 分配一个整帧 byte[]，纯 GC 压力）。
        var requiredBytes = _flowArgb.Length * 4;
        if (_flowBgra == null || _flowBgra.Length != requiredBytes)
            _flowBgra = new byte[requiredBytes];
        if (_flowBitmap == null)
        {
            _flowBitmap = new WriteableBitmap(_flowW, _flowH);
            _image.Source = _flowBitmap;
        }
        var bgra = _flowBgra;
        int j = 0;
        foreach (var p in _flowArgb)
        {
            bgra[j++] = (byte)(p & 0xFF);          // B
            bgra[j++] = (byte)((p >> 8) & 0xFF);   // G
            bgra[j++] = (byte)((p >> 16) & 0xFF);  // R
            bgra[j++] = (byte)((p >> 24) & 0xFF);  // A
        }
        using (Stream stream = _flowBitmap.PixelBuffer.AsStream())
        {
            stream.Write(bgra, 0, bgra.Length);
        }
        _flowBitmap.Invalidate();
    }

    /// <summary>ARGB→BGRA 转换缓冲（尺寸变化时重建，稳态零分配）</summary>
    private byte[]? _flowBgra;

    private void EnsureFlowBuffer(float viewW, float viewH)
    {
        // Halcyon 下采样：桌面 dpi 传 0 → 视口 1/18（封面流是放大铺满的重模糊图，超低分辨率即可，
        // 模糊块占画面比例与原版一致，极光块才够大够柔）。
        var (w, h) = CoverFlowProcessor.SuggestBufferSize((int)viewW, (int)viewH, 0);
        if (w == _flowW && h == _flowH && _flowArgb != null) return;
        _flowW = w; _flowH = h;
        _flowArgb = new int[w * h];
        _flowBitmap = null;   // 尺寸变化需重建位图
    }

    private void ApplyDrift()
    {
        if (_image?.RenderTransform is not CompositeTransform ct) return;
        // 轻微放大 + 平缓漂移，给程序化流光再添一点有机动感
        float t = _animator!.AnimTime * 0.6f;
        ct.ScaleX = ct.ScaleY = 1.08f;
        ct.TranslateX = 14f * (float)Math.Sin(t * 0.6 + 1.3);
        ct.TranslateY = 10f * (float)Math.Cos(t * 0.7 + 0.6);
        ct.Rotation = 0;
    }
}
#endif