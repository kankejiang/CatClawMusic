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
using WSolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using WLinearGradientBrush = Microsoft.UI.Xaml.Media.LinearGradientBrush;
using WGradientStop = Microsoft.UI.Xaml.Media.GradientStop;
using MColor = Microsoft.Maui.Graphics.Color;

namespace CatClawMusic.Maui.Platforms.Windows;

/// <summary>
/// Windows 端流光喷发背景（Halcyon / Apple Music 风格，程序化生成，不读封面）。
/// 复用 WriteableBitmap + DispatcherTimer 基础设施，每帧把共享 FrostedFlowProcessor 的
/// 低分辨率渲染结果写进同一位图并放大铺满，另叠一层柔和漂移。与 Android 端跑同一套 C# 数学。
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
    private DispatcherTimer? _timer;
    private volatile bool _isScrolling;
    private FrostedFlowAnimator? _animator;
    private FrostedFlowPreset? _preset;

    private WriteableBitmap? _flowBitmap;
    private int[]? _flowArgb;
    private float[]? _colors = new float[16];
    private int _flowW;
    private int _flowH;
    private long _lastTickTicks;

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
            HorizontalAlignment = WHorizontalAlignment.Center,
            VerticalAlignment = WVerticalAlignment.Center,
            RenderTransformOrigin = new WPoint(0.5, 0.5),
            RenderTransform = new CompositeTransform(),
        };
        _tintOverlay = new WRectangle { IsHitTestVisible = false };
        _dimOverlay = new WRectangle { IsHitTestVisible = false };
        // 层级：封面色调最底 → 流光中层 → 暗化最顶（贴合 Halcyon：封面色是不透明底色）
        grid.Children.Add(_tintOverlay);
        grid.Children.Add(_image);
        grid.Children.Add(_dimOverlay);
        return grid;
    }

    protected override void DisconnectHandler(WGrid platformView)
    {
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
        // 桌面端始终常驻显示并漂移，不随播放状态停；启用开关由控件 IsVisible 决定
        // _isActive 字段省略：动画仅受 IsScrolling 门控
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
        if (_coverSrc.IsEmpty == src.IsEmpty
            && (_coverSrc.IsEmpty || _coverSrc.Argb == src.Argb)) return;
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
        if (_dimOverlay != null) _dimOverlay.Fill = CoverFlowMode ? ScrimBrush() : null;
    }

    /// <summary>palette.middle（与 CoverFlowProcessor 底色一致），用于封面流铺底。</summary>
    private WColor MiddleWColor()
    {
        int accent = _tintColor.Alpha <= 0 ? 0 : PackRgb(_tintColor);
        var (r, g, b) = CoverFlowProcessor.MiddleColor(accent, _isDark);
        return WColor.FromArgb(255, (byte)r, (byte)g, (byte)b);
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
        if (_dimOverlay != null)
        {
            byte a = (byte)Math.Clamp(dimAmount * 255, 0, 255);
            _dimOverlay.Fill = new WSolidColorBrush(WColor.FromArgb(a, 0, 0, 0));
        }
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
        if (!_isScrolling)
            StartAnimation();
        else
            StopAnimation();
    }

    private void StartAnimation()
    {
        if (_animator == null) UpdateDark(false);
        if (_timer != null) return;
        _uiDispatcher ??= Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _lastTickTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(125) };  // ~8fps，与 Android 一致，省电
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void StopAnimation()
    {
        _timer?.Stop();
        _timer = null;
    }

    private void OnTick(object? sender, object e)
    {
        if (_image == null || _animator == null || _preset == null) return;

        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var dtMs = (now - _lastTickTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        _lastTickTicks = now;
        // 封面流旋转时钟：进程级共享增量（同一时刻仅一个页面在动画，切页相位无缝衔接）
        s_sharedRenderMs += (long)(dtMs * FrostedFlowAnimator.TimeScale);
        _renderTimeMs = s_sharedRenderMs;
        _animator.Advance((float)(dtMs / 1000.0), true);

        TryRestoreSharedFrame();
        TryDispatchBackgroundRender();
        PresentPendingFrame();   // 后台算完则本 tick 上屏
        ApplyDrift();
    }

    /// <summary>把这一帧的渲染派发到线程池（快照全部输入，纯计算，不碰 UI 对象）。</summary>
    private void TryDispatchBackgroundRender()
    {
        var viewW = (float)_image.ActualWidth;
        var viewH = (float)_image.ActualHeight;
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
                // UI 线程上屏（后台完成的帧交给 UI DispatcherQueue 处理）
                if (_uiDispatcher != null && !_uiDispatcher.HasThreadAccess)
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
        // ARGB -> BGRA(premultiplied) 写入可复用 WriteableBitmap
        if (_flowBitmap == null)
        {
            _flowBitmap = new WriteableBitmap(_flowW, _flowH);
            _image.Source = _flowBitmap;
        }
        using (Stream stream = _flowBitmap.PixelBuffer.AsStream())
        {
            var bgra = new byte[_flowArgb.Length * 4];
            int j = 0;
            foreach (var p in _flowArgb)
            {
                bgra[j++] = (byte)(p & 0xFF);          // B
                bgra[j++] = (byte)((p >> 8) & 0xFF);   // G
                bgra[j++] = (byte)((p >> 16) & 0xFF);  // R
                bgra[j++] = (byte)((p >> 24) & 0xFF);  // A
            }
            stream.Write(bgra, 0, bgra.Length);
        }
        _flowBitmap.Invalidate();
    }

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