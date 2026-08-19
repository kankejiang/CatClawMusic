using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.Services;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using System.IO;
using System.Linq;

namespace CatClawMusic.Maui.Pages;

/// <summary>正在播放页面，展示当前播放歌曲的封面、进度、歌词及播放控制。</summary>
public partial class NowPlayingPage : ContentPage
{
    private readonly NowPlayingViewModel _viewModel;
    private readonly SleepTimerService _sleepTimer;
    private readonly IMusicLibraryService _musicLibrary;
    private readonly AudioPlayerService _audioPlayer;
    private readonly Services.DesktopLyricManager _desktopLyricManager;
    private bool _isDragging;
    private readonly List<KaraokeLabel> _lyricLabels = new();
    private readonly List<Border> _lyricBorders = new();
    private int _lastHighlightIndex = -1;

    /// <summary>歌词表面可见性令牌：页面可见期间持有，Disappearing 时释放，
    /// 驱动 AudioPlayerService 位置定时器的 60fps ↔ 5Hz 自适应降频。</summary>
    private IDisposable? _playerSurfaceToken;

    // 竖屏歌词滚动（复刻 Windows）：自绘静态堆叠 + 整体平移 TranslationY。
    // _lyricRowViews = 每行根视图（Border 或 含译文的 VerticalStackLayout）；
    // _lyricRowTops = 实测行高累加锚点表；滚动目标 = 当前行中心恒钉在裁剪区 1/3 处。
    private readonly List<View> _lyricRowViews = new();
    private double[] _lyricRowTops = Array.Empty<double>();
    private double[] _lastMeasuredTops = Array.Empty<double>();
    private double _lyricClipHeight;
    private int _lyricMeasureRetries;
    private bool _isLandscape;
    private int _lastCoverSize;
    private bool _landscapeLyricsMode;
    private readonly List<KaraokeLabel> _landscapeLyricLabels = new();
    private readonly List<Border> _landscapeLyricBorders = new();
    private int _landscapeLastHighlight = -1;
    private readonly LyricsSettingsService _settings = LyricsSettingsService.Instance;

    /// <summary>LyricClip Handler 变化时触发：就绪后实测行高并把当前行钉到 1/3 处。</summary>
    private void OnCollectionViewHandlerChanged(object? sender, EventArgs e)
    {
        if (LyricClip.Handler != null && _lyricRowViews.Count > 0 && _viewModel.CurrentLyricIndexObservable >= 0)
        {
            _ = Task.Delay(300).ContinueWith(_ =>
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (LyricClip.Handler != null && !_isLandscape)
                    {
                        MeasureLyricRows();
                        ScrollToLine(_viewModel.CurrentLyricIndexObservable);
                    }
                }));
        }
    }

    /// <summary>初始化 <see cref="NowPlayingPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="viewModel">当前播放视图模型，提供歌曲、进度与歌词数据。</param>
    /// <param name="sleepTimer">睡眠定时服务。</param>
    /// <param name="musicLibrary">音乐库服务（歌单操作）。</param>
    /// <param name="audioPlayer">音频播放服务（均衡器应用）。</param>
    /// <param name="desktopLyricManager">桌面歌词协调器（Windows 悬浮歌词开关）。</param>
    public NowPlayingPage(NowPlayingViewModel viewModel, SleepTimerService sleepTimer,
        IMusicLibraryService musicLibrary, AudioPlayerService audioPlayer,
        Services.DesktopLyricManager desktopLyricManager)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _sleepTimer = sleepTimer;
        _musicLibrary = musicLibrary;
        _audioPlayer = audioPlayer;
        _desktopLyricManager = desktopLyricManager;
        BindingContext = _viewModel;

        // FM 模式选择抽屉：VM 加载完模式列表后触发事件，页面动态构建抽屉内容
        _viewModel.FmModeDrawerRequested += OnFmModeDrawerRequested;

        // 控件级事件：在构造函数中订阅一次，永不取消（控件实例随页面存活，无泄漏风险）
        LyricClip.HandlerChanged += OnCollectionViewHandlerChanged;
        Loaded += OnPageLoaded;

        // 锁屏解锁 / 从后台切回前台时，Activity/Handler 可能重建导致旧锚点失效；
        // 监听全局 App.Resumed 事件，立即重测并把当前行钉回 1/3 处，修复"锁屏后高亮位置不对"。
        App.Resumed -= OnAppResumed;
        App.Resumed += OnAppResumed;

        // 静态/单例事件：通过 HandlerChanged 管理订阅生命周期，支持页面实例复用（Singleton）。
        // 页面挂载时订阅、分离时取消，避免横竖屏切换后旧订阅残留或新挂载时漏订阅。
        HandlerChanged += (_, _) =>
        {
            if (Handler == null)
            {
#if ANDROID
                Android.Util.Log.Info("NPP", "[NowPlayingPage] Handler=null(取消订阅) #{0}", GetHashCode());
#endif
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                SafeAreaHelper.SafeAreaChanged -= OnSafeAreaChanged;
            }
            else
            {
                // 挂载（或重新挂载）：订阅静态/单例事件
                // 先 -= 再 += 避免重复订阅
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
                SafeAreaHelper.SafeAreaChanged -= OnSafeAreaChanged;
                SafeAreaHelper.SafeAreaChanged += OnSafeAreaChanged;
            }
        };

        // 监听 RootGrid 尺寸变化（Content 被 MainPage 提取后 OnSizeAllocated 不会触发）
        RootGrid.SizeChanged += OnRootSizeChanged;

        // 设置收起图标（使用 ImageSourceHelper 确保 Windows 端正确加载）
        CollapseIcon.Source = ImageSourceHelper.FromNameOriginal("ic_collapse");
        // 横屏布局右上角收起按钮复用同一图标
        LandscapeCollapseIcon.Source = ImageSourceHelper.FromNameOriginal("ic_collapse");
#if WINDOWS
        // Windows 桌面播放布局的收起按钮
        WinCollapseIcon.Source = ImageSourceHelper.FromNameOriginal("ic_collapse");
#endif

        // 播放页封面原生 ImageView 懒创建（尤其横屏 LandscapeCoverImage 在首次显示时才建 Handler），
        // 在其 Handler 就绪后立即打标记，确保自定义图像服务对封面一律高分辨率解码（横竖屏一致）。
        ArtworkImage.HandlerChanged += (_, _) => TagPlayerCoverViews();
        LandscapeCoverImage.HandlerChanged += (_, _) => TagPlayerCoverViews();
    }

    private void OnRootSizeChanged(object? sender, EventArgs e)
    {
        var w = RootGrid.Width;
        var h = RootGrid.Height;
        if (w > 0 && h > 0)
            ApplyLayoutForOrientation(w, h);
    }

    private void OnPageLoaded(object? sender, EventArgs e)
    {
        UpdateTimerButtonState();
    }

    private void OnAppResumed(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // 回前台：竖屏歌词立即重测并把当前行钉回 1/3 处（无缓动），
            // 同时追加 3 次重试，兜底"Handler 重建/二次布局晚于本次回调"的情况。
            ForcePinCurrentLine();
            ScheduleRetriedPin(3);
        });
    }

    /// <summary>系统栏高度变化时触发，更新内容区域的顶部 padding 以避开状态栏</summary>
    private void OnSafeAreaChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => ApplySafeArea());
    }

    /// <summary>给 RootGrid 应用 SafeArea 顶部 padding（雾面背景不应用，保持延伸到状态栏）。
    /// 竖屏：顶部+12px、底部+16px 基础间距。
    /// 横屏：顶部状态栏+5px、底部=max(状态栏,dock栏)+5px，封面上下对称留出最小间距。</summary>
    private void ApplySafeArea()
    {
#if WINDOWS
        // Windows 端使用 WindowsStage 自身的顶部安全区 padding
        ApplyWindowsSafeArea();
        return;
#endif
        var topInset = SafeAreaHelper.TopInset;
        var bottomInset = SafeAreaHelper.BottomInset;

        if (_isLandscape)
        {
            // 横屏：内容区域上下贴边（仅留5px呼吸间距）
            // 顶部 = 状态栏 + 5px
            // 底部 = max(状态栏, dock栏) + 5px
            //   手机/平板无 dock 栏（bottomInset≈0），用 topInset 与顶部对称
            //   车机有 dock 栏（bottomInset>0），用 bottomInset 确保不遮挡
            var effectiveBottom = Math.Max(topInset, bottomInset) + 5;
            RootGrid.Padding = new Thickness(20, topInset + 5, 20, effectiveBottom);
            // LandscapeRoot 上下 padding 为 0（RootGrid 已处理上下间距），
            // 左右 48px 保证右栏内容不贴边
            LandscapeRoot.Padding = new Thickness(48, 0, 48, 0);
        }
        else
        {
            // 竖屏：原始 Padding (20,12,20,16) + 状态栏/底部安全区
#if ANDROID || WINDOWS
            var bottom = 16 + bottomInset;
#else
            var bottom = 16;
#endif
            RootGrid.Padding = new Thickness(20, topInset + 12, 20, bottom);
        }
    }

    /// <summary>页面尺寸分配时触发，根据宽高比切换横屏/竖屏布局。</summary>
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        ApplyLayoutForOrientation(width, height);
    }

    /// <summary>根据屏幕方向动态切换主体内容与底部控件的布局。
    /// 横屏（宽>高）：封面在左、歌词在右，底部使用三栏控件（等同 PC 布局）。
    /// 竖屏（高>=宽）：封面在上、歌词在下，底部使用 5 列等分控件。</summary>
    private void ApplyLayoutForOrientation(double width, double height)
    {
        if (width <= 0 || height <= 0) return;

#if WINDOWS
        // Windows 桌面端：使用独立的 WindowsStage 布局（基于 Aurora 原型），
        // 完全跳过手机竖屏/横屏逻辑，安卓端不受影响。
        ApplyWindowsLayout(width, height);
        return;
#endif

        var isLandscape = width > height;
        var topInset = SafeAreaHelper.TopInset;
        var bottomInset = SafeAreaHelper.BottomInset;

        // 封面尺寸计算：
        // 竖屏：根据较短边计算，左右各留30px间距。
        // 横屏：三个边定一个正方形——
        //   顶部 = 状态栏 + 5px
        //   底部 = max(状态栏, dock栏) + 5px（手机/平板用状态栏对称，车机用dock栏）
        //   左边 = 状态栏 + 5px
        //   边长 = min(可用高度, 左栏可用宽度)
        int coverSize;
        if (isLandscape)
        {
            // 可用高度 = height - 顶部预留 - 底部预留
            // 顶部预留 = topInset + 5，底部预留 = max(topInset, bottomInset) + 5
            double effectiveBottomInset = Math.Max(topInset, bottomInset);
            double availableHeight = height - topInset - effectiveBottomInset - 10;
            // 左栏宽度：LandscapeRoot 内容宽度 = width - 20*2(RootGrid左右) - 48*2(LandscapeRoot左右) - 32(列间距)
            // 左栏占比 1 / (1+1.15) = 1/2.15
            double landscapeContentWidth = width - 40 - 96 - 32;
            double leftColumnWidth = landscapeContentWidth / 2.15;
            // 封面左边距(相对于左栏Grid) = 目标左边距 - RootGrid左padding - LandscapeRoot左padding
            // 目标左边距 = topInset + 5（相对于屏幕左边）
            double leftMarginInGrid = (topInset + 5) - 20 - 48; // = topInset - 63
            // 左栏可用宽度 = 左栏宽度 - 左边距（左边距为负时增加可用宽度）
            double availableWidth = leftColumnWidth - leftMarginInGrid;
            // 封面尺寸 = min(可用高度, 可用宽度)，保留 240-800 的合理范围
            coverSize = Math.Clamp((int)Math.Min(availableHeight, availableWidth), 240, 800);
        }
        else
        {
            coverSize = Math.Clamp((int)(width - 60), 280, 560);
        }

        if (_isLandscape == isLandscape && _lastCoverSize == coverSize)
            return;

        var orientationChanged = _isLandscape != isLandscape;
        _isLandscape = isLandscape;

        if (isLandscape)
        {
            // 横屏：使用独立 LandscapeRoot 布局（左封面 + 右信息/控制），隐藏竖屏与三栏控件
            if (orientationChanged)
            {
                LandscapeRoot.IsVisible = true;
                MainContent.IsVisible = false;
                BottomControlsRoot.IsVisible = false;
                TopNavBar.IsVisible = false;
                PhoneControls.IsVisible = false;
                DesktopControls.IsVisible = false;
                BottomActionBar.IsVisible = false;
                // 横屏封面首次显示时 Handler 才创建，可能错过 PlayerCoverTag 导致低分辨率解码；
                // 延迟重载一次封面，确保使用高分辨率桶。
                _ = ReloadCoverHighResAsync(LandscapeCoverImage);
                // 方向切换后立即应用横屏 SafeArea，确保车机 dock 栏下内容不被遮挡
                ApplySafeArea();
            }
            RightHalf.ClearValue(HeightRequestProperty);
            MainContent.RowDefinitions = new RowDefinitionCollection
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star }
            };
            // 横屏封面位置：顶部贴齐（VerticalOptions=Start，顶部=0即对齐状态栏+5），
            // 左边距 = topInset + 5 - RootGrid左padding - LandscapeRoot左padding
            double leftMargin = topInset + 5 - 20 - 48;
            LandscapeCover.Margin = new Thickness(leftMargin, 0, 0, 0);
        }
        else
        {
            // 竖屏：封面上、歌词下，歌词区域限制为5行高度；恢复竖屏控件并隐藏横屏布局
            if (orientationChanged)
            {
                LandscapeRoot.IsVisible = false;
                MainContent.IsVisible = true;
                BottomControlsRoot.IsVisible = true;
                TopNavBar.IsVisible = true;
                PhoneControls.IsVisible = true;
                DesktopControls.IsVisible = false;
                BottomActionBar.IsVisible = true;
                // 退出横屏时复位歌词模式，恢复信息区显示
                LandscapeTitleBlock.IsVisible = true;
                LandscapeToolsRow.IsVisible = true;
                LandscapeCurrentLyric.IsVisible = true;
                LandscapeLyricsScroll.IsVisible = false;
                _landscapeLyricsMode = false;
                // 若页面首次以横屏创建，竖屏封面 Handler 此时才就绪，重载一次确保高分辨率。
                _ = ReloadCoverHighResAsync(ArtworkImage);
            }
            // 5行歌词高度估算：约 200-220px
            RightHalf.HeightRequest = 200;
            MainContent.RowDefinitions = new RowDefinitionCollection
            {
                new RowDefinition { Height = GridLength.Star },
                new RowDefinition { Height = GridLength.Auto }
            };
            // 恢复竖屏 SafeArea
            if (orientationChanged)
            {
                ApplySafeArea();
                // 横屏→竖屏切换：竖屏 5 行歌词是重新显示的，Handler/行高需要重新测量，
                // 强制重测 + 钉回当前行（无缓动），避免停留在横屏时的位置。
                ForcePinCurrentLine();
                ScheduleRetriedPin(3);
            }
        }

        if (_lastCoverSize != coverSize)
        {
            _lastCoverSize = coverSize;
            CoverGlow.WidthRequest = coverSize;
            CoverGlow.HeightRequest = coverSize;
            CoverArea.WidthRequest = coverSize;
            CoverArea.HeightRequest = coverSize;
            ArtworkImage.WidthRequest = coverSize;
            ArtworkImage.HeightRequest = coverSize;
            // 横屏布局封面同步尺寸（LandscapeRoot 隐藏时设置也无害）
            LandscapeCover.WidthRequest = coverSize;
            LandscapeCover.HeightRequest = coverSize;
            LandscapeCoverImage.WidthRequest = coverSize;
            LandscapeCoverImage.HeightRequest = coverSize;
        }
    }

    /// <summary>
    /// 给播放页两张封面（ArtworkImage / LandscapeCoverImage）的原生 ImageView 打上标记，
    /// 使 Android 自定义图像服务（CachingFileImageSourceService）对它们一律按高分辨率桶解码，
    /// 避免隐藏态 0 尺寸预解码成 256px 低清图，从而让横屏与竖屏封面分辨率一致（源文件 1000px）。
    /// 该调用幂等，每次 OnAppearing 执行一次即可；Handler 未就绪时跳过，下次出现再补。
    /// </summary>
    private void TagPlayerCoverViews()
    {
#if ANDROID
        try
        {
            if (ArtworkImage.Handler?.PlatformView is Android.Widget.ImageView v1)
            {
                v1.Tag = CatClawMusic.Maui.Platforms.Android.CachingFileImageSourceService.PlayerCoverTag;
                v1.SetScaleType(Android.Widget.ImageView.ScaleType.CenterCrop);
            }
            if (LandscapeCoverImage.Handler?.PlatformView is Android.Widget.ImageView v2)
            {
                v2.Tag = CatClawMusic.Maui.Platforms.Android.CachingFileImageSourceService.PlayerCoverTag;
                v2.SetScaleType(Android.Widget.ImageView.ScaleType.CenterCrop);
            }
        }
        catch { /* Handler 未就绪时忽略，下次 OnAppearing 再补 */ }
#endif
    }

    /// <summary>
    /// 对播放页封面强制设置 Android 原生 scaleType=CenterCrop，保证封面恒定以
    /// 居中裁切铺满正方形容器（等价 AspectFill），彻底消除非方形封面上下/左右的
    /// 黑条。在线封面（Stream/Uri）不经过 CachingFileImageSourceService，
    /// 没有该服务内部的 CenterCrop 兜底，必须在此页面层统一强制。
    /// </summary>
    private static void EnforceCoverCenterCrop(Image image)
    {
#if ANDROID
        try
        {
            if (image.Handler?.PlatformView is Android.Widget.ImageView iv)
                iv.SetScaleType(Android.Widget.ImageView.ScaleType.CenterCrop);
        }
        catch { }
#else
        _ = image;
#endif
    }

    /// <summary>
    /// 封面图异步加载完成后 scaleType 可能被 MAUI 重置（尤其在线 URL 封面），
    /// 切歌 / 重绑后分多次延迟强制 CenterCrop，覆盖加载时序，确保跨路径恒定铺满。
    /// </summary>
    private void ScheduleCoverCenterCrop()
    {
#if ANDROID
        try
        {
            for (int i = 1; i <= 4; i++)
            {
                var delayMs = 120 * i;
                Dispatcher?.DispatchDelayed(TimeSpan.FromMilliseconds(delayMs), () =>
                {
                    EnforceCoverCenterCrop(ArtworkImage);
                    EnforceCoverCenterCrop(LandscapeCoverImage);
                });
            }
        }
        catch { }
#endif
    }

    /// <summary>
    /// 为指定封面 Image 强制使用高分辨率桶重新加载图片。
    /// 用于横屏/竖屏封面在布局切换后 Handler 才创建、导致首次解码分辨率不足的场景。
    /// </summary>
    private async Task ReloadCoverHighResAsync(Image image)
    {
#if ANDROID
        try
        {
            // 等待布局测量完成、Handler 与原生 ImageView 就绪
            await Task.Delay(50);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                try
                {
                    // 直接给原生 ImageView 打 Tag，确保后续解码走 PlayerCoverTargetPx
                    if (image.Handler?.PlatformView is Android.Widget.ImageView iv)
                        iv.Tag = CatClawMusic.Maui.Platforms.Android.CachingFileImageSourceService.PlayerCoverTag;

                    // 强制重新解码：先清空 Source（会破坏 XAML 的 OneWay 绑定），
                    // 再用 SetBinding 重新建立绑定。绑定立即求值，将 Source 设回
                    // ViewModel.CoverImage 的当前值，触发带 Tag 的高分辨率重新解码。
                    // 关键：必须用 SetBinding 而非直接赋值 Source，否则后续切歌时
                    // ViewModel.CoverImage 的变化无法传播到 Image（绑定已断开）。
                    image.Source = null;
                    image.SetBinding(Image.SourceProperty, new Binding(nameof(NowPlayingViewModel.CoverImage)));
                    // 重绑触发重新解码，同步强制 CenterCrop 覆盖在线封面路径
                    ScheduleCoverCenterCrop();
                }
                catch (Exception ex)
                {
                    Log.Debug("NowPlayingPage", $"[Cover] 高分辨率重载失败: {ex.Message}");
                }
            });
        }
        catch { }
#endif
    }

    /// <summary>当页面显示在屏幕上时触发，加载当前歌曲、构建歌词视图并启动进度定时器。</summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // 标记歌词表面可见 → AudioPlayerService 位置定时器切到 60fps（逐字歌词平滑着色）
        _playerSurfaceToken ??= Services.PlayerSurfaceTracker.Acquire();
        TagPlayerCoverViews();
        CrashReporter.MarkStage("NowPlayingPage.OnAppearing: 开始");
#if WINDOWS
        Shell.SetNavBarIsVisible(this, false);
#endif
        ApplySafeArea();
        await _viewModel.LoadCurrentSongAsync();
        CrashReporter.MarkStage("NowPlayingPage.OnAppearing: LoadCurrentSongAsync 完成");

        if (_viewModel.Duration > 0)
            ProgressSlider.Maximum = _viewModel.Duration;

        // 切页重建或滑块被重置后，立即把当前进度同步到滑块：
        // 若 Progress 值恰与前值相同（未变化），不会触发 PropertyChanged，
        // 滑块会停在 XAML 初始值 0，表现为「进度条归零」。这里强制同步一次。
        if (_viewModel.Progress > 0)
            ProgressSlider.Value = _viewModel.Progress;

        // 横屏布局滑块同步（与竖屏 ProgressSlider 保持一致）
        if (_viewModel.Duration > 0)
            LandscapeProgressSlider.Maximum = _viewModel.Duration;
        if (_viewModel.Progress > 0)
            LandscapeProgressSlider.Value = _viewModel.Progress;

        Application.Current!.RequestedThemeChanged += OnThemeChanged;

#if WINDOWS
        // Windows 桌面端：构建 WindowsStage 歌词视图、同步滑块、初始化音量与图标
        OnWindowsStageReady();
        CrashReporter.MarkStage("NowPlayingPage.OnAppearing: Windows 歌词视图构建完成");
#else
        // 仅在歌词行数变化时重建视图，避免切页时大量控件销毁/重建
        var allLines = _viewModel.AllLyricLines;
        if (allLines == null || _lyricLabels.Count != allLines.Count)
            BuildLyricViews();
        else if (_viewModel.CurrentLyricIndexObservable >= 0 && _lyricLabels.Count > 0)
            HighlightLine(_viewModel.CurrentLyricIndexObservable);
        CrashReporter.MarkStage("NowPlayingPage.OnAppearing: 歌词视图构建完成");

        // 进入播放页：立即钉一次当前行（可能是后台期间切歌 / 横竖屏切换了，位置不准），
        // 随后每隔 300ms 再补钉 3 次，覆盖 Handler/PlatformView 在 OnAppearing 之后才就绪的场景。
        ForcePinCurrentLine();
        ScheduleRetriedPin(3);
#endif

        // 整个进入播放页流程无异常完成，清除阶段标记（若此后再崩，说明是后续交互，非进入阶段）
        CrashReporter.ClearStage();
    }

    /// <summary>当页面从屏幕上消失时触发，取消订阅主题变更事件。</summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // 歌词表面不再可见 → 位置定时器降频到 5Hz，把主线程让给当前页面
        _playerSurfaceToken?.Dispose();
        _playerSurfaceToken = null;
        Application.Current!.RequestedThemeChanged -= OnThemeChanged;
    }

    /// <summary>当系统主题发生变更时触发，在主线程上重建歌词视图以应用新主题颜色。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">主题变更事件参数。</param>
    private void OnThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
#if WINDOWS
        MainThread.BeginInvokeOnMainThread(BuildWindowsLyricViews);
#else
        MainThread.BeginInvokeOnMainThread(BuildLyricViews);
#endif
    }

#if !WINDOWS
    // WindowsStage 桌面面板在安卓端不可见；这些处理器仅为满足共享 XAML 的事件绑定编译。
    private void OnWinPlayTapped(object? sender, TappedEventArgs e)
        => _viewModel.TogglePlayPauseCommand.Execute(null);

    private void OnWinLikeTapped(object? sender, TappedEventArgs e)
        => _viewModel.ToggleLikeCommand.Execute(null);

    private void OnWinLyricSettingsTapped(object? sender, TappedEventArgs e) { }

    private void OnWinVolumeChanged(object? sender, ValueChangedEventArgs e) { }

    private void OnWinMuteTapped(object? sender, EventArgs e) { }
#endif

    /// <summary>当视图模型属性变更时触发，根据变更的属性重建歌词视图或更新高亮行。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">属性变更事件参数。</param>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 横竖屏切换时旧页面被销毁但订阅未取消，UI 控件可能已为 null。
        // try-catch 防止 NullReferenceException 阻断多播委托调用链，
        // 否则后续的绑定系统 handler 不会被调用，导致封面等绑定不更新。
        try
        {
#if ANDROID
            // PlatformView 临时分离时（ViewPager2 离屏页）跳过处理但不取消订阅：
            // 取消订阅会导致返回页面后歌词不再更新（订阅不会自动恢复）。
            // 永久分离（横竖屏切换 Handler=null）由 HandlerChanged 事件统一处理取消订阅。
            if (Handler?.PlatformView is Android.Views.View av && !av.IsAttachedToWindow)
            {
                return;
            }
#endif
#if WINDOWS
            // Windows 桌面端：所有歌词/进度/播放状态变更路由到 WindowsStage 控件
            switch (e.PropertyName)
            {
                case nameof(NowPlayingViewModel.AllLyricLines):
                case nameof(NowPlayingViewModel.HasLyrics):
                    MainThread.BeginInvokeOnMainThread(BuildWindowsLyricViews);
                    return;
                case nameof(NowPlayingViewModel.CurrentLyricIndexObservable):
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        var idx = _viewModel.CurrentLyricIndexObservable;
                        // 恒为跟随模式：切行直接高亮 + 滚动跟随
                        HighlightWindowsLine(idx);
                        // 切行瞬间立即同步新行的逐字进度（VM 已算好，无需等下一个 tick）
                        if (idx >= 0 && idx < _winRows.Count)
                        {
                            var row = _winRows[idx];
                            row.Main.FillProgress = -1;   // 强制刷新（防同值漏触发）
                            row.Main.FillProgress = _viewModel.CurrentLineFillProgress;
                        }
                    });
                    return;
                case nameof(NowPlayingViewModel.CurrentLineFillProgress):
                    // 逐字填充：当前行 KaraokeLabel 按字符比例着色（已唱=白，未唱=灰）
                    {
                        var idx = _viewModel.CurrentLyricIndexObservable;
                        var fill = _viewModel.CurrentLineFillProgress;
                        if (idx >= 0 && idx < _winRows.Count)
                        {
                            // 强制刷新：先设 -1 再设目标值，防 PropertyChanged 同值漏触发
                            var row = _winRows[idx];
                            row.Main.FillProgress = -1;
                            row.Main.FillProgress = fill;
                        }
                        return;
                    }
                case nameof(NowPlayingViewModel.IsPlaying):
                    MainThread.BeginInvokeOnMainThread(OnWinPlayingChanged);
                    return;
                case nameof(NowPlayingViewModel.IsLiked):
                    MainThread.BeginInvokeOnMainThread(UpdateWinLikeIcon);
                    return;
                case nameof(NowPlayingViewModel.Duration):
                    {
                        var duration = _viewModel.Duration;
                        var max = duration > 1 ? duration : 1;
                        if (WinProgressSlider.Maximum != max)
                            WinProgressSlider.Maximum = max;
                        return;
                    }
                case nameof(NowPlayingViewModel.Progress) when !_isDragging:
                    {
                        var progress = _viewModel.Progress;
                        if (progress == 0)
                        {
                            WinProgressSlider.Value = 0;
                            return;
                        }
                        var duration = _viewModel.Duration;
                        if (duration > 1 && Math.Abs(WinProgressSlider.Value - progress) > 0.5)
                            WinProgressSlider.Value = progress;
                        return;
                    }
            }
            return;
#endif
            if (e.PropertyName == nameof(NowPlayingViewModel.AllLyricLines) ||
                e.PropertyName == nameof(NowPlayingViewModel.HasLyrics))
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_isLandscape && _landscapeLyricsMode)
                        BuildLandscapeLyricViews();
                    else
                        BuildLyricViews();
                });
                return;
            }

            if (e.PropertyName == nameof(NowPlayingViewModel.CurrentLyricIndexObservable))
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_isLandscape && _landscapeLyricsMode)
                        HighlightLandscapeLine(_viewModel.CurrentLyricIndexObservable);
                    else
                        HighlightLine(_viewModel.CurrentLyricIndexObservable);
                });
                return;
            }

            // 逐字填充进度变化：直接更新当前行 KaraokeLabel 的 FillProgress
            // PropertyChanged 已在主线程触发，无需额外 dispatch
            if (e.PropertyName == nameof(NowPlayingViewModel.CurrentLineFillProgress))
            {
                var idx = _viewModel.CurrentLyricIndexObservable;
                if (_isLandscape && _landscapeLyricsMode)
                {
                    if (idx >= 0 && idx < _landscapeLyricLabels.Count)
                        _landscapeLyricLabels[idx].FillProgress = _viewModel.CurrentLineFillProgress;
                }
                else if (idx >= 0 && idx < _lyricLabels.Count)
                    _lyricLabels[idx].FillProgress = _viewModel.CurrentLineFillProgress;
                return;
            }

            // 直接响应 ViewModel 的 Progress/Duration 变化，替代冗余的 500ms UI 定时器
            if (e.PropertyName == nameof(NowPlayingViewModel.Duration))
            {
                var duration = _viewModel.Duration;
                // 即使 duration<=1（数据库时长未知）也要更新 Maximum，
                // 否则切歌时滑块保留上一首的 Maximum，进度条显示异常。
                var max = duration > 1 ? duration : 1;
                if (ProgressSlider.Maximum != max)
                {
                    ProgressSlider.Maximum = max;
                    LandscapeProgressSlider.Maximum = max;
                }
            }

            if (e.PropertyName == nameof(NowPlayingViewModel.Progress) && !_isDragging)
            {
                var progress = _viewModel.Progress;
                // 切歌时 Progress 被重置为 0：无论新歌时长是否已知，都必须把滑块归零，
                // 否则滑块停在上一首的结束位置，表现为「进度条未重建」。
                if (progress == 0)
                {
                    ProgressSlider.Value = 0;
                    LandscapeProgressSlider.Value = 0;
                    return;
                }
                var duration = _viewModel.Duration;
                if (duration > 1 && Math.Abs(ProgressSlider.Value - progress) > 0.5)
                {
                    ProgressSlider.Value = progress;
                    LandscapeProgressSlider.Value = progress;
                }
            }

            if (e.PropertyName == nameof(NowPlayingViewModel.CoverImage))
            {
                // 切歌时封面新图异步加载，在线封面（Clip/URI）无自定义服务的 CenterCrop
                // 兜底，这里分多次延迟强制 CenterCrop，确保非方形封面不露黑条。
                ScheduleCoverCenterCrop();
                return;
            }
        }
        catch { /* 页面已销毁，忽略 */ }
    }

    private void OnSliderDragStarted(object? sender, EventArgs e)
    {
        _isDragging = true;
        _viewModel.OnSeekStarted();
    }

    /// <summary>当用户结束拖动进度条时触发，通知视图模型定位到拖动结束位置。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnSliderDragCompleted(object? sender, EventArgs e)
    {
        _isDragging = false;
        // 读取触发拖拽的滑块自身的值（竖屏/横屏共用同一处理程序）
        var slider = sender as Slider ?? ProgressSlider;
        await _viewModel.OnSeekCompleted(slider.Value);
    }

    private static string FormatTime(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
    }

    /// <summary>点击页面空白区域时触发，若点击位于封面、歌词或无歌词提示区域则切换到全屏歌词页。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">点击事件参数。</param>
    private void OnPageTapped(object? sender, TappedEventArgs e)
    {
#if WINDOWS
        // Windows 端通过独立的歌词按钮/Chip 进入歌词页，不响应整页点击
        return;
#else
        var ptCover = e.GetPosition(CoverArea);
        if (ptCover.HasValue && ptCover.Value.X >= -10 && ptCover.Value.X <= CoverArea.Width + 10
            && ptCover.Value.Y >= -10 && ptCover.Value.Y <= CoverArea.Height + 10)
        {
            GoToFullLyrics();
            return;
        }

        if (LyricsContainer.IsVisible)
        {
            var pt = e.GetPosition(LyricsContainer);
            if (pt.HasValue && pt.Value.X >= 0 && pt.Value.X <= LyricsContainer.Width
                && pt.Value.Y >= 0 && pt.Value.Y <= LyricsContainer.Height)
            {
                GoToFullLyrics();
                return;
            }
        }

        if (NoLyricsLabel.IsVisible)
        {
            var pt = e.GetPosition(NoLyricsLabel);
            if (pt.HasValue && pt.Value.X >= -20 && pt.Value.X <= NoLyricsLabel.Width + 20
                && pt.Value.Y >= -10 && pt.Value.Y <= NoLyricsLabel.Height + 10)
            {
                GoToFullLyrics();
            }
        }
#endif
    }

    /// <summary>跳转到全屏歌词页：移动端走 ViewPager 切换，桌面端嵌入全屏歌词页</summary>
    private static void GoToFullLyrics()
    {
#if WINDOWS
        // 桌面无 Shell：嵌入 FullLyricsPage（若处于 PlayerOverlay 覆盖层则 MainArea 被遮挡，静默跳过）
        if (DesktopNavigation.TryGoToShell("//fullyrics")) return;
        DesktopNavigation.GoOrEmbed("fullyrics", typeof(FullLyricsPage));
#else
        MainPage.Instance?.SwitchToFullLyrics();
#endif
    }

    /// <summary>点击右上角收起按钮：播放页向下平移收起，露出发现页</summary>
    private void OnCollapseButtonTapped(object? sender, TappedEventArgs e)
    {
#if WINDOWS
        var shell = DesktopNavigation.TryGetShell();
        if (shell != null && shell.Navigation.NavigationStack.Count > 1)
            _ = shell.Navigation.PopAsync();
        else if (shell != null)
            _ = shell.GoToAsync("//main");
        else
            DesktopNavigation.ClosePlayerOverlay(); // 桌面无 Shell：关闭播放页覆盖层
#else
        MainPage.Instance?.CollapseNowPlaying();
#endif
    }

    /// <summary>
    /// 横屏布局右上角收起按钮：
    /// DesktopMainPage 为 Shell 根页面，播放页经 PushAsync 入栈，PopAsync 返回即可。
    /// 普通手机竖屏（播放页为 MainPage 内浮层）则收起播放页回到发现页。
    /// </summary>
    private void OnLandscapeCollapseTapped(object? sender, TappedEventArgs e)
    {
#if ANDROID || WINDOWS
        var shell = DesktopNavigation.TryGetShell();
        if (shell != null && shell.Navigation.NavigationStack.Count > 1)
            _ = shell.Navigation.PopAsync();
        else if (shell != null)
            _ = shell.GoToAsync("//main");
        else
            DesktopNavigation.ClosePlayerOverlay(); // 桌面无 Shell：关闭播放页覆盖层
#else
        MainPage.Instance?.CollapseNowPlaying();
#endif
    }

    /// <summary>点击歌词按钮：横屏下 PushAsync 推入 FullLyricsPage（复用竖屏歌词滚动逻辑），竖屏下走 ViewPager 切换</summary>
    private void OnOpenLyricsClicked(object? sender, EventArgs e)
    {
        if (_isLandscape)
        {
            var fullLyricsPage = MauiProgram.Services.GetRequiredService<Pages.FullLyricsPage>();
            DesktopNavigation.PushEmbed(fullLyricsPage);
        }
        else
            GoToFullLyrics();
    }

    // ═══════════════════════════════════════
    // 横屏歌词模式（就地显示多行歌词，不跳独立页面）
    // ═══════════════════════════════════════

    /// <summary>切换横屏歌词模式：开 → 收起信息区、显示多行歌词、封面加大；关 → 恢复信息区</summary>
    private void OnSongDetailTapped(object? sender, EventArgs e)
    {
        var song = _viewModel.CurrentSong;
        if (song == null || song.Id <= 0) return;

        if (DesktopNavigation.TryGoToShell($"songdetail?songId={song.Id}")) return;
        DesktopNavigation.OpenSongDetail(song.Id.ToString());
    }
    /// <summary>点击播放列表按钮：弹出播放队列弹窗</summary>
    private void OnOpenPlaylistClicked(object? sender, EventArgs e)
    {
        BuildPlaylistPopupContent();
        PlaylistPopup.Open();

        // 延迟滚动到当前歌曲
        _ = Task.Delay(300).ContinueWith(_ =>
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    var currentSong = _viewModel.CurrentSong;
                    if (currentSong != null && _playlistCollectionView != null)
                    {
                        var songs = _viewModel.GetQueueSongs();
                        var idx = songs.ToList().FindIndex(s => s.Id == currentSong.Id);
                        if (idx >= 0)
                            _playlistCollectionView.ScrollTo(idx, position: ScrollToPosition.Center, animate: false);
                    }
                }
                catch { }
            }));
    }

    private CollectionView? _playlistCollectionView;

    /// <summary>构建播放列表弹窗内容：歌曲列表 + 每项可点击播放/滑动删除</summary>
    private void BuildPlaylistPopupContent()
    {
        PlaylistPopup.ClearContent();

        var songs = _viewModel.GetQueueSongs();
        var currentSong = _viewModel.CurrentSong;
        var primaryColor = (Color)Application.Current!.Resources["PrimaryColor"];
        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        var textSecondary = (Color)Application.Current!.Resources["TextSecondaryColor"];
        var textHint = (Color)Application.Current!.Resources["TextHintColor"];

        // 歌曲数量标签
        var countLabel = new Label
        {
            Text = $"{songs.Count} 首歌曲",
            FontSize = 13,
            TextColor = textHint,
            Margin = new Thickness(0, 0, 0, 12)
        };
        PlaylistPopup.AddContent(countLabel);

        // 歌曲列表 CollectionView（高度限制 400，可滚动）
        _playlistCollectionView = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            HeightRequest = Math.Min(songs.Count * 56, 400),
            VerticalScrollBarVisibility = ScrollBarVisibility.Default,
            ItemsSource = songs.ToList(),
            ItemTemplate = new DataTemplate(() =>
            {
                var grid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitionCollection
                    {
                        new() { Width = GridLength.Auto },       // 播放指示器
                        new() { Width = new GridLength(1, GridUnitType.Star) }, // 歌曲信息
                        new() { Width = GridLength.Auto }         // 删除按钮
                    },
                    HeightRequest = 52,
                    Padding = new Thickness(0, 4),
                    ColumnSpacing = 10
                };

                // 播放指示器（当前歌曲显示小图标）
                var indicator = new Image
                {
                    WidthRequest = 16,
                    HeightRequest = 16,
                    Aspect = Aspect.AspectFit,
                    Source = ImageSourceHelper.FromNameOriginal("ic_play_dark"),
                    IsVisible = false,
                    VerticalOptions = LayoutOptions.Center
                };
                grid.Add(indicator, 0);

                // 歌曲信息
                var infoStack = new VerticalStackLayout
                {
                    Spacing = 2,
                    VerticalOptions = LayoutOptions.Center
                };
                var titleLabel = new Label
                {
                    FontSize = 14,
                    FontFamily = "OpenSansSemibold",
                    MaxLines = 1,
                    LineBreakMode = LineBreakMode.TailTruncation,
                    VerticalOptions = LayoutOptions.Center
                };
                titleLabel.SetBinding(Label.TextProperty, "Title");
                var artistLabel = new Label
                {
                    FontSize = 12,
                    TextColor = textSecondary,
                    MaxLines = 1,
                    LineBreakMode = LineBreakMode.TailTruncation
                };
                artistLabel.SetBinding(Label.TextProperty, "Artist");
                infoStack.Children.Add(titleLabel);
                infoStack.Children.Add(artistLabel);
                grid.Add(infoStack, 1);

                // 删除按钮
                var removeBtn = new ImageButton
                {
                    WidthRequest = 32,
                    HeightRequest = 32,
                    CornerRadius = 16,
                    Padding = 6,
                    Aspect = Aspect.AspectFit,
                    BackgroundColor = Colors.Transparent,
                    Source = ImageSourceHelper.FromNameOriginal("ic_close"),
                    VerticalOptions = LayoutOptions.Center
                };
                grid.Add(removeBtn, 2);

                // 绑定上下文加载后设置当前歌曲高亮
                grid.BindingContextChanged += (s, _) =>
                {
                    if (s is Grid g && g.BindingContext is Song song)
                    {
                        var isCurrent = currentSong != null && song.Id == currentSong.Id;
                        titleLabel.TextColor = isCurrent ? primaryColor : textPrimary;
                        indicator.IsVisible = isCurrent;
                        if (isCurrent)
                            titleLabel.FontAttributes = FontAttributes.Bold;
                    }
                };

                // 点击播放
                var tapGesture = new TapGestureRecognizer();
                tapGesture.Tapped += (_, _) =>
                {
                    if (grid.BindingContext is Song song)
                    {
                        _ = PlaylistPopup.CloseAsync();
                        _ = _viewModel.PlaySongFromQueueCommand.ExecuteAsync(song);
                    }
                };
                grid.GestureRecognizers.Add(tapGesture);

                // 删除按钮点击
                removeBtn.Clicked += (_, _) =>
                {
                    if (grid.BindingContext is Song song)
                    {
                        _ = _viewModel.RemoveSongFromQueueCommand.ExecuteAsync(song);
                        // 刷新列表
                        BuildPlaylistPopupContent();
                    }
                };

                return grid;
            })
        };
        _playlistCollectionView.Behaviors.Add(new Controls.ScrollPerformanceBehavior());
        PlaylistPopup.AddContent(_playlistCollectionView);
    }

#if WINDOWS
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_MINIMIZE = 6;
#else
    // 桌面歌词按钮仅 Windows 存在（XAML 已 OnPlatform 隐藏），Android 空实现兜底防 XamlC XC0002
    private void OnWinDesktopLyricTapped(object? sender, EventArgs e) { }
#endif

    // === FM 模式选择抽屉 ===

    /// <summary>VM 加载完模式列表后触发：动态构建抽屉内容并打开</summary>
    private void OnFmModeDrawerRequested(List<FmModeCategory> categories, string currentLabel)
    {
        Log.Debug("NowPlaying", $"[FM] drawer requested: categories={categories?.Count ?? 0}, current={currentLabel}");
        MainThread.BeginInvokeOnMainThread(() =>
        {
            BuildFmModeSheetContent(categories, currentLabel);
            Log.Debug("NowPlaying", $"[FM] sheet content built, opening...");
            FmModeSheet.Open();
            Log.Debug("NowPlaying", $"[FM] sheet Open() returned, IsVisible={FmModeSheet.IsVisible}");
        });
    }

    /// <summary>构建 FM 模式抽屉内容：顶部 3 个推荐模式卡 + 下方场景模式 4 列网格</summary>
    private void BuildFmModeSheetContent(List<FmModeCategory> categories, string currentLabel)
    {
        FmModeSheet.ClearContent();

        var container = new VerticalStackLayout { Spacing = 16, Padding = new Thickness(0, 4, 0, 0) };

        // 标题
        container.Children.Add(new Label
        {
            Text = "私人漫游模式",
            FontSize = 16, FontAttributes = FontAttributes.Bold,
            TextColor = (Color)Application.Current!.Resources["TextPrimaryColor"],
            HorizontalOptions = LayoutOptions.Start,
        });

        var modes = categories.Where(c => c.Type == "mode").ToList();
        var scenes = categories.Where(c => c.Type == "scene").ToList();

        // 推荐模式（3 个横排卡片）
        if (modes.Count > 0)
        {
            container.Children.Add(new Label
            {
                Text = "推荐模式",
                FontSize = 12,
                TextColor = (Color)Application.Current.Resources["TextSecondaryColor"],
                HorizontalOptions = LayoutOptions.Start,
            });
            var modeRow = new HorizontalStackLayout { Spacing = 10 };
            foreach (var m in modes)
            {
                var isCurrent = m.Title == currentLabel;
                var card = CreateFmModeCard(m, isCurrent);
                modeRow.Children.Add(card);
            }
            container.Children.Add(modeRow);
        }

        // 场景模式（4 列网格）
        if (scenes.Count > 0)
        {
            container.Children.Add(new Label
            {
                Text = "场景模式",
                FontSize = 12,
                Margin = new Thickness(0, 8, 0, 0),
                TextColor = (Color)Application.Current.Resources["TextSecondaryColor"],
                HorizontalOptions = LayoutOptions.Start,
            });
            int cols = 4;
            int rows = (int)Math.Ceiling(scenes.Count / (double)cols);
            var grid = new Grid
            {
                RowSpacing = 10,
                ColumnSpacing = 10,
            };
            for (int r = 0; r < rows; r++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < cols; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            for (int i = 0; i < scenes.Count; i++)
            {
                var s = scenes[i];
                var isCurrent = s.Title == currentLabel;
                var chip = CreateFmSceneChip(s, isCurrent);
                grid.Add(chip, i % cols, i / cols);
            }
            container.Children.Add(grid);
        }

        FmModeSheet.AddContent(container);
    }

    /// <summary>推荐模式卡（带标题+副标题+图标，选中高亮）</summary>
    private Border CreateFmModeCard(FmModeCategory m, bool isCurrent)
    {
        var border = new Border
        {
            Padding = new Thickness(14, 10),
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            StrokeThickness = isCurrent ? 2 : 1,
            Stroke = isCurrent ? Color.FromArgb("#f953c6") : (Color)Application.Current!.Resources["GlassStrokeColor"]!,
            BackgroundColor = isCurrent ? Color.FromArgb("#1Af953c6") : (Color)Application.Current.Resources["CardBackgroundColor"]!,
            HorizontalOptions = LayoutOptions.Fill,
        };
        border.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                await _viewModel.SelectFmModeAsync(m.Code);
                await FmModeSheet.CloseAsync();
            }),
        });
        var stack = new VerticalStackLayout { Spacing = 4 };
        stack.Children.Add(new Label { Text = $"{m.Icon} {m.Title}", FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = (Color)Application.Current.Resources["TextPrimaryColor"]! });
        if (!string.IsNullOrEmpty(m.SubTitle))
            stack.Children.Add(new Label { Text = m.SubTitle, FontSize = 10, TextColor = (Color)Application.Current.Resources["TextSecondaryColor"]!, MaxLines = 2 });
        border.Content = stack;
        return border;
    }

    /// <summary>场景模式 Chip（圆角小卡片，选中高亮）</summary>
    private Border CreateFmSceneChip(FmModeCategory s, bool isCurrent)
    {
        var border = new Border
        {
            Padding = new Thickness(10, 8),
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            StrokeThickness = isCurrent ? 2 : 0,
            Stroke = isCurrent ? Color.FromArgb("#f953c6") : Colors.Transparent,
            BackgroundColor = isCurrent ? Color.FromArgb("#1Af953c6") : (Color)Application.Current!.Resources["CardBackgroundColor"]!,
            HorizontalOptions = LayoutOptions.Fill,
        };
        border.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                await _viewModel.SelectFmModeAsync(s.Code);
                await FmModeSheet.CloseAsync();
            }),
        });
        border.Content = new Label
        {
            Text = s.Title,
            FontSize = 12,
            FontAttributes = isCurrent ? FontAttributes.Bold : FontAttributes.None,
            TextColor = (Color)Application.Current.Resources["TextPrimaryColor"]!,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        return border;
    }
}
