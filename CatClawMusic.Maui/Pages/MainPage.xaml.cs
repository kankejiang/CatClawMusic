using System.Reflection;
using System.Linq;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.ViewModels;
using System.ComponentModel;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Devices;
using Microsoft.Maui.ApplicationModel;

#if ANDROID
// 类型别名，避免与 MAUI 的 Microsoft.Maui.Controls.View 命名冲突
using AView = Android.Views.View;
// 引入 Android 平台扩展（SetHardwareLayer），仅 Android 编译单元可用
using CatClawMusic.Maui.Platforms.Android;
#endif

namespace CatClawMusic.Maui.Pages;

/// <summary>
/// 主页面：包含 4 个 tab 页面的 ViewPager + 自定义底部 TabBar + 迷你播放器。
/// 所有 tab 页面水平排列在同一 Grid 中，滑动时同时移动，实现连续平滑切换。
/// </summary>
public partial class MainPage : ContentPage
{
    private readonly IServiceProvider _services;
    private readonly List<ContentPage> _tabPages = new();
    private readonly NowPlayingViewModel _nowPlayingVm;
    private readonly double _discoverBaseBottomPadding;
    // 发现页实例，用于随迷你播放器/TabBar 显隐联动调整其滚动内容底部预留，避免推荐艺人被悬浮条遮挡
    private SearchPage? _searchPage;
    private readonly SearchViewModel? _searchVm;
    private readonly Services.IInteractionStateService? _interactionState;
    // ViewPager 布局: [FullLyrics(0), NowPlaying(1), Search(2), Playlist(3), Library(4)]
    // TabBar 的 4 个按钮对应 index 1-4，index 0 是全屏歌词（无 Tab 按钮）
    private int _currentIndex = 1;
    private double _panTotalX;
    private bool _isPanning;
    private bool _directionLocked;
    private bool _isFirstLoad = true;
    // 用于减少 SafeArea/TabBar 更新时的重复重排
    private Thickness _lastViewPagerPadding = new(-1);
    private Thickness _lastTabBarPadding = new(-1);
    private double _lastTabBarHeight = -1;
    private bool _lastMiniPlayerVisible;
    private const double SwipeThresholdRatio = 0.25;
    private const int AnimDuration = 280;
    private const int BounceDuration = 200;
    private System.Timers.Timer? _panWatchdogTimer;
    private DateTime _lastPanRunningTime;
    private IDisposable? _panInteractionToken;
#if ANDROID
    // 原生 ViewPager2 分页容器（仅 Android 使用；Windows 走手动 TranslationX）
    private NativeTabPager? _nativePager;
    // 滑动期间暂停 FrostedBackground 动画的交互令牌（对应原生 OnPageScrollStateChanged）
    private IDisposable? _swipeInteractionToken;
#endif
    /// <summary>方向判定阈值：位移超过此值才判定水平/垂直方向（像素），避免手指微颤误触</summary>
    private const double DirectionLockThreshold = 14;
    /// <summary>水平倾斜比：|TotalX| 必须达到 |TotalY| 的此倍数才判定为水平滑动，否则视为垂直滚动</summary>
    private const double HorizontalRatio = 1.4;
    /// <summary>看门狗间隔：手指停顿超过此毫秒数且无新 Running 事件时，强制弹回当前页，防止页面卡在中间</summary>
    private const int PanWatchdogInterval = 400;
    // ═══ 横屏桌面舞台：Android 横屏时 MainPage 借入 DesktopMainPage 的桌面布局（与 Windows 同界面）═══
    private DesktopMainPage? _desktopPage;
    private Grid? _desktopRoot;
    private bool _desktopActive;

    /// <summary>全局实例，供外部调用 SwitchToTab</summary>
    public static MainPage? Instance { get; private set; }

    /// <summary>待切换的 tab 索引（从子页面返回时使用）</summary>
    public static int? PendingTabIndex;

    /// <summary>初始化 <see cref="MainPage"/> 类的新实例，构建各 Tab 页面并绑定迷你播放器。</summary>
    /// <param name="services">服务提供程序，用于解析各 Tab 页面及其依赖。</param>
    /// <param name="nowPlayingVm">当前播放视图模型，用于驱动迷你播放器。</param>
    public MainPage(IServiceProvider services, NowPlayingViewModel nowPlayingVm)
    {
        InitializeComponent();
        _services = services;

        // .NET MAUI 11 / CoreCLR：Layout 默认 SafeAreaEdges=Container，会在底部（及顶部）套系统栏内缩，
        // 使自定义 TabBar 离屏幕底部一截、下方露出空白。Mono 时代无此默认，升级 .NET 11 后才出现。
        // 用代码显式设为 None 让页面真正边缘到边；不依赖 XAML（Release 的 SourceGen 膨胀器可能丢弃该属性）。
        ApplyEdgeToEdgeSafeArea();
        _nowPlayingVm = nowPlayingVm;
        _interactionState = services.GetService<Services.IInteractionStateService>();
        Instance = this;

        // 迷你播放器绑定到 NowPlayingViewModel
        MiniPlayer.BindingContext = _nowPlayingVm;
        _searchVm = services.GetService<SearchViewModel>();

        SetupPages();
        // 初始即应用底部预留（TabBar 常驻显隐由 UpdateSafeAreaPadding 维护，此处随页就绪联动一次）
        UpdateDiscoverBottomReserve();
        ViewPagerGrid.SizeChanged += OnViewPagerSizeChanged;

        // 原生旋转：页面尺寸变化（横竖屏）时切换 chrome（左侧图标导航栏 ↔ 底部 TabBar），
        // 不换根页面——tab/页面状态全部原地保留
        SizeChanged += OnMainPageSizeChanged;

        // 静态/单例事件：通过 HandlerChanged 管理订阅生命周期，支持页面实例复用（Singleton）。
        // 页面挂载时订阅、分离时取消，并释放交互令牌防止 IsUserInteracting 卡死。
        HandlerChanged += (_, _) =>
        {
            if (Handler == null)
            {
                // 页面分离：取消订阅并释放交互令牌
                _nowPlayingVm.PropertyChanged -= OnNowPlayingPropertyChanged;
                _searchVm!.PropertyChanged -= OnSearchVmPropertyChanged;
                SafeAreaHelper.SafeAreaChanged -= OnSafeAreaChanged;
#if ANDROID
                _swipeInteractionToken?.Dispose();
                _swipeInteractionToken = null;
#endif
                _panInteractionToken?.Dispose();
                _panInteractionToken = null;
            }
            else
            {
                // 页面挂载（或重新挂载）：订阅静态/单例事件
                _nowPlayingVm.PropertyChanged -= OnNowPlayingPropertyChanged;
                _nowPlayingVm.PropertyChanged += OnNowPlayingPropertyChanged;
                if (_searchVm != null)
                {
                    _searchVm.PropertyChanged -= OnSearchVmPropertyChanged;
                    _searchVm.PropertyChanged += OnSearchVmPropertyChanged;
                }
                SafeAreaHelper.SafeAreaChanged -= OnSafeAreaChanged;
                SafeAreaHelper.SafeAreaChanged += OnSafeAreaChanged;
            }
        };

    }

    /// <summary>创建 5 个页面（全屏歌词 + 4 个 tab），按平台选择承载方式：
    /// Android 用原生 ViewPager2（GPU 合成水平滑动，根治 net10 抽搐）；
    /// Windows 走手动 TranslationX + 懒加载兜底。</summary>
    private void SetupPages()
    {
        var pages = new ContentPage[]
        {
            _services.GetRequiredService<FullLyricsPage>(),
            _services.GetRequiredService<NowPlayingPage>(),
            _services.GetRequiredService<SearchPage>(),
            _services.GetRequiredService<PlaylistPage>(),
            _services.GetRequiredService<LibraryPage>(),
        };
        // 复用 SetupPages 创建的实例（SearchPage 为 AddTransient，直接再解析会得到不同实例）
        _searchPage = pages.OfType<SearchPage>().FirstOrDefault();

#if ANDROID
        // ── 原生 ViewPager2 路径 ──
        // 直接承载完整 MAUI 页：Page.ToPlatform 产出的 ContentViewGroup 自带 measure，
        // 能被 ViewPager2 正确测量布局，无需把页 Content 抽出来塞进 MAUI 容器。
        // 水平分页位移由 Android 渲染管线 GPU 合成，彻底摆脱 MAUI 的 TranslationX 重绘。
        foreach (var page in pages)
        {
            _tabPages.Add(page);
            // 非全屏页面（发现/歌单/音乐库，index>1）自行预留状态栏高度；
            // 全屏页面（歌词/播放页，index 0/1）保持边缘到边。
            var pageIndex = _tabPages.Count - 1;
            if (pageIndex > 1 && page.Content is Layout layout)
                layout.Behaviors.Add(new SafeAreaPaddingBehavior());
        }

        _nativePager = new NativeTabPager
        {
            Pages = _tabPages,
            CurrentItem = _currentIndex,
        };
        _nativePager.PageSelected += (s, e) => OnNativePageSelected(e);
        _nativePager.ScrollStateChanged += (s, e) => OnNativeScrollStateChanged(e);

        // 放入 ViewPagerGrid（BlurHostView 的内容），让 BlurHostView 能捕获到 NativeTabPager
        // 的实际内容用于真毛玻璃。原实现插入 rootGrid 且 ViewPagerGrid 隐藏，导致
        // BlurHostView 捕获的是空容器，毛玻璃只剩纯色 tint。
        var pagerHost = new Grid { IsClippedToBounds = true };
        pagerHost.Children.Add(_nativePager);
        ViewPagerGrid.Children.Add(pagerHost);
        // Android 下页面滑动由 NativeTabPager 处理，移除 ViewPagerGrid 的 Pan 手势避免冲突
        ViewPagerGrid.GestureRecognizers.Clear();
#else
        // ── 手动 TranslationX 路径（Windows 兜底）──
        foreach (var page in pages)
        {
            _tabPages.Add(page);
            var content = page.Content;
            page.Content = null;
            content.BindingContext = page.BindingContext;

            ForceVerticalScroll(content);
            AddPanToLayouts(content, OnPanUpdated);

            // 非全屏页面（发现/歌单/音乐库，index>1）自行预留状态栏高度，
            // 不再依赖 ViewPagerGrid 的 top padding（更健壮：即使 grid padding 未生效也不侵入状态栏）。
            // 全屏页面（歌词/播放页，index 0/1）保持边缘到边，背景延伸到状态栏下。
            var pageIndex = _tabPages.Count - 1;
            if (pageIndex > 1 && content is Layout layout)
                layout.Behaviors.Add(new SafeAreaPaddingBehavior());

            ViewPagerGrid.Children.Add(content);
        }

        // 懒加载：仅当前页±1 可见，远页隐藏（保留 handler，滚动位置不丢）
        UpdatePageVisibility();
#endif
    }

#if ANDROID
    /// <summary>原生 ViewPager2 选中页变化：更新当前索引、生命周期、TabBar。</summary>
    private void OnNativePageSelected(int index)
    {
        if (index == _currentIndex) return;
        InvokeLifecycle(_tabPages[_currentIndex], "OnDisappearing");
        _currentIndex = index;
        InvokeLifecycle(_tabPages[_currentIndex], "OnAppearing");
        UpdateTabBarVisibility();
        UpdateTabBarSelection();
        // 切到播放页（index 1）或歌词页（index 0）时 MiniPlayer 必须隐藏，否则被全屏播放页压住
        UpdateMiniPlayerVisibility();
    }

    /// <summary>原生 ViewPager2 滑动状态变化：拖拽/归位期间暂停 FrostedBackground 动画，空闲恢复。</summary>
    private void OnNativeScrollStateChanged(NativeTabPager.ScrollState state)
    {
        switch (state)
        {
            case NativeTabPager.ScrollState.Dragging:
            case NativeTabPager.ScrollState.Settling:
                _swipeInteractionToken ??= _interactionState?.BeginInteraction("TabSwipe");
                break;
            case NativeTabPager.ScrollState.Idle:
                _swipeInteractionToken?.Dispose();
                _swipeInteractionToken = null;
                break;
        }
    }
#endif

    /// <summary>跨平台切换分页：Android 走原生 ViewPager2，Windows 走手动动画。</summary>
    private void SwitchToVpIndex(int vpIndex, bool animate = true)
    {
#if ANDROID
        _nativePager?.GoToItem(vpIndex, animate);
#else
        _ = AnimateToPage(vpIndex);
#endif
    }

    /// <summary>
    /// 递归遍历各页面视图树，给非滚动的 Layout 区域添加 PanGestureRecognizer，
    /// 确保即便子视图（如 RecyclerView）持有事件流，水平滑动也能触发 tab 切换。
    /// 每个视图创建独立实例，避免多视图共享同一实例导致部分机型闪退。
    /// 跳过 ScrollView（与内置滚动冲突）、跳过横向 List/CollectionView（自行处理水平滑动）、
    /// 跳过包含 Button/Entry 等交互控件的 Layout（避免手势拦截按钮点击）。
    /// </summary>
    private static void AddPanToLayouts(VisualElement element, EventHandler<PanUpdatedEventArgs> handler)
    {
        if (element is Slider) return;

        if (element is Layout layout)
        {
            var hasInteractiveChild = layout.Children.OfType<VisualElement>().Any(c =>
                c is Button or ImageButton or Switch or Entry or Editor or Picker or DatePicker or TimePicker or CheckBox or RadioButton or Slider or Stepper);
            if (!hasInteractiveChild)
            {
                var pan = new PanGestureRecognizer();
                pan.PanUpdated += handler;
                layout.GestureRecognizers.Add(pan);
            }
            foreach (var child in layout.Children.OfType<VisualElement>())
                AddPanToLayouts(child, handler);
            return;
        }

        if (element is ScrollView) return;

        if (element is ItemsView itemsView)
        {
            if (element is CarouselView) return;
            if (element is StructuredItemsView structuredView
                && structuredView.ItemsLayout is LinearItemsLayout linearLayout
                && linearLayout.Orientation == ItemsLayoutOrientation.Horizontal)
            {
                if (structuredView is CollectionView cv)
                {
                    if (cv.Header is VisualElement header) AddPanToLayouts(header, handler);
                    if (cv.Footer is VisualElement footer) AddPanToLayouts(footer, handler);
                    if (cv.EmptyView is VisualElement empty) AddPanToLayouts(empty, handler);
                }
                return;
            }
            if (itemsView is CollectionView colView)
            {
                if (colView.Header is VisualElement header) AddPanToLayouts(header, handler);
                if (colView.Footer is VisualElement footer) AddPanToLayouts(footer, handler);
                if (colView.EmptyView is VisualElement empty) AddPanToLayouts(empty, handler);
            }
            return;
        }

        if (element is ContentView contentView)
        {
            if (contentView.Content is VisualElement content)
                AddPanToLayouts(content, handler);
            return;
        }

        if (element is ContentPage page)
        {
            if (page.Content is VisualElement content)
                AddPanToLayouts(content, handler);
            return;
        }
    }

    /// <summary>递归遍历所有 ScrollView，强制设为垂直滚动</summary>
    private static void ForceVerticalScroll(VisualElement element)
    {
        if (element is ScrollView scrollView)
        {
            scrollView.Orientation = ScrollOrientation.Vertical;
        }

        if (element is Layout layout)
        {
            foreach (var child in layout.Children.OfType<VisualElement>())
            {
                ForceVerticalScroll(child);
            }
        }
    }

    /// <summary>递归遍历所有 ScrollView，强制设为垂直滚动</summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // 启动时重新应用主题，确保 TabBar 等控件颜色正确
        try
        {
            var themeService = MauiProgram.Services.GetService<IThemeService>();
            themeService?.ApplyTheme();
        }
        catch { }

        Log.Debug("MainPage.xaml", $"[MainPage] OnAppearing, currentIndex={_currentIndex}, ViewPagerGrid.Width={ViewPagerGrid.Width}");
        UpdatePagePositions(0);
        UpdateTabBarVisibility();
        UpdateTabBarSelection();
        // 主题色可能在设置页切换后返回，这里按当前主题/深浅模式刷新播放控制条图标
        _nowPlayingVm.RefreshPlayerCtrlIcons();

        // 启动后兜底：窗口稳定后反复强制 Edge-to-Edge，复刻「导航到二级页再返回」的全屏效果
        // （MAUI 在窗口就绪前会把 DecorFitsSystemWindows 重置为 true，导致启动首帧露白）。
        // 缩减为 2 次（1s 内），避免过量全页重排压垮主线程。EdgeToEdgeGlobalLayoutListener
        // 的 4 次回调 + insets 异步派发已提供充足的重试覆盖。
#if ANDROID
        StartEdgeToEdgeSettlingTimer();
#endif

        // 启动时 SafeAreaHelper.BottomInset 可能尚未就绪（EdgeToEdgeInsets 经 BeginInvokeOnMainThread 异步赋值），
        // 延迟一帧再应用一次 SafeArea padding，确保 BottomInset 已是真实导航栏高度，消除启动空白。
        // 不再递归 ClearNativeBottomPadding——EdgeToEdgeInsets.OnApplyWindowInsets 已清零根视图 padding，
        // 额外的递归清零（250ms/700ms）与定时器重复，导致过量全页重排（14+ 次 InvalidateMeasure），
        // 每轮在主线程上触发 measure→layout 级联，叠加 Mono GC 桥接后导致 11s 级阻塞。
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(250);
            UpdateSafeAreaPadding();
        });

        // 处理待切换的 tab（PendingTabIndex 使用 0-4 的 tab 索引，需 +1 映射到 ViewPager）
        if (PendingTabIndex.HasValue)
        {
            var idx = PendingTabIndex.Value + 1;
            PendingTabIndex = null;
            if (idx != _currentIndex)
            {
                SwitchToVpIndex(idx);
                return;
            }
        }

        InvokeLifecycle(_tabPages[_currentIndex], "OnAppearing");

        // 首次加载：后台预加载启动目标 tab 的 ViewModel 数据（不显示全屏遮罩，
        // 每个 tab 页面自带加载指示，直接进入即可）
        if (_isFirstLoad)
        {
            _isFirstLoad = false;

            var startupIdx = Preferences.Default.Get("StartupPageIndex", 2);
            var targetTabIdx = AppearanceSettingsViewModel.MapStartupIndexToTabIndex(startupIdx);

            // 预加载放后台执行，不阻塞 UI；数据就绪后由各页面自行刷新
            _ = PreloadTabDataAsync(targetTabIdx);

            if (targetTabIdx != 0)
            {
                SwitchToTab(targetTabIdx);
            }
        }
    }

    /// <summary>后台预加载启动目标 tab 的 ViewModel 数据。
    /// TabBar 按钮语义（按钮 t → ViewPager t+1）：0=播放、1=发现、2=歌单、3=音乐库；
    /// MapStartupIndexToTabIndex 仅产出 {0,1,3}，歌单(2)永不是启动目标。
    /// 播放(0)：预加载封面/歌词；发现(1)：预加载发现页——SearchPage.OnAppearing 有 Count 守卫会跳过重复加载。
    /// 采用后台执行（非 await），不阻碍主界面进入。</summary>
    private async Task PreloadTabDataAsync(int targetTabIdx)
    {
        try
        {
            await PreloadTabDataCoreAsync(targetTabIdx);
        }
        catch (Exception ex)
        {
            Log.Debug("MainPage.xaml", $"[MainPage] 预加载失败: {ex.Message}");
        }
    }

    private async Task PreloadTabDataCoreAsync(int targetTabIdx)
    {
        try
        {
            var searchVm = _services.GetRequiredService<SearchViewModel>();
            var libraryVm = _services.GetRequiredService<LibraryViewModel>();

            if (targetTabIdx == 3)
                await libraryVm.LoadLocalAsync();
            else if (targetTabIdx == 0)
                await _nowPlayingVm.PreloadMediaAsync();
            else
                await searchVm.LoadExploreDataAsync();

            Log.Debug("MainPage.xaml", "[MainPage] 预加载完成");
        }
        catch (Exception ex)
        {
            Log.Debug("MainPage.xaml", $"[MainPage] 预加载失败: {ex.Message}");
        }
    }

    /// <summary>当页面从屏幕上消失时触发，通知当前 Tab 页面其正在消失。</summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        InvokeLifecycle(_tabPages[_currentIndex], "OnDisappearing");
    }

    /// <summary>
    /// 系统返回键处理：聊天模式下退出聊天，全屏歌词/播放页返回发现页，
    /// 主 tab 上手动锁定横屏时退出横屏锁定，否则不拦截（交由系统处理）。
    /// </summary>
    protected override bool OnBackButtonPressed()
    {
#if ANDROID
        // 横屏桌面舞台：桌面内嵌子页优先关闭回桌面主页，否则解锁横屏回竖屏
        if (_desktopActive)
        {
            if (_desktopPage is not null && _desktopPage.HasEmbeddedSubPage)
            {
                _desktopPage.CloseEmbeddedSubPage("discover");
                return true;
            }
            if (Application.Current is App app && app.ManualLandscape)
            {
                app.ReleaseManualLandscape();
                return true;
            }
            return true; // 主 tab 的横屏：返回键兜底解锁
        }
#endif
        var searchVm = _services.GetService<SearchViewModel>();
        if (searchVm?.IsChatMode == true)
        {
            searchVm.ExitChatModeCommand.Execute(null);
            return true;
        }

        if (_currentIndex == 0)
        {
            // 全屏歌词页 → 返回播放页
            SwitchToVpIndex(1);
            return true;
        }

        if (_currentIndex == 1)
        {
            // 播放页 → 返回发现页
            SwitchToVpIndex(2);
            return true;
        }

        return base.OnBackButtonPressed();
    }

    /// <summary>当 ViewPager 网格尺寸发生变化时触发，重新计算各页面位置以适配新尺寸。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnViewPagerSizeChanged(object? sender, EventArgs e)
    {
        UpdatePagePositions(0);
    }

    /// <summary>懒加载：仅当前页±1 可见，远页 IsVisible=false。
    /// MAUI 的 IsVisible=false 不会销毁 handler，故各页滚动位置/状态不丢失，切换时也不会闪白；
    /// 同时大幅减少参与 GPU 合成的全屏页面数量，缓解 net10 上左右滑动的抽搐。</summary>
    private void UpdatePageVisibility()
    {
        var width = ViewPagerGrid.Width;
        for (int i = 0; i < _tabPages.Count; i++)
        {
            if (ViewPagerGrid.Children[i] is not VisualElement view) continue;
            var visible = Math.Abs(i - _currentIndex) <= 1;
            if (view.IsVisible != visible)
            {
                view.IsVisible = visible;
                // 重新可见时确保落在正确静止位置（远页此前可能未参与跟手位移）
                if (visible && width > 0)
                    view.TranslationX = (i - _currentIndex) * width;
            }
        }
    }

    /// <summary>切页动画前临时扩大可见范围到 [min-1, max+1]，确保跨多页跳转时目标页与途经页在动画期间可见，避免滑入时缺页闪白。</summary>
    private void SetTransitionVisibility(int targetIndex)
    {
        var width = ViewPagerGrid.Width;
        var lo = Math.Max(0, Math.Min(_currentIndex, targetIndex) - 1);
        var hi = Math.Min(_tabPages.Count - 1, Math.Max(_currentIndex, targetIndex) + 1);
        for (int i = 0; i < _tabPages.Count; i++)
        {
            if (ViewPagerGrid.Children[i] is not VisualElement view) continue;
            var vis = i >= lo && i <= hi;
            if (view.IsVisible != vis)
            {
                view.IsVisible = vis;
                if (vis && width > 0)
                    view.TranslationX = (i - _currentIndex) * width;
            }
        }
    }

    /// <summary>更新当前页±1 的 TranslationX，offset 为滑动偏移量（懒加载：远页不参与）</summary>
    private void UpdatePagePositions(double offset)
    {
#if ANDROID
        // Android 路径分页位置由 NativeTabPager（ViewPager2）管理，无需手动 TranslationX。
        // ViewPagerGrid 现承载 pagerHost，若在此移动会把整个 pagerHost 移出屏幕。
        return;
#else
        var width = ViewPagerGrid.Width;
        if (width <= 0) return;

        for (int i = 0; i < _tabPages.Count; i++)
        {
            if (Math.Abs(i - _currentIndex) > 1) continue; // 懒加载：远页（IsVisible=false）不参与跟手位移
            if (ViewPagerGrid.Children[i] is VisualElement view)
                view.TranslationX = (i - _currentIndex) * width + offset;
        }
#endif
    }

    /// <summary>
    /// 给 ViewPager 内的 Tab 页面开启/关闭 GPU 硬件层。
    /// Tab 平移与切页动画本质是对整页做 TranslationX 位移。若不上硬件层，
    /// MAUI 的 TranslateTo/TranslationX 每帧会触发子树重绘（CollectionView/Border/Image 等），
    /// 主线程繁忙导致滑动卡顿（在 .NET 10 上尤为明显，表现为左右抽搐）；
    /// 上硬件层后整页被栅格化为独立纹理，位移由 GPU 合成，帧率显著提升。
    /// 拖拽开始(Started)即开启，切页/弹回动画结束后关闭以释放 GPU 显存。
    /// 仅对可见页（当前±1）开硬件层，远页保持默认，避免无意义纹理开销；
    /// 不按 enabled 状态提前返回：切页后可见范围会变，需按当前 _currentIndex 重新计算。
    /// 非 Android 平台为空实现。
    /// </summary>
    /// <param name="enabled">true=开启硬件层，false=恢复默认（关闭硬件层）。</param>
    private void SetHardwareLayersEnabled(bool enabled)
    {
#if ANDROID
        // 按实际可见性开层：懒加载下只有可见页（IsVisible=true）参与合成，
        // 远页无需纹理。切页跨页跳转时会临时扩大可见范围，层也随之覆盖。
        for (int i = 0; i < ViewPagerGrid.Children.Count; i++)
        {
            var child = ViewPagerGrid.Children[i];
            if (child is not VisualElement ve) continue;
            if (child.Handler?.PlatformView is not AView nativeView) continue;
            nativeView.SetHardwareLayer(enabled && ve.IsVisible);
        }
#endif
    }

    /// <summary>PanGestureRecognizer 跟手滑动处理（还原 v1.6.5：拖拽期开硬件层 + 看门狗 + 暂停雾面动画）</summary>
    private async void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panTotalX = 0;
                _isPanning = true;
                _directionLocked = false;
                _lastPanRunningTime = DateTime.Now;
                StartPanWatchdog();
                // 开始滑动：开启各 Tab 页 GPU 硬件层，平移期间由 GPU 合成，避免主线程重绘卡顿/抽搐
                SetHardwareLayersEnabled(true);
                // 暂停歌词更新、FrostedBackground 动画等耗时操作，减轻主线程负担
                _panInteractionToken?.Dispose();
                _panInteractionToken = _interactionState?.BeginInteraction("TabSwipe");
                break;

            case GestureStatus.Running:
                if (!_isPanning) return;
                _lastPanRunningTime = DateTime.Now;

                // 方向锁定：只响应明确的水平滑动，垂直或近水平交给滚动控件
                if (!_directionLocked)
                {
                    if (Math.Abs(e.TotalX) > DirectionLockThreshold || Math.Abs(e.TotalY) > DirectionLockThreshold)
                    {
                        _directionLocked = true;
                        // 判定为垂直滚动（垂直分量更大，或水平分量未达到垂直分量的 HorizontalRatio 倍）
                        if (Math.Abs(e.TotalY) > Math.Abs(e.TotalX)
                            || Math.Abs(e.TotalX) < Math.Abs(e.TotalY) * HorizontalRatio)
                        {
                            _isPanning = false;
                            StopPanWatchdog();
                            // 判定为垂直滚动，放弃平移：关闭硬件层，避免长期占用 GPU 显存
                            SetHardwareLayersEnabled(false);
                            _panInteractionToken?.Dispose();
                            _panInteractionToken = null;
                            return;
                        }
                    }
                    else return;
                }

                _panTotalX = e.TotalX;

                var width = ViewPagerGrid.Width;
                if (width <= 0) return;

                // 边界阻尼
                var offset = _panTotalX;
                if (_currentIndex == 0 && offset > 0)
                    offset *= 0.35;
                if (_currentIndex == _tabPages.Count - 1 && offset < 0)
                    offset *= 0.35;

                UpdatePagePositions(offset);
                break;

            case GestureStatus.Completed:
                if (!_isPanning) return;
                _isPanning = false;
                StopPanWatchdog();

                var sw = ViewPagerGrid.Width;
                var threshold = sw * SwipeThresholdRatio;

                if (_panTotalX < -threshold && _currentIndex < _tabPages.Count - 1)
                {
                    // 左滑 → 下一个页面
                    await AnimateToPage(_currentIndex + 1);
                }
                else if (_panTotalX > threshold && _currentIndex > 0)
                {
                    // 右滑 → 上一个页面（index 0 是全屏歌词，index 1 是播放页）
                    await AnimateToPage(_currentIndex - 1);
                }
                else
                {
                    await BounceBack();
                }
                // 动画结束后释放交互令牌，恢复正常更新
                _panInteractionToken?.Dispose();
                _panInteractionToken = null;
                break;

            case GestureStatus.Canceled:
                _isPanning = false;
                StopPanWatchdog();
                await BounceBack();
                _panInteractionToken?.Dispose();
                _panInteractionToken = null;
                break;
        }
    }

    /// <summary>启动滑动看门狗：防止滚动控件偷走触摸事件导致页面卡在中间。
    /// 回调时重检 _lastPanRunningTime：如果用户在 timer elapsed 之后恢复了滑动则不弹回。</summary>
    private void StartPanWatchdog()
    {
        StopPanWatchdog();
        _panWatchdogTimer = new System.Timers.Timer(PanWatchdogInterval);
        _panWatchdogTimer.Elapsed += (_, _) =>
        {
            if (!_isPanning)
            {
                StopPanWatchdog();
                return;
            }
            if ((DateTime.Now - _lastPanRunningTime).TotalMilliseconds >= PanWatchdogInterval)
            {
                StopPanWatchdog();
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    // 在回调中重检：timer elapsed 到回调执行之间用户可能已恢复滑动
                    if (_isPanning
                        && (DateTime.Now - _lastPanRunningTime).TotalMilliseconds >= PanWatchdogInterval)
                    {
                        _isPanning = false;
                        await BounceBack();
                        _panInteractionToken?.Dispose();
                        _panInteractionToken = null;
                    }
                });
            }
        };
        _panWatchdogTimer.AutoReset = true;
        _panWatchdogTimer.Start();
    }

    /// <summary>停止滑动看门狗</summary>
    private void StopPanWatchdog()
    {
        _panWatchdogTimer?.Stop();
        _panWatchdogTimer?.Dispose();
        _panWatchdogTimer = null;
    }

    /// <summary>动画切换到指定页面（v1.6.5：整页平移由 GPU 合成，动画前后开关硬件层）</summary>
    private async Task AnimateToPage(int targetIndex)
    {
        if (targetIndex < 0 || targetIndex >= _tabPages.Count) return;

        var width = ViewPagerGrid.Width;
        if (width <= 0) return;

        // 跨页跳转（如从发现页直接点歌单/资料）前，先临时扩大可见范围到 [min-1,max+1]，
        // 确保目标页与途经页在动画期间可见，避免滑入时缺页闪白
        SetTransitionVisibility(targetIndex);

        // 切页动画前开启硬件层，整页平移由 GPU 合成
        SetHardwareLayersEnabled(true);

        var animations = new List<Task>();
        for (int i = 0; i < _tabPages.Count; i++)
        {
            if (ViewPagerGrid.Children[i] is not VisualElement view) continue;
            if (!view.IsVisible) continue; // 仅动画可见页，远页不参与
            var targetX = (i - targetIndex) * width;
            animations.Add(view.TranslateTo(targetX, 0, AnimDuration, Easing.SinOut));
        }
        await Task.WhenAll(animations);

        // 动画结束：关闭硬件层，释放 GPU 显存
        SetHardwareLayersEnabled(false);

        if (targetIndex != _currentIndex)
        {
            InvokeLifecycle(_tabPages[_currentIndex], "OnDisappearing");
            _currentIndex = targetIndex;
            InvokeLifecycle(_tabPages[_currentIndex], "OnAppearing");
            UpdateTabBarVisibility();
            UpdateTabBarSelection();
            // 切到播放页（index 1）或歌词页（index 0）时 MiniPlayer 必须隐藏，否则被全屏播放页压住
            UpdateMiniPlayerVisibility();
            // 切页后更新懒加载可见范围：新相邻页显示、远页隐藏
            UpdatePageVisibility();
        }
    }

    /// <summary>弹回当前位置（v1.6.5：弹回动画期间 GPU 合成）</summary>
    private async Task BounceBack()
    {
        var width = ViewPagerGrid.Width;
        if (width <= 0) return;

        // 弹回动画前开启硬件层
        SetHardwareLayersEnabled(true);

        var animations = new List<Task>();
        for (int i = 0; i < _tabPages.Count; i++)
        {
            if (ViewPagerGrid.Children[i] is not VisualElement view) continue;
            if (!view.IsVisible) continue; // 仅动画可见页，远页不参与
            var targetX = (i - _currentIndex) * width;
            animations.Add(view.TranslateTo(targetX, 0, BounceDuration, Easing.SinOut));
        }
        await Task.WhenAll(animations);

        // 动画结束：关闭硬件层
        SetHardwareLayersEnabled(false);
    }

    /// <summary>程序化切换 tab（tab 索引 0-4，内部映射到 ViewPager index 1-5）</summary>
    public void SwitchToTab(int tabIndex)
    {
        var vpIndex = tabIndex + 1;
        if (vpIndex < 0 || vpIndex >= _tabPages.Count || vpIndex == _currentIndex) return;
        SwitchToVpIndex(vpIndex);
    }

    /// <summary>切换到全屏歌词页（ViewPager index 0）</summary>
    public void SwitchToFullLyrics()
    {
        if (_currentIndex == 0) return;
        SwitchToVpIndex(0);
    }

    /// <summary>
    /// 收起播放页：播放页面向下平移，露出下层的发现页。
    /// 动画结束后内部切换到发现页（ViewPager index 2），并重置播放页位置。
    /// </summary>
    public async void CollapseNowPlaying()
    {
#if ANDROID
        // 原生 ViewPager2 下无法在分页容器之上叠加垂直抽屉收起动画，
        // 简化为直接切换到发现页（ViewPager index 2）。抽屉效果在 Windows 端保留。
        if (_currentIndex != 1) return;
        SwitchToVpIndex(2);
        return;
#else
        const int nowPlayingIndex = 1;
        const int discoverIndex = 2;

        if (_currentIndex != nowPlayingIndex) return;

        var height = ViewPagerGrid.Height;
        var width = ViewPagerGrid.Width;
        if (height <= 0 || width <= 0) return;

        if (ViewPagerGrid.Children[nowPlayingIndex] is not VisualElement nowPlaying) return;
        if (ViewPagerGrid.Children[discoverIndex] is not VisualElement discover) return;

        SetHardwareLayersEnabled(true);

        // 动画期间关闭裁剪，让播放页能向下滑出 ViewPagerGrid 边界
        ViewPagerGrid.IsClippedToBounds = false;

        // 动画期间让播放页位于上层，发现页在下层作为收起后露出的底
        nowPlaying.ZIndex = 10;
        // 将发现页移到播放页正下方（重叠位置）
        discover.TranslationX = 0;

        // 播放页向下平移收起（抽屉效果）
        await nowPlaying.TranslateTo(0, height, 320, Easing.SinOut);

        // 更新生命周期和状态：切换到发现页
        InvokeLifecycle(_tabPages[_currentIndex], "OnDisappearing");
        _currentIndex = discoverIndex;
        InvokeLifecycle(_tabPages[_currentIndex], "OnAppearing");
        UpdateTabBarVisibility();
        // 收起抽屉后回到发现页（index 2）应重新显示 MiniPlayer
        UpdateMiniPlayerVisibility();
        // 收起后切换可见范围：发现页相邻页显示、远页隐藏
        UpdatePageVisibility();
        UpdateTabBarSelection();
        UpdateSafeAreaPadding();
        UpdateMiniPlayerVisibility();

        // 重置播放页位置和 ZIndex：移到屏幕右侧（对应 ViewPager index 1 相对 index 2 的偏移）
        nowPlaying.TranslationX = (nowPlayingIndex - discoverIndex) * width;
        nowPlaying.TranslationY = 0;
        nowPlaying.ZIndex = 0;

        // 恢复裁剪，关闭动画期间开启的硬件层
        ViewPagerGrid.IsClippedToBounds = true;
        SetHardwareLayersEnabled(false);
#endif
    }

    /// <summary>导航到指定 tab（静态方法，供外部调用。tab 索引 0-4）</summary>
    public static async Task GoToTabAsync(int tabIndex)
    {
        var shell = DesktopNavigation.TryGetShell();
        if (shell == null) return; // 桌面无 Shell 不处理
        var currentRoute = shell.CurrentState.Location.ToString();
        if (currentRoute != "//main")
        {
            PendingTabIndex = tabIndex;
            await shell.GoToAsync("//main");
        }
        else
        {
            Instance?.SwitchToTab(tabIndex);
        }
    }

    /// <summary>点击底部 TabBar 的某个 Tab 按钮时触发，切换到对应的页面。</summary>
    /// <param name="sender">事件源，对应被点击的 Tab 按钮。</param>
    /// <param name="e">点击事件参数。</param>
    private void OnTabTapped(object? sender, TappedEventArgs e)
    {
        // Tab 按钮 0-3 对应 ViewPager index 1-4
        if (sender == TabItem0) SwitchToVpIndex(1);
        else if (sender == TabItem1) SwitchToVpIndex(2);
        else if (sender == TabItem2) SwitchToVpIndex(3);
        else if (sender == TabItem3) SwitchToVpIndex(4);
    }

    /// <summary>页面尺寸变化（横竖屏旋转/首布局）：切换舞台。原生旋转唯一入口。</summary>
    private void OnMainPageSizeChanged(object? sender, EventArgs e)
    {
        if (Width <= 0 || Height <= 0) return;
        if (Width > Height) ShowDesktopStage();
        else ShowMobileStage();
    }

    /// <summary>进入横屏舞台：把 DesktopMainPage 的 Content 借入本页 DesktopStage（与 Windows 桌面同一布局）。
    /// 借入后手动触发 OnAppearing 生命周期（页面自身不可见，需反射调用）。</summary>
    private void ShowDesktopStage()
    {
        if (_desktopActive) return;
        _desktopActive = true;
        try
        {
            var desktop = _desktopPage ??= _services.GetRequiredService<DesktopMainPage>();
            if (_desktopRoot == null && desktop.Content is Grid root)
                _desktopRoot = root;

            if (_desktopRoot != null)
            {
                // ⚠ 先 detach：ContentPage.Content 的 Parent 是页面本身（非 null），
                // 只有置 null 后 Parent 才变 null，才能加入舞台。
                desktop.Content = null;
                if (_desktopRoot.Parent == null)
                {
                    DesktopStage.Children.Add(_desktopRoot);
                    // ⚠ 借入的根 Grid 脱离了 DesktopMainPage，BindingContext 继承链断在 MainPage
                    // （MainPage 自身未设 BindingContext）→ 底部播放栏的封面/歌名/进度条等
                    // {Binding CoverImage/Title/Progress...} 全部落空。显式钉回桌面页的 VM
                    // （与 DesktopMainPage.OpenSubPageEmbedded 的 content.BindingContext 同理）。
                    _desktopRoot.BindingContext = desktop.BindingContext;
                    if (_currentIndex == 0)
                    {
                        // 竖屏在歌词页转横屏 → 桌面舞台自动内嵌全屏歌词页（横屏布局）
                        desktop.OpenSubPageEmbedded(_services.GetRequiredService<FullLyricsPage>());
                    }
                    else if (_currentIndex == 1)
                    {
                        // 竖屏在播放页转横屏 → 桌面舞台自动内嵌播放页（横屏布局）
                        desktop.OpenSubPageEmbedded(_services.GetRequiredService<NowPlayingPage>());
                    }
                    else
                    {
                        // 其它 tab → 桌面侧栏对应 tab
                        string tab = _currentIndex switch
                        {
                            3 => "playlists",
                            4 => "library",
                            _ => "discover"
                        };
                        desktop.SwitchToNamedTab(tab);
                    }
                    // 播放/歌词页全屏沉浸：舞台不垫安全区（页面自身处理刘海）；桌面主页左侧垫刘海/状态栏安全区
                    DesktopStage.Padding = _currentIndex <= 1
                        ? new Thickness(0)
                        : new Thickness(SafeAreaHelper.LeftInset, 0, 0, 0);
                    InvokeLifecycle(desktop, "OnAppearing");
                }
            }
            DesktopStage.IsVisible = true;
            MobileStage.IsVisible = false;
        }
        catch (Exception ex)
        {
            _desktopActive = false;
#if ANDROID
            Android.Util.Log.Error("CatClaw", $"[Stage] ShowDesktopStage 失败: {ex}");
#endif
        }
    }

    /// <summary>当前是否处于横屏桌面舞台（供播放页/歌词页按钮判断横屏承载方式）。</summary>
    public bool IsDesktopActive => _desktopActive;

    /// <summary>回到竖屏移动舞台：把桌面布局归还 DesktopMainPage，恢复 ViewPager/MiniPlayer/TabBar 显示。</summary>
    private void ShowMobileStage()
    {
        if (!_desktopActive) return;
        _desktopActive = false;
        if (_desktopPage != null && _desktopRoot != null && _desktopRoot.Parent != null)
        {
            // 页面不可视前通知 OnDisappearing（停止桌面持续性工作），再归还 Content
            InvokeLifecycle(_desktopPage, "OnDisappearing");
            DesktopStage.Children.Remove(_desktopRoot);
            _desktopPage.Content = _desktopRoot;
        }
        DesktopStage.IsVisible = false;
        MobileStage.IsVisible = true;
    }

    /// <summary>全屏歌词页和播放页时隐藏导航栏和迷你播放器；AI 聊天模式仅隐藏导航栏。
    /// 仅竖屏移动舞台使用（横屏时整个舞台被桌面布局替换）。</summary>
    private void UpdateTabBarVisibility()
    {
        // index 0 = 全屏歌词, index 1 = 播放页，两者都全屏
        var isFullScreen = _currentIndex <= 1;
        // AI 聊天模式：隐藏导航栏，但保留迷你播放器（显示在输入框上方）
        var searchVm = _services.GetService<SearchViewModel>();
        var isChatMode = searchVm?.IsChatMode == true;
        var hideNav = isFullScreen || isChatMode;
        // MAUI 11: IsVisible=false 在 Auto 行中可能不收缩行高，需要同时设置 HeightRequest=0
        TabBar.IsVisible = !hideNav;
        UpdateMiniPlayerVisibility();
        UpdateSafeAreaPadding();
    }

    /// <summary>系统栏高度变化时触发，更新各区域 SafeArea padding</summary>
    private void OnSafeAreaChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(UpdateSafeAreaPadding);
    }

    /// <summary>
    /// 根据当前页面是否全屏，更新 SafeArea padding：
    /// - 全屏页面（播放页/歌词页）：ViewPagerGrid 无顶部 padding，让雾面背景延伸到状态栏；TabBar 隐藏无需底部 padding
    /// - 非全屏页面：ViewPagerGrid 顶部留出状态栏高度；TabBar 底部留出导航栏高度，并动态调整高度包含 inset
    /// </summary>
    /// <summary>
    /// 关闭各布局的安全区内缩：.NET MAUI 11 起 Layout 默认 SafeAreaEdges=Container，
    /// 会在底部（及顶部）套系统栏内缩 padding，使自定义 TabBar 离屏幕底部一截、下方露出空白。
    /// 项目从 Mono 升级到 .NET 11 / CoreCLR 后该默认行为才出现，故在此显式禁用。
    /// </summary>
    private void ApplyEdgeToEdgeSafeArea()
    {
#if ANDROID
        // 注意：必须用完全限定名 Microsoft.Maui.Controls.Layout，
        // 否则未限定的 Layout 会解析成 VisualElement.Layout(Rect) 方法而编译失败（CS0119）。
        // 页面自身也显式设为 None（全局样式已覆盖，这里双保险，防止 Release SourceGen 膨胀器丢弃 XAML 属性）。
        this.SetValue(Microsoft.Maui.Controls.Layout.SafeAreaEdgesProperty, SafeAreaEdges.None);
        if (this.Content is Microsoft.Maui.Controls.Layout root)
            root.SetValue(Microsoft.Maui.Controls.Layout.SafeAreaEdgesProperty, SafeAreaEdges.None);
        ViewPagerGrid.SetValue(Microsoft.Maui.Controls.Layout.SafeAreaEdgesProperty, SafeAreaEdges.None);
        MiniPlayer.SetValue(Microsoft.Maui.Controls.Layout.SafeAreaEdgesProperty, SafeAreaEdges.None);
        TabBar.SetValue(Microsoft.Maui.Controls.Layout.SafeAreaEdgesProperty, SafeAreaEdges.None);
#endif
    }

    /// <summary>清除页面容器（MainPage 的原生视图 + android.R.id.content 根视图）上的原生 padding。</summary>
#if ANDROID
    private void ClearNativeBottomPadding()
    {
        try
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            var rootView = activity?.Window?.DecorView?.FindViewById(Android.Resource.Id.Content);

            // 递归清除 android.R.id.content 及其所有子容器的底部 padding
            if (rootView is Android.Views.ViewGroup rootGroup)
                ClearPaddingRecursive(rootGroup);

            if (rootView != null)
                AndroidX.Core.View.ViewCompat.RequestApplyInsets(rootView);
        }
        catch { }
    }

    /// <summary>递归清除 ViewGroup 树中所有容器的底部 padding，直到找到 MainPage 的原生视图为止</summary>
    private static void ClearPaddingRecursive(Android.Views.ViewGroup? parent)
    {
        if (parent == null) return;
        parent.SetPadding(parent.PaddingLeft, parent.PaddingTop, parent.PaddingRight, 0);
        for (int i = 0; i < parent.ChildCount; i++)
        {
            var child = parent.GetChildAt(i);
            if (child is Android.Views.ViewGroup childGroup)
                ClearPaddingRecursive(childGroup);
        }
    }
#endif

#if ANDROID
    private bool _edgeToEdgeSettling;
    private int _edgeToEdgeTicks;

    /// <summary>
    /// 启动后兜底：MAUI 的 AndroidWindow 在窗口完全就绪（首个页面铺设）之前会把
    /// DecorFitsSystemWindows 重置为 true（内容止于系统栏），导致首帧 MainPage 比屏幕矮一截、
    /// TabBar 被裁切露出底部空白；而「导航到二级页再返回」之所以能全屏，是因为那时窗口已稳定、
    /// 重新铺设后才生效。这里用定时器在窗口稳定后反复强制 Edge-to-Edge + 清零原生 padding +
    /// 强制整页重排，复刻「导航返回」的全屏效果，确保「启动即全屏」。
    /// </summary>
    private void StartEdgeToEdgeSettlingTimer()
    {
        if (_edgeToEdgeSettling) return;
        _edgeToEdgeSettling = true;
        _edgeToEdgeTicks = 0;
        Microsoft.Maui.Controls.Device.StartTimer(TimeSpan.FromMilliseconds(500), () =>
        {
            _edgeToEdgeTicks++;
            ForceFullScreenAfterStartup();
            if (_edgeToEdgeTicks >= 2)
            {
                _edgeToEdgeSettling = false;
                return false; // 停止定时器
            }
            return true; // 继续
        });
    }

    /// <summary>复刻「导航返回」的全屏效果：重新强制 Edge-to-Edge 并更新 SafeArea padding。
    /// ClearNativeBottomPadding 已由 EdgeToEdgeInsets.OnApplyWindowInsets 覆盖（v.SetPadding(0,0,0,0)），
    /// 无需在此递归清零（深层遍历 ViewGroup 树触发大量 SetPadding → requestLayout，级联开销极大）。
    /// InvalidateMeasure 已含在 UpdateSafeAreaPadding 末尾，无需重复调用。</summary>
    private void ForceFullScreenAfterStartup()
    {
        try
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity as MainActivity;
            activity?.SetupEdgeToEdge();
        }
        catch { }
        UpdateSafeAreaPadding();
    }
#endif

    private void UpdateSafeAreaPadding()
    {
        // 滑动过程中不触发整页重排，避免和 TranslationX 动画抢主线程；
        // 动画/滑动结束后的调用（如 AnimateToPage -> UpdateTabBarVisibility）会补齐最终状态。
        if (_isPanning) return;

        var bottom = SafeAreaHelper.BottomInset;
        var isFullScreen = _currentIndex <= 1;
        var searchVm = _services.GetService<SearchViewModel>();
        var isChatMode = searchVm?.IsChatMode == true;
        var hideTabBar = isFullScreen || isChatMode;

        // 顶部状态栏高度由各非全屏页面自行通过 SafeAreaPaddingBehavior 预留
        // （见 SetupPages），ViewPagerGrid 不再加顶部 padding，避免重复计算与失效风险。
        // 这里 ViewPagerGrid.Padding 仅保持为 0。
        var viewPagerPadding = new Thickness(0);
        // AI 聊天模式：TabBar 隐藏后页面区域会延伸到屏幕底部，聊天输入栏（SearchPage 底部）
        // 会被三键导航栏/车机 dock 遮挡 → 为页面区域预留底部 inset。
        // 全屏页面（播放页/歌词页）自身处理底部安全区，chat 与其互斥（聊天仅在发现页触发），
        // 但显式排除 isFullScreen 防止双倍 padding。
        if (isChatMode && !isFullScreen)
            viewPagerPadding = new Thickness(0, 0, 0, bottom);
        if (ViewPagerGrid.Padding != _lastViewPagerPadding)
        {
            ViewPagerGrid.Padding = viewPagerPadding;
            _lastViewPagerPadding = viewPagerPadding;
        }

        // TabBar 延伸到屏幕底部（含导航栏区域）：
        // HeightRequest(56+bottom) 让毛玻璃表面覆盖导航栏区域；
        // Padding(bottom+4) 把内部图标抬离导航栏，内容区高度保持恒定 46dp（不变形）。
        // 当 bottom=0（手势导航）时退化为 56+0 / 4+0，视觉与旧版完全一致。
        var tabBarHeight = hideTabBar ? 0 : (76 + bottom);
        var tabBarBottomPad = hideTabBar ? 0 : (6 + bottom);
        var tabBarPadding = new Thickness(0, 6, 0, tabBarBottomPad);

        bool tabBarChanged = TabBar.Padding != _lastTabBarPadding || Math.Abs(TabBar.HeightRequest - tabBarHeight) > 0.01;
        if (tabBarChanged)
        {
            TabBar.Padding = tabBarPadding;
            TabBar.HeightRequest = tabBarHeight;
            _lastTabBarPadding = tabBarPadding;
            _lastTabBarHeight = tabBarHeight;
        }

        // 关键修复：根 Grid 已设 SafeAreaEdges="None"，页面边缘到边延伸到屏幕底部（含导航栏区域），
        // 不再由 MAUI 预留底部安全区；TabBar 自身 HeightRequest(64+bottom) 与 Padding(bottom+8)
        // 负责把图标抬离系统导航栏。因此这里无需再用 TranslationY 下移（否则会推到屏幕外）。
#if ANDROID
        this.Padding = new Thickness(0);
#endif
        TabBar.TranslationY = 0;

        // 只有当相关值真正变化时才强制重排，减少无效 measure/layout 级联。
        // 滑动期间已提前返回，不会走到这里。
        if (tabBarChanged || ViewPagerGrid.Padding != _lastViewPagerPadding)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                TabBar.InvalidateMeasure();
                this.Content?.InvalidateMeasure();
            });
        }
        UpdateDiscoverBottomReserve();
    }

    /// <summary>迷你播放器仅在有当前歌曲且非全屏页、非聊天模式时显示（聊天模式下迷你播放器显示在聊天界面输入框上方）</summary>
    private void UpdateMiniPlayerVisibility()
    {
        var hasSong = !string.IsNullOrEmpty(_nowPlayingVm.Title);
        var searchVm = _services.GetService<SearchViewModel>();
        var isChatMode = searchVm?.IsChatMode == true;
        var visible = hasSong && _currentIndex > 1 && !isChatMode;
        if (visible == _lastMiniPlayerVisible) return;
        _lastMiniPlayerVisible = visible;
        MiniPlayer.IsVisible = visible;
        MiniPlayer.HeightRequest = visible ? 52 : 0;
        UpdateDiscoverBottomReserve();
    }

    /// <summary>根据迷你播放器与 TabBar 当前显隐/高度，联动调整发现页滚动内容底部预留，
    /// 使推荐艺人在播放时也能滚到悬浮条上方完整露出。</summary>
    private void UpdateDiscoverBottomReserve()
    {
        if (_searchPage == null) return;
        var mini = MiniPlayer.IsVisible ? MiniPlayer.HeightRequest : 0;
        var tab = TabBar.IsVisible ? TabBar.HeightRequest : 0;
        // 预留 = 迷你播放器高度 + 其底部间距(2) + 与TabBar间距(2) + TabBar高度
        var extra = mini + tab + (mini > 0 ? 4 : 0) + (tab > 0 ? 2 : 0);
        _searchPage.SetBottomReservedHeight(extra);
    }

    /// <summary>ViewModel 属性变化时更新迷你播放器显隐</summary>
    private void OnNowPlayingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NowPlayingViewModel.Title) ||
            e.PropertyName == nameof(NowPlayingViewModel.CurrentSong))
        {
            MainThread.BeginInvokeOnMainThread(UpdateMiniPlayerVisibility);
        }
    }

    /// <summary>SearchViewModel 属性变化：聊天模式切换时更新 TabBar 显隐</summary>
    private void OnSearchVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchViewModel.IsChatMode))
        {
            MainThread.BeginInvokeOnMainThread(UpdateTabBarVisibility);
        }
    }

    /// <summary>点击迷你播放器非按钮区域（封面/标题）跳转到播放页</summary>
    private void OnMiniPlayerTapped(object? sender, EventArgs e)
    {
        SwitchToVpIndex(1);
    }

    private static readonly string[] DarkIconSources = { "ic_play", "ic_home", "ic_playlist", "ic_library" };

    /// <summary>将 Color 转为 #RRGGBB（大写），用于定位按主题色预生成的图标资源</summary>
    private static string ToHex(Color c)
    {
        var r = (byte)Math.Round(c.Red * 255);
        var g = (byte)Math.Round(c.Green * 255);
        var b = (byte)Math.Round(c.Blue * 255);
        return $"#{r:x2}{g:x2}{b:x2}";
    }

    /// <summary>更新 TabBar 选中状态（Tab 0-3 对应 ViewPager index 1-4）</summary>
    private void UpdateTabBarSelection()
    {
        // TabActiveColor = 主题色（选中，按主题预生成 *_{hex}_active.svg）；
        // TabInactiveColor = 深色白 / 浅色灰（未选中，分别用原白底 svg / *_gray.svg）
        var activeColor = (Color)Application.Current!.Resources["TabActiveColor"];
        var inactiveColor = (Color)Application.Current!.Resources["TabInactiveColor"];
        var isDark = Application.Current.RequestedTheme == Microsoft.Maui.ApplicationModel.AppTheme.Dark;
        var activeHex = ToHex(activeColor);

        var labels = new[] { TabLabel0, TabLabel1, TabLabel2, TabLabel3 };
        var icons = new[] { TabIcon0, TabIcon1, TabIcon2, TabIcon3 };
        var tabBgs = new[] { TabBg0, TabBg1, TabBg2, TabBg3 };

        for (int i = 0; i < 4; i++)
        {
            var isActive = (i + 1) == _currentIndex;
            labels[i].TextColor = isActive ? activeColor : inactiveColor;

            // 选中态：胶囊高亮背景（accent 色 28% 透明度）+ 缩放，未选中透明
            tabBgs[i].BackgroundColor = isActive ? activeColor.WithAlpha(0.28f) : Colors.Transparent;

            if (isActive)
                icons[i].Source = $"{DarkIconSources[i]}_{activeHex.TrimStart('#')}_active";
            else
                icons[i].Source = isDark ? DarkIconSources[i] : $"{DarkIconSources[i]}_gray";

            icons[i].Scale = isActive ? 1.15 : 1.0;
            labels[i].Scale = isActive ? 1.1 : 1.0;
        }
    }

    /// <summary>缓存反射查找结果，避免每次切页都 GetMethods 遍历</summary>
    private static readonly Dictionary<(Type, string), MethodInfo?> _lifecycleCache = new();

    /// <summary>通过反射调用 ContentPage 的 OnAppearing/OnDisappearing</summary>
    private static void InvokeLifecycle(ContentPage page, string methodName)
    {
        try
        {
            var type = page.GetType();
            var key = (type, methodName);
            if (!_lifecycleCache.TryGetValue(key, out var method))
            {
                method = type.GetMethods(
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == 0);

                if (method == null)
                {
                    method = typeof(ContentPage).GetMethods(
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == 0);
                }
                _lifecycleCache[key] = method;
            }

            method?.Invoke(page, null);
        }
        catch (Exception ex)
        {
            Log.Debug("MainPage.xaml", $"[MainPage] InvokeLifecycle {methodName} FAILED: {ex.Message}");
        }
    }
}
