using System.Reflection;
using System.Linq;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Maui.ViewModels;
using System.ComponentModel;
using Microsoft.Maui.Storage;

namespace CatClawMusic.Maui.Pages;

/// <summary>
/// 主页面：包含 5 个 tab 页面的 ViewPager + 自定义底部 TabBar + 迷你播放器。
/// 所有 tab 页面水平排列在同一 Grid 中，滑动时同时移动，实现连续平滑切换。
/// </summary>
public partial class MainPage : ContentPage
{
    private readonly IServiceProvider _services;
    private readonly List<ContentPage> _tabPages = new();
    private readonly NowPlayingViewModel _nowPlayingVm;
    // ViewPager 布局: [FullLyrics(0), NowPlaying(1), Search(2), Playlist(3), Library(4), Settings(5)]
    // TabBar 的 5 个按钮对应 index 1-5，index 0 是全屏歌词（无 Tab 按钮）
    private int _currentIndex = 1;
    private double _panTotalX;
    private bool _isPanning;
    private bool _directionLocked;
    private bool _isFirstLoad = true;
    private const double SwipeThresholdRatio = 0.25;
    private const int AnimDuration = 280;

    /// <summary>全局实例，供外部调用 SwitchToTab</summary>
    public static MainPage? Instance { get; private set; }

    /// <summary>待切换的 tab 索引（从子页面返回时使用）</summary>
    public static int? PendingTabIndex;

    public MainPage(IServiceProvider services, NowPlayingViewModel nowPlayingVm)
    {
        InitializeComponent();
        _services = services;
        _nowPlayingVm = nowPlayingVm;
        Instance = this;

        // 迷你播放器绑定到 NowPlayingViewModel
        MiniPlayer.BindingContext = _nowPlayingVm;
        _nowPlayingVm.PropertyChanged += OnNowPlayingPropertyChanged;

        SetupPages();
        ViewPagerGrid.SizeChanged += OnViewPagerSizeChanged;
    }

    /// <summary>创建 6 个页面（全屏歌词 + 5 个 tab），提取 Content 放入 ViewPager</summary>
    private void SetupPages()
    {
        var pages = new ContentPage[]
        {
            _services.GetRequiredService<FullLyricsPage>(),
            _services.GetRequiredService<NowPlayingPage>(),
            _services.GetRequiredService<SearchPage>(),
            _services.GetRequiredService<PlaylistPage>(),
            _services.GetRequiredService<LibraryPage>(),
            _services.GetRequiredService<SettingsPage>(),
        };

        foreach (var page in pages)
        {
            _tabPages.Add(page);
            var content = page.Content;
            page.Content = null;
            content.BindingContext = page.BindingContext;

            ForceVerticalScroll(content);

            var panGesture = new PanGestureRecognizer();
            panGesture.PanUpdated += OnPanUpdated;
            AddPanToLayouts(content, panGesture);

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

    /// <summary>递归给所有 Layout 子元素添加 PanGestureRecognizer，确保空白区域也能滑动</summary>
    private static void AddPanToLayouts(VisualElement element, PanGestureRecognizer panGesture)
    {
        if (element is Layout layout && element is not ScrollView && element is not Slider)
        {
            layout.GestureRecognizers.Add(panGesture);
            foreach (var child in layout.Children.OfType<VisualElement>())
            {
                AddPanToLayouts(child, panGesture);
            }
        }
    }

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

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        InvokeLifecycle(_tabPages[_currentIndex], "OnDisappearing");
    }

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

    /// <summary>PanGestureRecognizer 跟手滑动处理</summary>
    private async void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panTotalX = 0;
                _isPanning = true;
                _directionLocked = false;
                break;

            case GestureStatus.Running:
                if (!_isPanning) return;

                // 方向锁定：只响应水平滑动，垂直交给 ScrollView
                if (!_directionLocked)
                {
                    if (Math.Abs(e.TotalX) > 10 || Math.Abs(e.TotalY) > 10)
                    {
                        _directionLocked = true;
                        if (Math.Abs(e.TotalY) > Math.Abs(e.TotalX))
                        {
                            _isPanning = false;
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
                break;

            case GestureStatus.Canceled:
                _isPanning = false;
                await BounceBack();
                break;
        }
    }

    /// <summary>动画切换到指定页面</summary>
    private async Task AnimateToPage(int targetIndex)
    {
        if (targetIndex < 0 || targetIndex >= _tabPages.Count) return;

        var width = ViewPagerGrid.Width;
        if (width <= 0) return;

        var animations = new List<Task>();
        for (int i = 0; i < _tabPages.Count; i++)
        {
            if (ViewPagerGrid.Children[i] is not VisualElement view) continue;
            var targetX = (i - targetIndex) * width;
            animations.Add(view.TranslateTo(targetX, 0, AnimDuration, Easing.SinOut));
        }
        await Task.WhenAll(animations);

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

        var animations = new List<Task>();
        for (int i = 0; i < _tabPages.Count; i++)
        {
            if (ViewPagerGrid.Children[i] is not VisualElement view) continue;
            var targetX = (i - _currentIndex) * width;
            animations.Add(view.TranslateTo(targetX, 0, 200, Easing.SinOut));
        }
        await Task.WhenAll(animations);
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

    private void OnTabTapped(object? sender, TappedEventArgs e)
    {
        // Tab 按钮 0-4 对应 ViewPager index 1-5
        if (sender == TabItem0) _ = AnimateToPage(1);
        else if (sender == TabItem1) _ = AnimateToPage(2);
        else if (sender == TabItem2) _ = AnimateToPage(3);
        else if (sender == TabItem3) _ = AnimateToPage(4);
        else if (sender == TabItem4) _ = AnimateToPage(5);
    }

    /// <summary>全屏歌词页和播放页时隐藏 TabBar 和迷你播放器</summary>
    private void UpdateTabBarVisibility()
    {
        // index 0 = 全屏歌词, index 1 = 播放页，两者都全屏
        var isFullScreen = _currentIndex <= 1;
        // MAUI 11: IsVisible=false 在 Auto 行中可能不收缩行高，需要同时设置 HeightRequest=0
        TabBar.IsVisible = !isFullScreen;
        TabBar.HeightRequest = isFullScreen ? 0 : 64;
        UpdateMiniPlayerVisibility();
    }

    /// <summary>迷你播放器仅在有当前歌曲且非全屏页时显示</summary>
    private void UpdateMiniPlayerVisibility()
    {
        var hasSong = !string.IsNullOrEmpty(_nowPlayingVm.Title);
        var visible = hasSong && _currentIndex > 1;
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
    private void OnMiniPlayerTapped(object? sender, TappedEventArgs e)
    {
        _ = AnimateToPage(1);
    }

    private static readonly string[] DarkIconSources = { "ic_play", "ic_home", "ic_playlist", "ic_library", "ic_settings" };

    /// <summary>更新 TabBar 选中状态（Tab 0-4 对应 ViewPager index 1-5）</summary>
    private void UpdateTabBarSelection()
    {
        var primaryColor = (Color)Application.Current!.Resources["PrimaryColor"];
        var inactiveColor = (Color)Application.Current!.Resources["TabInactiveColor"];

        var labels = new[] { TabLabel0, TabLabel1, TabLabel2, TabLabel3, TabLabel4 };
        var icons = new[] { TabIcon0, TabIcon1, TabIcon2, TabIcon3, TabIcon4 };
        var bgs = new[] { TabBg0, TabBg1, TabBg2, TabBg3, TabBg4 };

        for (int i = 0; i < 5; i++)
        {
            var isActive = (i + 1) == _currentIndex;
            labels[i].TextColor = isActive ? primaryColor : inactiveColor;
            bgs[i].Opacity = isActive ? 1.0 : 0.0;

            if (isActive)
            {
                icons[i].Source = DarkIconSources[i];
                icons[i].Scale = 1.1;
            }
            else
            {
                icons[i].Scale = 1.0;
                AppThemeBinding lightDarkBinding = new()
                {
                    Light = $"{DarkIconSources[i]}_light",
                    Dark = DarkIconSources[i]
                };
                icons[i].SetValue(Image.SourceProperty, lightDarkBinding);
            }
        }
    }

    /// <summary>通过反射调用 ContentPage 的 OnAppearing/OnDisappearing</summary>
    private static void InvokeLifecycle(ContentPage page, string methodName)
    {
        try
        {
            // 用 GetMethods 替代 GetMethod，避免 .NET 10 中基类新增重载导致 AmbiguousMatchException
            var method = page.GetType().GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == 0);

            // 如果派生类没有 override，回退到基类方法
            if (method == null)
            {
                method = typeof(ContentPage).GetMethods(
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == 0);
            }

            System.Diagnostics.Debug.WriteLine($"[MainPage] InvokeLifecycle {methodName} on {page.GetType().Name}, method found={method != null}");
            method?.Invoke(page, null);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainPage] InvokeLifecycle {methodName} FAILED: {ex.Message}");
        }
    }
}
