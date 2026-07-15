using System.Reflection;
using System.Linq;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Maui.ViewModels;
using System.ComponentModel;
using Microsoft.Maui.Storage;

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
    private readonly Services.IInteractionStateService? _interactionState;
    // ViewPager 布局: [FullLyrics(0), NowPlaying(1), Search(2), Playlist(3), Library(4)]
    // TabBar 的 4 个按钮对应 index 1-4，index 0 是全屏歌词（无 Tab 按钮）
    private int _currentIndex = 1;
    private double _panTotalX;
    private bool _isPanning;
    private bool _directionLocked;
    private bool _isFirstLoad = true;
    private System.Timers.Timer? _panWatchdogTimer;
    private DateTime _lastPanRunningTime;
    private IDisposable? _panInteractionToken;
    private const double SwipeThresholdRatio = 0.25;
    private const int AnimDuration = 280;
    private const int PanWatchdogInterval = 400;
    /// <summary>方向判定阈值：位移超过此值才判定水平/垂直方向（像素），避免手指微颤误触</summary>
    private const double DirectionLockThreshold = 14;
    /// <summary>水平倾斜比：|TotalX| 必须达到 |TotalY| 的此倍数才判定为水平滑动，否则视为垂直滚动</summary>
    private const double HorizontalRatio = 1.4;

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
        _nowPlayingVm = nowPlayingVm;
        _interactionState = services.GetService<Services.IInteractionStateService>();
        Instance = this;

        // 迷你播放器绑定到 NowPlayingViewModel
        MiniPlayer.BindingContext = _nowPlayingVm;
        _nowPlayingVm.PropertyChanged += OnNowPlayingPropertyChanged;

        // 订阅 SearchViewModel 的聊天模式变化：进入聊天时隐藏 TabBar，迷你播放器保留在输入框上方
        var searchVm = services.GetService<SearchViewModel>();
        if (searchVm != null)
        {
            searchVm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SearchViewModel.IsChatMode))
                {
                    MainThread.BeginInvokeOnMainThread(UpdateTabBarVisibility);
                }
            };
        }

        SetupPages();
        ViewPagerGrid.SizeChanged += OnViewPagerSizeChanged;

        // 订阅系统栏高度变化，更新 SafeArea padding
        SafeAreaHelper.SafeAreaChanged += OnSafeAreaChanged;
    }

    /// <summary>创建 5 个页面（全屏歌词 + 4 个 tab），提取 Content 放入 ViewPager</summary>
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

        foreach (var page in pages)
        {
            _tabPages.Add(page);
            var content = page.Content;
            page.Content = null;
            content.BindingContext = page.BindingContext;

            ForceVerticalScroll(content);
            AddPanToLayouts(content, OnPanUpdated);

            ViewPagerGrid.Children.Add(content);
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

    /// <summary>
    /// 递归给所有 Layout 子元素添加 PanGestureRecognizer，
    /// 确保页面任意区域都能响应左右滑动切换。
    /// 每个元素创建独立的 PanGestureRecognizer 实例，避免同一实例附加到多个视图导致部分机型闪退。
    /// 横向滚动的 CollectionView 不添加手势，让其自行处理水平滑动。
    /// 方向锁定逻辑（OnPanUpdated 中）会区分水平/垂直滑动，不影响 ScrollView 的垂直滚动。
    /// </summary>
    private static void AddPanToLayouts(VisualElement element, EventHandler<PanUpdatedEventArgs> handler)
    {
        if (element is Slider) return;

        // Layout（Grid/StackLayout等）：添加手势并递归子元素
        if (element is Layout layout)
        {
            var pan = new PanGestureRecognizer();
            pan.PanUpdated += handler;
            layout.GestureRecognizers.Add(pan);
            foreach (var child in layout.Children.OfType<VisualElement>())
            {
                AddPanToLayouts(child, handler);
            }
            return;
        }

        // ScrollView：不给 ScrollView 加 Pan 手势（会与内置滚动严重冲突，上下滑动阻力大）
        // 也不递归到内容里加（会被 ScrollView 的触摸拦截打断）
        // 解决方案：页面主容器请用 CollectionView 而非 ScrollView，与音乐库/歌单页保持一致
        if (element is ScrollView)
        {
            return;
        }

        // ItemsView (CollectionView/CarouselView/ListView 等)
        if (element is ItemsView itemsView)
        {
            // CarouselView（如发现页 Hero 轮播）：不添加 Pan 手势，
            // 让其内置的水平滑动切换自行处理，避免与 tab 切换手势冲突。
            if (element is CarouselView)
            {
                return;
            }

            // 横向滚动的 CollectionView 不添加 Pan 手势，避免拦截内部水平滑动
            if (element is StructuredItemsView structuredView
                && structuredView.ItemsLayout is LinearItemsLayout linearLayout
                && linearLayout.Orientation == ItemsLayoutOrientation.Horizontal)
            {
                // 水平列表自身不挂手势，但仍需递归其 Header/Footer/EmptyView，
                // 因为这些区域可能包含需要手势的空白区域或嵌套的水平列表
                if (structuredView is CollectionView cv)
                {
                    if (cv.Header is VisualElement header) AddPanToLayouts(header, handler);
                    if (cv.Footer is VisualElement footer) AddPanToLayouts(footer, handler);
                    if (cv.EmptyView is VisualElement empty) AddPanToLayouts(empty, handler);
                }
                return;
            }

            // 垂直滚动的列表：添加独立手势实例，方向锁定逻辑保证垂直滚动不受影响
            var pan = new PanGestureRecognizer();
            pan.PanUpdated += handler;
            itemsView.GestureRecognizers.Add(pan);

            // 递归进入 Header/Footer/EmptyView，让其中嵌套的水平 CollectionView 被显式跳过，
            // 同时给 Header 内的空白 Layout 区域也挂上 Pan 手势，支持在所有区域左右滑动切 tab
            if (itemsView is CollectionView colView)
            {
                if (colView.Header is VisualElement header) AddPanToLayouts(header, handler);
                if (colView.Footer is VisualElement footer) AddPanToLayouts(footer, handler);
                if (colView.EmptyView is VisualElement empty) AddPanToLayouts(empty, handler);
            }
            return;
        }

        // ContentView / Border：递归到其 Content
        if (element is ContentView contentView)
        {
            if (contentView.Content is VisualElement content)
                AddPanToLayouts(content, handler);
            return;
        }

        // ContentPage：递归到其 Content
        if (element is ContentPage page)
        {
            if (page.Content is VisualElement content)
                AddPanToLayouts(content, handler);
            return;
        }
    }

    /// <summary>当页面显示在屏幕上时触发，应用主题、更新页面位置与 TabBar 状态，并在首次加载时预加载数据。</summary>
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

        System.Diagnostics.Debug.WriteLine($"[MainPage] OnAppearing, currentIndex={_currentIndex}, ViewPagerGrid.Width={ViewPagerGrid.Width}");
        UpdatePagePositions(0);
        UpdateTabBarVisibility();
        UpdateTabBarSelection();

        // 处理待切换的 tab（PendingTabIndex 使用 0-4 的 tab 索引，需 +1 映射到 ViewPager）
        if (PendingTabIndex.HasValue)
        {
            var idx = PendingTabIndex.Value + 1;
            PendingTabIndex = null;
            if (idx != _currentIndex)
            {
                _ = AnimateToPage(idx);
                return;
            }
        }

        InvokeLifecycle(_tabPages[_currentIndex], "OnAppearing");

        // 首次加载：显示遮罩，并行预加载所有 Tab 的 ViewModel 数据
        if (_isFirstLoad)
        {
            _isFirstLoad = false;

            var startupIdx = Preferences.Default.Get("StartupPageIndex", 2);
            var targetTabIdx = AppearanceSettingsViewModel.MapStartupIndexToTabIndex(startupIdx);

            await PreloadTabDataAsync();

            if (targetTabIdx != 0)
            {
                SwitchToTab(targetTabIdx);
            }
        }
    }

    /// <summary>预加载所有 Tab 页面的 ViewModel 数据，加载完成后隐藏遮罩</summary>
    private async Task PreloadTabDataAsync()
    {
        LoadingOverlay.IsVisible = true;

        try
        {
            var libraryVm = _services.GetRequiredService<LibraryViewModel>();
            var playlistVm = _services.GetRequiredService<PlaylistViewModel>();
            var searchVm = _services.GetRequiredService<SearchViewModel>();

            // 并行预加载（各方法内部已用 Task.Run/Task.WhenAll 进行后台数据获取）
            await Task.WhenAll(
                libraryVm.LoadLocalAsync(),
                playlistVm.LoadPlaylistsAsync(),
                searchVm.LoadExploreDataAsync()
            );

            System.Diagnostics.Debug.WriteLine("[MainPage] 预加载完成");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainPage] 预加载失败: {ex.Message}");
        }
        finally
        {
            LoadingOverlay.IsVisible = false;
        }
    }

    /// <summary>当页面从屏幕上消失时触发，通知当前 Tab 页面其正在消失。</summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        InvokeLifecycle(_tabPages[_currentIndex], "OnDisappearing");
    }

    /// <summary>
    /// 系统返回键处理：聊天模式下退出聊天，全屏歌词/播放页返回发现页，否则不拦截（交由系统处理）。
    /// </summary>
    protected override bool OnBackButtonPressed()
    {
        var searchVm = _services.GetService<SearchViewModel>();
        if (searchVm?.IsChatMode == true)
        {
            searchVm.ExitChatModeCommand.Execute(null);
            return true;
        }

        if (_currentIndex == 0)
        {
            // 全屏歌词页 → 返回播放页
            _ = AnimateToPage(1);
            return true;
        }

        if (_currentIndex == 1)
        {
            // 播放页 → 返回发现页
            _ = AnimateToPage(2);
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

    /// <summary>更新所有页面的 TranslationX，offset 为滑动偏移量</summary>
    private void UpdatePagePositions(double offset)
    {
        var width = ViewPagerGrid.Width;
        if (width <= 0) return;

        for (int i = 0; i < _tabPages.Count; i++)
        {
            if (ViewPagerGrid.Children[i] is VisualElement view)
                view.TranslationX = (i - _currentIndex) * width + offset;
        }
    }

    /// <summary>
    /// 给 ViewPager 内的所有 Tab 页面开启/关闭 GPU 硬件层。
    /// Tab 平移与切页动画本质是对整页做 TranslationX 位移。若不上硬件层，
    /// MAUI 的 TranslateTo/TranslationX 每帧会触发子树重绘（CollectionView/Border/Image 等），
    /// 主线程繁忙导致滑动卡顿；上硬件层后整页被栅格化为独立纹理，位移由 GPU 合成，帧率显著提升。
    /// 仅在滑动/动画期间开启，结束后立即关闭以释放 GPU 显存（避免 6 个全屏页面常驻纹理）。
    /// 非 Android 平台为空实现。
    /// </summary>
    /// <param name="enabled">true=开启硬件层，false=恢复默认（关闭硬件层）。</param>
    private void SetHardwareLayersEnabled(bool enabled)
    {
#if ANDROID
        foreach (var child in ViewPagerGrid.Children)
        {
            if (child.Handler?.PlatformView is AView nativeView)
                nativeView.SetHardwareLayer(enabled);
        }
#endif
    }

    /// <summary>PanGestureRecognizer 跟手滑动处理</summary>
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
                // 开始滑动：开启各 Tab 页 GPU 硬件层，平移期间由 GPU 合成，避免主线程重绘卡顿
                SetHardwareLayersEnabled(true);
                // 通知交互状态服务：暂停歌词更新、FrostedBackground 动画等耗时操作，减轻主线程负担
                _panInteractionToken?.Dispose();
                _panInteractionToken = _interactionState?.BeginInteraction("TabSwipe");
                break;

            case GestureStatus.Running:
                if (!_isPanning) return;
                _lastPanRunningTime = DateTime.Now;

                // 方向锁定：只响应明确的水平滑动，垂直或近水平交给滚动控件
                // 防呆：只有位移超过阈值才判定方向，且要求水平分量明显大于垂直分量
                if (!_directionLocked)
                {
                    if (Math.Abs(e.TotalX) > DirectionLockThreshold || Math.Abs(e.TotalY) > DirectionLockThreshold)
                    {
                        _directionLocked = true;
                        // 判定为垂直滚动（垂直分量更大，或水平分量未达到垂直分量的 HorizontalRatio 倍）
                        // 这样在水平 CollectionView 上略微带垂直分量的滑动不会误触发 tab 切换
                        if (Math.Abs(e.TotalY) > Math.Abs(e.TotalX)
                            || Math.Abs(e.TotalX) < Math.Abs(e.TotalY) * HorizontalRatio)
                        {
                            _isPanning = false;
                            StopPanWatchdog();
                            // 判定为垂直滚动，放弃平移：关闭硬件层，避免长期占用 GPU 显存
                            SetHardwareLayersEnabled(false);
                            // 释放交互令牌，恢复正常更新
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

    /// <summary>启动滑动看门狗：防止滚动控件偷走触摸事件导致页面卡在中间</summary>
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
                    if (_isPanning)
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

    /// <summary>动画切换到指定页面</summary>
    private async Task AnimateToPage(int targetIndex)
    {
        if (targetIndex < 0 || targetIndex >= _tabPages.Count) return;

        var width = ViewPagerGrid.Width;
        if (width <= 0) return;

        // 切页动画前开启硬件层，整页平移由 GPU 合成
        SetHardwareLayersEnabled(true);

        var animations = new List<Task>();
        for (int i = 0; i < _tabPages.Count; i++)
        {
            if (ViewPagerGrid.Children[i] is not VisualElement view) continue;
            var targetX = (i - targetIndex) * width;
            animations.Add(view.TranslateTo(targetX, 0, AnimDuration, Easing.SinOut));
        }
        await Task.WhenAll(animations);

        // 动画结束：关闭硬件层，释放 GPU 显存，恢复正常渲染
        SetHardwareLayersEnabled(false);

        if (targetIndex != _currentIndex)
        {
            InvokeLifecycle(_tabPages[_currentIndex], "OnDisappearing");
            _currentIndex = targetIndex;
            InvokeLifecycle(_tabPages[_currentIndex], "OnAppearing");
            UpdateTabBarVisibility();
            UpdateTabBarSelection();
        }
    }

    /// <summary>弹回当前位置</summary>
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
            var targetX = (i - _currentIndex) * width;
            animations.Add(view.TranslateTo(targetX, 0, 200, Easing.SinOut));
        }
        await Task.WhenAll(animations);

        // 动画结束：关闭硬件层
        SetHardwareLayersEnabled(false);
    }

    /// <summary>程序化切换 tab（tab 索引 0-4，内部映射到 ViewPager index 1-5）</summary>
    public async void SwitchToTab(int tabIndex)
    {
        var vpIndex = tabIndex + 1;
        if (vpIndex < 0 || vpIndex >= _tabPages.Count || vpIndex == _currentIndex) return;
        await AnimateToPage(vpIndex);
    }

    /// <summary>切换到全屏歌词页（ViewPager index 0）</summary>
    public void SwitchToFullLyrics()
    {
        if (_currentIndex == 0) return;
        _ = AnimateToPage(0);
    }

    /// <summary>
    /// 收起播放页：播放页面向下平移，露出下层的发现页。
    /// 动画结束后内部切换到发现页（ViewPager index 2），并重置播放页位置。
    /// </summary>
    public async void CollapseNowPlaying()
    {
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
        UpdateTabBarSelection();
        UpdateSafeAreaPadding();
        UpdateMiniPlayerVisibility();

        // 重置播放页位置和 ZIndex：移到屏幕右侧（对应 ViewPager index 1 相对 index 2 的偏移）
        nowPlaying.TranslationX = (nowPlayingIndex - discoverIndex) * width;
        nowPlaying.TranslationY = 0;
        nowPlaying.ZIndex = 0;

        // 恢复裁剪
        ViewPagerGrid.IsClippedToBounds = true;

        SetHardwareLayersEnabled(false);
    }

    /// <summary>导航到指定 tab（静态方法，供外部调用。tab 索引 0-4）</summary>
    public static async Task GoToTabAsync(int tabIndex)
    {
        var currentRoute = Shell.Current.CurrentState.Location.ToString();
        if (currentRoute != "//main")
        {
            PendingTabIndex = tabIndex;
            await Shell.Current.GoToAsync("//main");
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
        if (sender == TabItem0) _ = AnimateToPage(1);
        else if (sender == TabItem1) _ = AnimateToPage(2);
        else if (sender == TabItem2) _ = AnimateToPage(3);
        else if (sender == TabItem3) _ = AnimateToPage(4);
    }

    /// <summary>全屏歌词页和播放页时隐藏 TabBar 和迷你播放器；AI 聊天模式仅隐藏 TabBar</summary>
    private void UpdateTabBarVisibility()
    {
        // index 0 = 全屏歌词, index 1 = 播放页，两者都全屏
        var isFullScreen = _currentIndex <= 1;
        // AI 聊天模式：隐藏 TabBar，但保留迷你播放器（显示在输入框上方）
        var searchVm = _services.GetService<SearchViewModel>();
        var isChatMode = searchVm?.IsChatMode == true;
        var hideTabBar = isFullScreen || isChatMode;
        // MAUI 11: IsVisible=false 在 Auto 行中可能不收缩行高，需要同时设置 HeightRequest=0
        TabBar.IsVisible = !hideTabBar;
        TabBar.HeightRequest = hideTabBar ? 0 : 64;
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
    /// - 非全屏页面：ViewPagerGrid 顶部留出状态栏高度；TabBar 底部留出导航栏高度
    /// </summary>
    private void UpdateSafeAreaPadding()
    {
        var top = SafeAreaHelper.TopInset;
        var bottom = SafeAreaHelper.BottomInset;
        var isFullScreen = _currentIndex <= 1;
        var searchVm = _services.GetService<SearchViewModel>();
        var isChatMode = searchVm?.IsChatMode == true;
        var hideTabBar = isFullScreen || isChatMode;

        // 非全屏页面：顶部留出状态栏高度保护内容
        ViewPagerGrid.Padding = isFullScreen ? new Thickness(0) : new Thickness(0, top, 0, 0);

        // TabBar 底部留出导航栏高度（全屏页面或聊天模式 TabBar 已隐藏）
        TabBar.Padding = new Thickness(0, 6, 0, hideTabBar ? 8 : bottom + 8);
    }

    /// <summary>迷你播放器仅在有当前歌曲且非全屏页、非聊天模式时显示（聊天模式下迷你播放器显示在聊天界面输入框上方）</summary>
    private void UpdateMiniPlayerVisibility()
    {
        var hasSong = !string.IsNullOrEmpty(_nowPlayingVm.Title);
        var searchVm = _services.GetService<SearchViewModel>();
        var isChatMode = searchVm?.IsChatMode == true;
        var visible = hasSong && _currentIndex > 1 && !isChatMode;
        MiniPlayer.IsVisible = visible;
        MiniPlayer.HeightRequest = visible ? 52 : 0;
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

    /// <summary>点击迷你播放器非按钮区域（封面/标题）跳转到播放页</summary>
    private void OnMiniPlayerTapped(object? sender, EventArgs e)
    {
        _ = AnimateToPage(1);
    }

    private static readonly string[] DarkIconSources = { "ic_play", "ic_home", "ic_playlist", "ic_library" };

    /// <summary>更新 TabBar 选中状态（Tab 0-3 对应 ViewPager index 1-4）</summary>
    private void UpdateTabBarSelection()
    {
        var primaryColor = (Color)Application.Current!.Resources["PrimaryColor"];
        var inactiveColor = (Color)Application.Current!.Resources["TabInactiveColor"];

        var labels = new[] { TabLabel0, TabLabel1, TabLabel2, TabLabel3 };
        var icons = new[] { TabIcon0, TabIcon1, TabIcon2, TabIcon3 };
        var contents = new[] { TabContent0, TabContent1, TabContent2, TabContent3 };

        for (int i = 0; i < 4; i++)
        {
            var isActive = (i + 1) == _currentIndex;
            labels[i].TextColor = isActive ? primaryColor : inactiveColor;

            if (isActive)
            {
                icons[i].Source = DarkIconSources[i];
                icons[i].Scale = 1.15;
                labels[i].Scale = 1.1;
            }
            else
            {
                icons[i].Scale = 1.0;
                labels[i].Scale = 1.0;
                AppThemeBinding lightDarkBinding = new()
                {
                    Light = $"{DarkIconSources[i]}_light",
                    Dark = DarkIconSources[i]
                };
                icons[i].SetValue(Image.SourceProperty, lightDarkBinding);
            }
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
            System.Diagnostics.Debug.WriteLine($"[MainPage] InvokeLifecycle {methodName} FAILED: {ex.Message}");
        }
    }
}
