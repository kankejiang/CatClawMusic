using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Pages;

/// <summary>
/// Desktop (Windows) main page: left sidebar + content area + bottom player bar.
/// Modeled after NetEase Cloud Music PC client layout, with PC-only enhancements:
/// sidebar playlists, top command-bar search, keyboard shortcuts, responsive sidebar,
/// and right-click context menus (replacing mobile swipe gestures).
/// </summary>
public partial class DesktopMainPage : ContentPage, ISongContextMenuHost
{
    private readonly NowPlayingViewModel _npVm;
    private readonly IServiceProvider _services;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly PlaylistViewModel _playlistVm;
    private readonly PlaylistDetailViewModel _playlistDetailVm;

    private enum DesktopTab { Discover, Library, Playlists, Settings }
    private DesktopTab _currentTab = DesktopTab.Discover;

    // Cached page contents
    private readonly Dictionary<DesktopTab, View> _pageCache = new();
    // 缓存每个 tab 对应的原始 ContentPage，用于调用 OnAppearing/OnDisappearing 生命周期
    private readonly Dictionary<DesktopTab, ContentPage> _pageHostCache = new();

    // 当前嵌入 ContentArea 的子页面（如 DesktopAllSongsPage），非 null 时表示有子页面覆盖在 tab 内容上
    private ContentPage? _embeddedSubPage;

    // 侧边栏歌单名称标签（用于响应式折叠时隐藏）
    private readonly List<Label> _playlistNameLabels = new();

    // 防止 BuildPlaylistList 并发执行导致歌单重复渲染
    private readonly object _playlistListLock = new();

    // 响应式：窄窗折叠为图标栏
    private bool _compact;
    private const double SidebarWidth = 220;
    private const double AndroidSidebarWidth = 168; // 横屏手机复用桌面布局时侧栏更窄
    private const double CompactThreshold = 1000;

    /// <summary>全局实例，供嵌入的子页面（如 SearchPage）请求切换 tab</summary>
    public static DesktopMainPage? Instance { get; private set; }

    /// <summary>窗口级根网格（全窗覆盖层宿主：弹窗临时挂载于此可覆盖侧栏/播放条，不受 ContentArea 裁剪影响）。</summary>
    public Grid WindowRoot => RootGrid;

    /// <summary>
    /// 歌曲上下文菜单宿主转发：嵌入子页面的 Content 被摘出后，行父链走不到子页面，
    /// 只能走到本壳页面——这里转发给当前嵌入的子页面。
    /// </summary>
    public void ShowSongMenu(Song song, View row, Point position)
    {
        if (_embeddedSubPage is ISongContextMenuHost host)
            host.ShowSongMenu(song, row, position);
    }

    public DesktopMainPage(NowPlayingViewModel npVm, IServiceProvider services)
    {
        InitializeComponent();
#if ANDROID
        // 隐藏侧栏顶部的 App Logo 区，降低底部播放器高度，并压缩手机横屏下的控件尺寸
        SidebarLogo.IsVisible = false;
        // 浮层化后播放条含底部 16dp 留白（Margin），行高 76 → 卡片可视高 60（接近原 64dp 播放条）
        RootGrid.RowDefinitions[2].Height = new GridLength(76);
        ApplyCompactPlayerBarMetrics();
        // Android 横屏复用桌面布局：侧栏保持完整宽度（与竖屏版一致），不折叠为图标栏
        _compact = false;
        RootGrid.ColumnDefinitions[0].Width = new GridLength(AndroidSidebarWidth);
#endif

        // 顶部搜索/命令栏在两个平台都移除：发现页已有独立搜索框，顶部为重复入口
        // （其中的歌词图标随之消失，如需保留可另加到播放栏）
        TopBar.IsVisible = false;
        _npVm = npVm;
        _services = services;
        _audioPlayer = services.GetRequiredService<IAudioPlayerService>();
        _playlistVm = services.GetRequiredService<PlaylistViewModel>();
        _playlistDetailVm = services.GetRequiredService<PlaylistDetailViewModel>();
        BindingContext = _npVm;
        Instance = this;

        #if WINDOWS
            // Windows: 0 占位（透明标题栏控件覆盖顶部，仅右上显示系统 caption 按钮）
            RootGrid.RowDefinitions[0].Height = new GridLength(0);
#else
            // Android: no title bar area
            RootGrid.RowDefinitions[0].Height = new GridLength(0);
#endif
            SizeChanged += OnPageSizeChanged;
        InitVolumeSlider();

#if ANDROID || WINDOWS
        // RootGrid 底部需要留出系统栏安全区：Android 为导航栏，Windows 为任务栏（底部 dock 栏）。
        // SafeAreaHelper 由平台在系统栏尺寸变化时异步更新，订阅变化事件以确保首帧即正确。
        ApplyRootGridSafeArea();
#endif

        // 静态/单例事件：通过 HandlerChanged 管理订阅生命周期，支持页面实例复用（Singleton）。
        HandlerChanged += (_, _) =>
        {
            if (Handler == null)
            {
#if ANDROID || WINDOWS
                SafeAreaHelper.SafeAreaChanged -= OnSafeAreaChanged;
#endif
            }
            else
            {
#if ANDROID || WINDOWS
                SafeAreaHelper.SafeAreaChanged -= OnSafeAreaChanged;
                SafeAreaHelper.SafeAreaChanged += OnSafeAreaChanged;
#endif
            }
        };

        // 构造时仅创建默认 tab 内容，不触发生命周期（页面尚未进入可视树）
        _currentTab = DesktopTab.Discover;
        UpdateNavHighlight();
        if (!_pageCache.TryGetValue(_currentTab, out var content))
        {
            content = CreatePageContent(_currentTab);
            if (content != null)
                _pageCache[_currentTab] = content;
        }
        if (content != null)
            ContentArea.Children.Add(content);

        // 初始即应用底部预留（播放条高度 Android 64 / Windows 100），使发现页推荐艺人可滚到播放条上方
        UpdateDiscoverBottomReserve();

        _ = LoadPlaylistsAsync();
    }

    /// <summary>随底部播放条当前高度联动调整发现页滚动内容底部预留，避免推荐艺人被播放条遮挡。</summary>
    private void UpdateDiscoverBottomReserve()
    {
        var barHeight = RootGrid.RowDefinitions[2].Height.Value;
        if (_pageHostCache.TryGetValue(DesktopTab.Discover, out var host)
            && host is DesktopDiscoverPage discover)
        {
            discover.SetBottomReservedHeight(barHeight);
        }
    }

    private bool _isFirstAppearing = true;

#if ANDROID || WINDOWS
    /// <summary>SafeArea 变化时刷新 RootGrid 安全区内边距。</summary>
    private void OnSafeAreaChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(ApplyRootGridSafeArea);
    }

    /// <summary>
    /// 自适应安全区：直接使用系统 insets 实际值。
    /// Android：横屏状态栏隐藏 → TopInset=0；车机状态栏可见 → TopInset=实际高度。底部为导航栏高度。
    /// Windows：TopInset 恒为 0；BottomInset 为任务栏（底部 dock 栏）高度（由 App.UpdateWindowsSafeArea 写入）。
    /// 子页面嵌入全屏模式时清零。
    /// </summary>
    private void ApplyRootGridSafeArea()
    {
        if (_embeddedSubPage != null)
        {
            // 子页面全屏嵌入：状态栏已隐藏，顶部/左右不留白；
            // 但底部导航栏/dock（车机 dock、三键导航）仍存在，必须保留底部 inset，
            // 否则嵌入的列表页（全部歌曲/艺术家/专辑）底部内容会被系统栏遮挡。
            RootGrid.Padding = new Thickness(0, 0, 0, SafeAreaHelper.BottomInset);
            return;
        }

        // 完全跟随系统 insets：有系统栏就留白，没有就不留
        RootGrid.Padding = new Thickness(0, SafeAreaHelper.TopInset, 0, SafeAreaHelper.BottomInset);
    }

    /// <summary>
    /// 内容区安全区已由 RootGrid 统一处理，此方法仅用于嵌入子页面时清零 ContentArea padding。
    /// </summary>
    private void ApplyContentAreaSafeArea()
    {
        // 嵌入子页面时全屏显示，不额外添加顶部安全区
        ContentArea.Padding = _embeddedSubPage != null ? new Thickness(0) : new Thickness(0);
    }
#endif

    protected override void OnAppearing()
    {
        base.OnAppearing();

        _ = _npVm.LoadCurrentSongAsync();
        // 主题色可能在设置页切换后返回，这里按当前主题/深浅模式刷新播放控制条图标
        _npVm.RefreshPlayerCtrlIcons();
        // 回到桌面主页时刷新歌单（例如从歌单详情页返回）
        _ = _playlistVm.RefreshIfChangedAsync()
            .ContinueWith(_ => MainThread.BeginInvokeOnMainThread(BuildPlaylistList));
        AttachKeyboard();

        // 首次显示时触发默认 tab 的 OnAppearing 以加载数据
        if (_isFirstAppearing)
        {
            _isFirstAppearing = false;
            if (_pageHostCache.TryGetValue(_currentTab, out var host))
                InvokeLifecycle(host, "OnAppearing");
        }

        // 单例页面复用：每次显示都按当前播放条高度重设发现页底部预留，
        // 防止横竖屏切换后 ApplyResponsiveMetrics 或高度变化导致推荐艺人被遮挡
        UpdateDiscoverBottomReserve();
    }

#if ANDROID
    /// <summary>系统返回键：退出手动横屏，回到竖屏手机布局。
    /// ReleaseManualLandscape 解除方向锁定并触发 Activity 重建（或直接切换布局）。</summary>
    protected override bool OnBackButtonPressed()
    {
        // 如果有嵌入子页面，先关闭它并恢复状态栏/播放栏
        if (_embeddedSubPage != null)
        {
            CloseEmbeddedSubPage("library");
            return true;
        }

        // 退出横屏前恢复状态栏
        ShowSystemStatusBar();
        if (Application.Current is App app)
            app.ReleaseManualLandscape();
        return true;
    }
#endif

    // ─── Navigation ───

    private void OnNavDiscoverTapped(object? sender, TappedEventArgs e) => SwitchTab(DesktopTab.Discover);
    private void OnNavLibraryTapped(object? sender, TappedEventArgs e) => SwitchTab(DesktopTab.Library);
    private void OnNavPlaylistsTapped(object? sender, TappedEventArgs e) => SwitchTab(DesktopTab.Playlists);
    private void OnNavSettingsTapped(object? sender, TappedEventArgs e) => SwitchTab(DesktopTab.Settings);

    /// <summary>切换到指定名称的 tab（供嵌入的子页面跨平台调用，name 不区分大小写）</summary>
    public void SwitchToNamedTab(string name, bool animate = true)
    {
        var tab = name?.ToLowerInvariant() switch
        {
            "discover" or "search" => DesktopTab.Discover,
            "library" => DesktopTab.Library,
            "playlists" => DesktopTab.Playlists,
            "settings" => DesktopTab.Settings,
            _ => DesktopTab.Discover
        };
        SwitchTab(tab, animate);
    }

    private void SwitchTab(DesktopTab tab, bool animate = true)
    {
        // 如果有嵌入的子页面，先通知它 OnDisappearing 并清理，同时恢复底部播放栏和状态栏
        if (_embeddedSubPage != null)
        {
            InvokeLifecycle(_embeddedSubPage, "OnDisappearing");
            _embeddedSubPage = null;
            // 恢复侧栏与列宽（播放/歌词页全屏内嵌时被隐藏）
            SidebarBorder.IsVisible = true;
#if ANDROID
            RootGrid.ColumnDefinitions[0].Width = new GridLength(AndroidSidebarWidth);
#else
            RootGrid.ColumnDefinitions[0].Width = new GridLength(SidebarWidth);
#endif
            PlayerBarBorder.IsVisible = true;
#if ANDROID
            RootGrid.RowDefinitions[2].Height = new GridLength(76);
            ApplyContentAreaSafeArea(); // 恢复 tab 内容的顶部状态栏安全区
#else
            RootGrid.RowDefinitions[2].Height = new GridLength(100);
#endif
            ShowSystemStatusBar();
            UpdateDiscoverBottomReserve();
        }

        // 通知旧 tab 消失（触发数据加载等生命周期）
        if (_pageHostCache.TryGetValue(_currentTab, out var oldHost))
            InvokeLifecycle(oldHost, "OnDisappearing");

        _currentTab = tab;
        UpdateNavHighlight();

        if (!_pageCache.TryGetValue(tab, out var content))
        {
            content = CreatePageContent(tab);
            if (content != null)
                _pageCache[tab] = content;
        }

        // fade-through 过渡：旧 tab 内容顶层快速淡出揭幕（内容被 _pageCache 缓存复用，动画后复位）
        View? oldContent = animate && content != null && ContentArea.Children.Count > 0 && ContentArea.Width > 10
            ? ContentArea.Children[^1] as View : null;

        ContentArea.Children.Clear();
        if (content != null)
            ContentArea.Children.Add(content);
        if (oldContent != null)
        {
            ContentArea.Children.Add(oldContent);
            DesktopTransitions.FadeThrough(ContentArea, content!, oldContent);
        }

        // 通知新 tab 显示（SearchPage/LibraryPage 等在此加载数据）
        if (_pageHostCache.TryGetValue(tab, out var newHost))
            InvokeLifecycle(newHost, "OnAppearing");

        // 播放条高度可能已变化，切页后按当前高度刷新发现页底部预留
        UpdateDiscoverBottomReserve();
    }

    private View? CreatePageContent(DesktopTab tab)
    {
        ContentPage? page = tab switch
        {
            DesktopTab.Discover => _services.GetRequiredService<DesktopDiscoverPage>(),
            DesktopTab.Library => _services.GetRequiredService<DesktopLibraryPage>(),
            DesktopTab.Playlists => _services.GetRequiredService<DesktopPlaylistPage>(),
            DesktopTab.Settings => _services.GetRequiredService<DesktopSettingsPage>(),
            _ => null
        };

        if (page == null) return null;
        _pageHostCache[tab] = page;

        // Extract content from the page and rebind
        var content = page.Content;
        page.Content = null;
        content.BindingContext = page.BindingContext;
        content.VerticalOptions = LayoutOptions.Fill;
        content.HorizontalOptions = LayoutOptions.Fill;

        // 自带纵向滚动区域的页面（固定页头 + 占满剩余高度的 CollectionView/ListView）绝对不能
        // 再包一层 ScrollView：外层 ScrollView 会把内层 CollectionView 的高度撑成无限，导致虚拟化
        // 失效、一次性创建全部歌曲行并加载全部封面，大曲库下「我的音乐 / 歌单」会卡好几秒。
        // 这类页面直接放进有界高度的 ContentArea，让内部 CollectionView 自行滚动即可。
        // DesktopDiscoverPage 发现模式由内部 ScrollView 滚动；聊天模式必须固定整页
        // （消息列表内部滚动、输入框/顶栏固定），若再包外层 ScrollView，聊天列表滚到边界后
        // 滚动链会带动整页（header/输入框跟着聊天记录滚动）。
        if (content is ScrollView
            || page is LibraryPage or PlaylistPage or PlaylistDetailPage
            || page is DesktopPlaylistPage or DesktopLibraryPage
            || page is DesktopArtistsPage or DesktopAlbumsPage or DesktopAllSongsPage
            || page is DesktopDiscoverPage)
        {
            return content;
        }
        return new ScrollView { Content = content };
    }

    /// <summary>通过反射调用 ContentPage 的 OnAppearing/OnDisappearing（嵌入到 ContentArea 的页面不会自动触发生命周期）</summary>
    private static void InvokeLifecycle(ContentPage page, string methodName)
    {
        try
        {
            var method = page.GetType().GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == 0);
            if (method == null)
            {
                method = typeof(ContentPage).GetMethods(
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == 0);
            }
            method?.Invoke(page, null);
        }
        catch (Exception ex)
        {
            Log.Debug("DesktopMainPage.xaml", $"[Desktop] InvokeLifecycle {methodName} on {page.GetType().Name} FAILED: {ex.Message}");
        }
    }

    private void UpdateNavHighlight()
    {
        var res = Application.Current?.Resources;
        var highlightBg = (res?["NavHighlightBrush"] as Brush) ?? new SolidColorBrush(Colors.Purple);
        var highlightFg = (Color)(res?["NavHighlightTextColor"] ?? Color.FromArgb("#0F0F14"));
        var normalFg = (Color)(res?["TextPrimaryColor"] ?? Colors.White);

        ApplyNavState(NavDiscover, NavDiscoverLabel, NavDiscoverIcon, "ic_home",
            _currentTab == DesktopTab.Discover, highlightBg, highlightFg, normalFg);
        ApplyNavState(NavPlaylists, NavPlaylistsLabel, NavPlaylistsIcon, "ic_playlist",
            _currentTab == DesktopTab.Playlists, highlightBg, highlightFg, normalFg);
        ApplyNavState(NavLibrary, NavLibraryLabel, NavLibraryIcon, "ic_library",
            _currentTab == DesktopTab.Library, highlightBg, highlightFg, normalFg);
        // 设置入口已从侧栏移入各页面（发现页头部右上角），侧栏不再渲染/高亮设置项
    }

    /// <summary>方案A晨雾框架导航选中态。图标跟随文字颜色切换（MAUI Image 无 TintColor，靠换深/浅图标源）：
    /// <para>浅色模式：未选中项用主题色文字 + 深色图标，选中项用白色文字 + 白色图标（渐变高亮底上白字清晰）。</para>
    /// <para>深色模式：选中用深色文字 + <c>_light</c> 深色图标，未选中用主题文字色。</para></summary>
    private void ApplyNavState(Border border, Label? label, Image icon, string baseIcon,
        bool active, Brush highlightBg, Color highlightFg, Color normalFg)
    {
        var isLight = Application.Current?.RequestedTheme == Microsoft.Maui.ApplicationModel.AppTheme.Light;
        border.Background = active ? highlightBg : Brush.Transparent;

        if (isLight)
        {
            // 浅色模式：未选中=主题色文字+主题色图标；选中=白色文字+白色图标
            if (label != null) label.TextColor = active ? Colors.White : (Color)(Application.Current?.Resources?["PrimaryColor"] ?? Colors.Purple);
            icon.Source = active
                ? ImageSourceHelper.FromName(baseIcon)                     // 原版白色图标
                : ImageSourceHelper.FromNamePlayerCtrl(baseIcon, baseIcon + "_light"); // 主题色变体，缺时回退深色图标
        }
        else
        {
            // 深色模式：保持原逻辑
            if (label != null) label.TextColor = active ? highlightFg : normalFg;
            icon.Source = active
                ? ImageSourceHelper.FromName(baseIcon + "_light")
                : ImageSourceHelper.FromNameThemed(baseIcon);
        }
    }

    // ─── Sidebar Playlists ───

    private async Task LoadPlaylistsAsync()
    {
        await _playlistVm.LoadPlaylistsAsync();
        BuildPlaylistList();
    }

    private void BuildPlaylistList()
    {
        lock (_playlistListLock)
        {
            PlaylistHost.Children.Clear();
            _playlistNameLabels.Clear();

            foreach (var pl in _playlistVm.Playlists)
            {
                PlaylistHost.Children.Add(CreatePlaylistRow(pl));
            }
        }
    }

    private View CreatePlaylistRow(Playlist pl)
    {
        var nameLabel = new Label
        {
            Text = pl.Name,
            FontSize = 13,
            VerticalOptions = LayoutOptions.Center,
            TextColor = (Color)(Microsoft.Maui.Controls.Application.Current?.Resources["TextPrimaryColor"] ?? Colors.Black),
            LineBreakMode = LineBreakMode.TailTruncation,
        };
        _playlistNameLabels.Add(nameLabel);

        var moreButton = new Label
        {
            Text = "⋮",
            FontSize = 18,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            TextColor = (Color)(Microsoft.Maui.Controls.Application.Current?.Resources["TextHintColor"] ?? Colors.Gray),
            WidthRequest = 24,
            HeightRequest = 28,
        };

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 10,
            Padding = new Thickness(12, 8),
            BackgroundColor = Colors.Transparent,
        };

        var icon = new Label
        {
            Text = pl.IsSystem ? "♫" : "📃",
            FontSize = 15,
            VerticalOptions = LayoutOptions.Center,
        };
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(nameLabel, 1);
        Grid.SetColumn(moreButton, 2);
        row.Children.Add(icon);
        row.Children.Add(nameLabel);
        row.Children.Add(moreButton);

        // 整行点击打开歌单
        var rowTap = new TapGestureRecognizer();
        rowTap.Tapped += (_, _) => OpenPlaylist(pl);
        row.GestureRecognizers.Add(rowTap);

        // ⋮ 按钮弹出操作菜单（替代手机端滑动手势；跨平台使用 DisplayActionSheet）
        var moreTap = new TapGestureRecognizer();
        moreTap.Tapped += async (_, _) => await ShowPlaylistActionsAsync(pl);
        moreButton.GestureRecognizers.Add(moreTap);

        return new Border
        {
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            Content = row,
        };
    }

    private async Task ShowPlaylistActionsAsync(Playlist pl)
    {
        var buttons = new List<string> { "打开" };
        if (!pl.IsSystem)
        {
            buttons.Add("播放");
            buttons.Add("重命名");
            buttons.Add("删除");
        }

        var choice = await DisplayActionSheet("歌单操作", "取消", null, buttons.ToArray());
        switch (choice)
        {
            case "打开":
                OpenPlaylist(pl);
                break;
            case "播放":
                await PlayPlaylistAsync(pl);
                break;
            case "重命名":
                await RenamePlaylistAsync(pl);
                break;
            case "删除":
                await DeletePlaylistAsync(pl);
                break;
        }
    }

    private void OpenPlaylist(Playlist pl)
    {
#if WINDOWS
        OpenPlaylistEmbedded(pl);
#else
        DesktopNavigation.TryGoToShell(
            $"playlistdetail?playlistId={pl.Id}&name={Uri.EscapeDataString(pl.Name)}");
#endif
    }

    /// <summary>Windows 桌面端：将歌单详情页嵌入右侧 ContentArea，保留左侧导航栏。</summary>
    private void OpenPlaylistEmbedded(Playlist pl)
    {
        try
        {
            var page = _services.GetRequiredService<PlaylistDetailPage>();
            page.PlaylistId = pl.Id;
            page.PlaylistName = pl.Name;

            // 桌面嵌入模式下，隐藏左上角返回按钮（左侧导航栏已提供全局导航）
            if (page.Content is Grid root)
            {
                var backButton = root.Children
                    .OfType<CatClawMusic.Maui.Controls.BackButton>()
                    .FirstOrDefault();
                if (backButton != null)
                    backButton.IsVisible = false;
            }

            var content = page.Content;
            if (content == null) return;
            page.Content = null;
            content.BindingContext = page.BindingContext;

            View? outgoing = ContentArea.Children.Count > 0 && ContentArea.Width > 10
                ? ContentArea.Children[^1] as View : null;
            ContentArea.Children.Clear();
            if (outgoing != null)
                ContentArea.Children.Add(outgoing); // 旧内容垫底（视差滑出层）
            ContentArea.Children.Add(content);

            InvokeLifecycle(page, "OnAppearing");

            if (outgoing != null)
                DesktopTransitions.PushSwap(ContentArea, content, outgoing, fromLeft: false);
        }
        catch (Exception ex)
        {
            Log.Debug("DesktopMainPage.xaml", $"[Desktop] OpenPlaylistEmbedded failed: {ex}");
        }
    }

    /// <summary>将音乐库子页面（DesktopAllSongsPage/DesktopArtistsPage/DesktopAlbumsPage）嵌入 ContentArea，
    /// 同时隐藏底部播放栏以最大化内容区域。点击侧边栏任意 tab 或子页面返回按钮即可恢复。
    /// 播放页/全屏歌词页内嵌时为全屏沉浸：隐藏左侧栏并把内容区横向拉满整个舞台。</summary>
    public void OpenSubPageEmbedded(ContentPage page)
    {
        // 播放/歌词页全屏沉浸：隐藏侧栏 + 列宽归零（Android 横屏播放页不带侧栏）
        bool immersivePlayer = page is NowPlayingPage or FullLyricsPage;
        if (immersivePlayer)
        {
            SidebarBorder.IsVisible = false;
            RootGrid.ColumnDefinitions[0].Width = new GridLength(0);
        }

        // 通知当前嵌入的子页面 OnDisappearing（连续打开多个子页面时）
        if (_embeddedSubPage != null)
            InvokeLifecycle(_embeddedSubPage, "OnDisappearing");
        else if (_pageHostCache.TryGetValue(_currentTab, out var currentHost))
            InvokeLifecycle(currentHost, "OnDisappearing");

        // 过渡判定：区域已布局（宽度>10）。旋转首次借入舞台时 ContentArea 尚未布局
        // （DesktopStage.IsVisible 在调用之后才置 true）→ 宽度为 0，自然退化为瞬时切换不抖动。
        bool animate = ContentArea.Children.Count > 0 && ContentArea.Width > 10;
        View? outgoing = animate ? ContentArea.Children[^1] as View : null;

        _embeddedSubPage = page;

        var content = page.Content;
        page.Content = null;
        content.BindingContext = page.BindingContext;
        content.VerticalOptions = LayoutOptions.Fill;
        content.HorizontalOptions = LayoutOptions.Fill;

        ContentArea.Children.Clear();
#if ANDROID || WINDOWS
        // 子页面全屏展示，移除 RootGrid 安全区 padding
        ApplyRootGridSafeArea();
#endif
        if (outgoing != null)
            ContentArea.Children.Add(outgoing); // 旧内容垫底（视差滑出层）
        ContentArea.Children.Add(content);

        // 隐藏底部播放栏，让子页面获得最大内容展示空间
        PlayerBarBorder.IsVisible = false;
        RootGrid.RowDefinitions[2].Height = new GridLength(0);

        // 隐藏系统状态栏，最大化内容展示区域
        HideSystemStatusBar();

        InvokeLifecycle(page, "OnAppearing");

        if (outgoing != null)
            DesktopTransitions.PushSwap(ContentArea, content, outgoing, fromLeft: page is FullLyricsPage);
    }

    /// <summary>当前是否有全屏子页面占据内容区（播放/歌词页横屏内嵌时为 true）。</summary>
    public bool HasEmbeddedSubPage => _embeddedSubPage != null;

    /// <summary>关闭嵌入的子页面，恢复到指定 tab（默认音乐库）并恢复底部播放栏显示。</summary>
    public void CloseEmbeddedSubPage(string returnTab = "library")
    {
        // 过渡：沉浸页退场方向 = 它进入时的方向（歌词页从左回退、播放页从右回退）
        View? outgoing = null;
        bool exitLeft = false;
        if (_embeddedSubPage != null)
        {
            exitLeft = _embeddedSubPage is FullLyricsPage;
            if (ContentArea.Width > 10 && ContentArea.Children.Count > 0)
                outgoing = ContentArea.Children[^1] as View;
            InvokeLifecycle(_embeddedSubPage, "OnDisappearing");
            _embeddedSubPage = null;
        }

        // 恢复侧栏与列宽（播放/歌词页全屏内嵌时被隐藏）
        SidebarBorder.IsVisible = true;
#if ANDROID
        RootGrid.ColumnDefinitions[0].Width = new GridLength(AndroidSidebarWidth);
#else
        RootGrid.ColumnDefinitions[0].Width = new GridLength(SidebarWidth);
#endif

        // 恢复底部播放栏
        PlayerBarBorder.IsVisible = true;
#if ANDROID
        RootGrid.RowDefinitions[2].Height = new GridLength(76);
#else
        RootGrid.RowDefinitions[2].Height = new GridLength(100);
#endif

        // 恢复系统状态栏显示
        ShowSystemStatusBar();

#if ANDROID || WINDOWS
        // 恢复 RootGrid 顶部/底部系统栏安全区
        ApplyRootGridSafeArea();
#endif

        SwitchToNamedTab(returnTab, animate: false);

        if (outgoing != null)
        {
            // 旧沉浸页内容垫到新 tab 内容之上，滑出 + 淡出（幕布揭开效果）
            ContentArea.Children.Add(outgoing);
            DesktopTransitions.PushExit(ContentArea, outgoing, exitLeft);
        }
    }

    private async Task PlayPlaylistAsync(Playlist pl)
    {
        try
        {
            await _playlistDetailVm.LoadPlaylistAsync(pl.Id, pl.Name);
            await _playlistDetailVm.PlayAllAsync();
        }
        catch (Exception ex)
        {
            Log.Debug("DesktopMainPage.xaml", $"[Desktop] PlayPlaylist failed: {ex}");
        }
    }

    private async Task RenamePlaylistAsync(Playlist pl)
    {
        var name = await DisplayPromptAsync("重命名歌单", "请输入新的歌单名称：",
            initialValue: pl.Name, accept: "确定", cancel: "取消");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            await _playlistVm.RenamePlaylistAsync(pl.Id, name.Trim());
            _playlistVm.MarkDirty();
            await _playlistVm.LoadPlaylistsAsync();
            BuildPlaylistList();
        }
        catch (Exception ex)
        {
            await DisplayAlert("重命名失败", ex.Message, "确定");
        }
    }

    private async Task DeletePlaylistAsync(Playlist pl)
    {
        bool ok = await DisplayAlert("删除歌单",
            $"确定删除「{pl.Name}」吗？此操作不可撤销。", "删除", "取消");
        if (!ok) return;
        try
        {
            await _playlistVm.DeletePlaylistAsync(pl.Id);
            BuildPlaylistList();
        }
        catch (Exception ex)
        {
            await DisplayAlert("删除失败", ex.Message, "确定");
        }
    }

    private async void OnAddPlaylistTapped(object? sender, TappedEventArgs e)
    {
        var name = await DisplayPromptAsync("新建歌单", "请输入歌单名称：",
            initialValue: "我的歌单", accept: "创建", cancel: "取消");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            await _playlistVm.CreatePlaylistAsync(name.Trim());
            await _playlistVm.LoadPlaylistsAsync();
            BuildPlaylistList();
        }
        catch (Exception ex)
        {
            await DisplayAlert("创建失败", ex.Message, "确定");
        }
    }

    // ─── Top Command Bar Search ───

    private void OnSearchSubmitted(object? sender, EventArgs e)
    {
        var q = SearchEntry.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(q)) return;

        SwitchTab(DesktopTab.Discover);

        if (_pageCache.TryGetValue(DesktopTab.Discover, out var view)
            && view.BindingContext is SearchViewModel svm)
        {
            svm.SearchQuery = q;
            svm.ApplyFilters();
        }
    }

    // ─── Player Controls ───

    private void OnDesktopSliderDragStarted(object? sender, EventArgs e)
    {
        _npVm.OnSeekStarted();
    }

    private async void OnDesktopSliderDragCompleted(object? sender, EventArgs e)
    {
        await _npVm.OnSeekCompleted(DesktopProgressSlider.Value);
        DesktopProgressSlider.SetBinding(Slider.ValueProperty,
            new Binding("Progress", BindingMode.TwoWay));
    }

    private void OnLyricsButtonClicked(object? sender, EventArgs e)
    {
        var page = _services.GetRequiredService<FullLyricsPage>();
        DesktopNavigation.PushEmbed(page);
    }

    /// <summary>点击底部播放栏的歌曲信息/封面时，跳转到正在播放页。
    /// DesktopMainPage 是 Shell 根页面，播放页用 PushAsync 覆盖全屏。</summary>
    private void OnPlayerSongInfoTapped(object? sender, EventArgs e)
    {
#if ANDROID
        // 横屏桌面舞台：播放页内嵌到内容区（全屏沉浸），不 push Shell 导航栈
        //（push 会绕过 MainPage 舞台机制并残留导航栈，旋转回竖屏时覆盖 ViewPager）
        OpenSubPageEmbedded(_services.GetRequiredService<NowPlayingPage>());
#else
        var page = _services.GetRequiredService<NowPlayingPage>();
        DesktopNavigation.PushEmbed(page);
#endif
    }

    private void InitVolumeSlider()
    {
        try
        {
            VolumeSlider.Value = _audioPlayer.Volume;
            VolumeSlider.ValueChanged += (_, e) => _audioPlayer.Volume = e.NewValue;
        }
        catch (Exception ex)
        {
            Log.Debug("DesktopMainPage.xaml", $"[Desktop] InitVolumeSlider failed: {ex}");
        }
    }

    // ─── Keyboard Shortcuts ───

    // 本版本 MAUI 的 ContentPage 没有 KeyDown 事件，因此 Windows 端改为直接订阅
    // 页面底层 WinUI 可视元素（UIElement）的 KeyDown 路由事件，按键类型为 Windows.System.VirtualKey。
    // 非 Windows 平台无需键盘快捷键，留空。
    // 注意：必须用完全限定名（Microsoft.UI.Xaml.UIElement / Microsoft.UI.Xaml.Input.KeyRoutedEventArgs /
    // Windows.System.VirtualKey），否则引入 using Microsoft.UI.Xaml 会与 MAUI 的 Window/GridLength/
    // Thickness 等冲突（CS0104），且 WinUI 的 VirtualKey 位于 Windows.System 而非 Microsoft.UI.Xaml.Input。

    private bool _keyboardAttached;
    private int _keyboardAttachAttempts;

#if WINDOWS
    private void AttachKeyboard()
    {
        if (_keyboardAttached) return;
        if (_keyboardAttachAttempts++ > 20) return; // 防止视图未就绪时无限重试

        // 页面自身的 WinUI 可视元素（UIElement）一定包含 KeyDown 路由事件；
        // KeyDown 会从聚焦控件向上冒泡，因此背景或滑块聚焦时均能被捕获。
        if (this.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement rootUi)
        {
            rootUi.KeyDown += OnWinUiKeyDown;
            _keyboardAttached = true;
        }
        else
        {
            // 视图/PlatformView 尚未就绪，下一帧再试
            Dispatcher.Dispatch(AttachKeyboard);
        }
    }

    private void OnWinUiKeyDown(object? sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        // 在搜索框中输入时不拦截普通按键（保证正常打字，空格应插入文本）
        // 但媒体键即使搜索框聚焦也应被拦截，因为它们不会输入文本
        // Windows.System.VirtualKey 枚举在 WinUI 3 投影中缺少媒体键命名成员，这里用 Win32 VK 码整数值
        const int VK_MEDIA_PLAY_PAUSE = 179;
        const int VK_MEDIA_NEXT_TRACK = 176;
        const int VK_MEDIA_PREV_TRACK = 177;
        const int VK_MEDIA_STOP = 178;
        const int VK_VOLUME_MUTE = 173;
        const int VK_VOLUME_UP = 175;
        const int VK_VOLUME_DOWN = 174;

        var key = e.Key;
        int keyVal = (int)key;
        bool isMediaKey = keyVal == VK_MEDIA_PLAY_PAUSE
            || keyVal == VK_MEDIA_NEXT_TRACK
            || keyVal == VK_MEDIA_PREV_TRACK
            || keyVal == VK_MEDIA_STOP
            || keyVal == VK_VOLUME_MUTE
            || keyVal == VK_VOLUME_UP
            || keyVal == VK_VOLUME_DOWN;

        if (SearchEntry.IsFocused && !isMediaKey) return;

        if (key == Windows.System.VirtualKey.Space || keyVal == VK_MEDIA_PLAY_PAUSE)
        {
            _npVm.TogglePlayPauseCommand.Execute(null);
            e.Handled = true;
        }
        else if (key == Windows.System.VirtualKey.Left || keyVal == VK_MEDIA_PREV_TRACK)
        {
            _npVm.PlayPreviousCommand.Execute(null);
            e.Handled = true;
        }
        else if (key == Windows.System.VirtualKey.Right || keyVal == VK_MEDIA_NEXT_TRACK)
        {
            _npVm.PlayNextCommand.Execute(null);
            e.Handled = true;
        }
        else if (keyVal == VK_MEDIA_STOP)
        {
            _ = _audioPlayer.StopAsync();
            e.Handled = true;
        }
        else if (key == Windows.System.VirtualKey.Up || keyVal == VK_VOLUME_UP)
        {
            ChangeVolume(+0.05);
            e.Handled = true;
        }
        else if (key == Windows.System.VirtualKey.Down || keyVal == VK_VOLUME_DOWN)
        {
            ChangeVolume(-0.05);
            e.Handled = true;
        }
        else if (keyVal == VK_VOLUME_MUTE)
        {
            ToggleMute();
            e.Handled = true;
        }
    }

    private bool _muted;
    private double _preMuteVolume = 1.0;

    private void ToggleMute()
    {
        if (_muted)
        {
            _audioPlayer.Volume = _preMuteVolume;
            VolumeSlider.Value = _preMuteVolume;
            _muted = false;
        }
        else
        {
            _preMuteVolume = _audioPlayer.Volume;
            _audioPlayer.Volume = 0;
            VolumeSlider.Value = 0;
            _muted = true;
        }
    }
#else
    private void AttachKeyboard()
    {
        // 非 Windows 平台暂不需要键盘快捷键
    }
#endif

    private void ChangeVolume(double delta)
    {
        var v = Math.Clamp(_audioPlayer.Volume + delta, 0, 1);
        _audioPlayer.Volume = v;
        VolumeSlider.Value = v;
    }

    // ─── Responsive Sidebar ───

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        ApplyResponsiveLayout();
    }

    private void ApplyResponsiveLayout()
    {
        if (Width <= 0) return;
#if ANDROID
        // Android 横屏复用桌面布局：侧栏保持完整宽度，不响应窗口宽度折叠
        const bool compact = false;
#else
        bool compact = Width < CompactThreshold;
#endif
        if (compact == _compact) return;
        _compact = compact;

        if (_compact)
        {
            RootGrid.ColumnDefinitions[0].Width = new GridLength(64);
            LogoText.IsVisible = false;
            NavHeader.IsVisible = false;
            NavDiscoverLabel.IsVisible = false;
            NavLibraryLabel.IsVisible = false;
            NavPlaylistsLabel.IsVisible = false;
            PlaylistHeader.IsVisible = false;
            AddPlaylistButton.IsVisible = false;
            foreach (var l in _playlistNameLabels) l.IsVisible = false;
        }
        else
        {
#if ANDROID
            RootGrid.ColumnDefinitions[0].Width = new GridLength(AndroidSidebarWidth);
#else
            RootGrid.ColumnDefinitions[0].Width = new GridLength(SidebarWidth);
#endif
            LogoText.IsVisible = true;
            NavHeader.IsVisible = true;
            NavDiscoverLabel.IsVisible = true;
            NavLibraryLabel.IsVisible = true;
            NavPlaylistsLabel.IsVisible = true;
            PlaylistHeader.IsVisible = true;
            AddPlaylistButton.IsVisible = true;
            foreach (var l in _playlistNameLabels) l.IsVisible = true;
        }
    }

#if ANDROID
    /// <summary>手机横屏复用桌面布局时，压缩底部播放栏的控件尺寸，降低行高。</summary>
    private void ApplyCompactPlayerBarMetrics()
    {
        // 歌曲信息区
        PlayerInfoGrid.Padding = new Thickness(10, 0);
        PlayerInfoGrid.WidthRequest = 140;
        PlayerCoverBorder.WidthRequest = 32;
        PlayerCoverBorder.HeightRequest = 32;
        PlayerCover.WidthRequest = 32;
        PlayerCover.HeightRequest = 32;

        // 播放控制区：上下留边交给外层内容 Grid(Padding 24,8,24,8) 统一控制，避免两层叠加溢出被 Center 裁剪；
        // 此处仅保留左右内边距与行距。
        PlayerControlsGrid.Padding = new Thickness(6, 0, 6, 0);
        // 按钮行与进度条贴近：行距 4→0（紧凑卡片内控件与进度更紧凑）
        PlayerControlsGrid.RowSpacing = 0;
        DesktopProgressSlider.MinimumWidthRequest = 140;
        DesktopProgressSlider.HeightRequest = 20;
        // 进度区时间标签微调，让进度条居中时上下留白更均匀
        //（高度保持自动按行内容）

        // 控制按钮行：主播放键 40→34、其余 32→28→24，进度条压到 20 高、
        // 行距 6→4，整体在 76 行高内为上下留边腾出真实空间（避免 Center 溢出裁剪掉边距）
        if (PlayerControlsGrid.Children.Count > 0 && PlayerControlsGrid.Children[0] is Grid btnRow)
        {
            foreach (var child in btnRow.Children)
            {
                if (child is Grid playGrid)
                {
                    playGrid.WidthRequest = 34;
                    playGrid.HeightRequest = 34;
                    if (playGrid.Children.Count > 0 && playGrid.Children[0] is Image playIcon)
                    {
                        playIcon.WidthRequest = 20;
                        playIcon.HeightRequest = 20;
                    }
                }
                else if (child is Image img)
                {
                    img.WidthRequest = 24;
                    img.HeightRequest = 24;
                }
            }
            btnRow.ColumnSpacing = 10;
        }

        // 音量区：compact 模式下保持 140 宽，Slider 至少 100
        VolumeGrid.WidthRequest = 140;
        VolumeGrid.MinimumWidthRequest = 100;
        VolumeSlider.MinimumWidthRequest = 100;
    }

    /// <summary>隐藏系统状态栏，让子页面内容延伸到屏幕顶部（沉浸式）。</summary>
    private void HideSystemStatusBar()
    {
        try
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            var window = activity?.Window;
            if (window == null) return;

            var decorView = window.DecorView;
#pragma warning disable CS0618
            decorView.SystemUiVisibility = (Android.Views.StatusBarVisibility)(
                (int)decorView.SystemUiVisibility
                | (int)Android.Views.SystemUiFlags.Fullscreen
                | (int)Android.Views.SystemUiFlags.ImmersiveSticky
                | (int)Android.Views.SystemUiFlags.LayoutStable
                | (int)Android.Views.SystemUiFlags.LayoutFullscreen);
#pragma warning restore CS0618
        }
        catch (Exception ex)
        {
            Log.Debug("DesktopMainPage.xaml", $"[Desktop] HideSystemStatusBar failed: {ex.Message}");
        }
    }

    /// <summary>恢复系统状态栏显示。</summary>
    private void ShowSystemStatusBar()
    {
        try
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            var window = activity?.Window;
            if (window == null) return;

            var decorView = window.DecorView;
#pragma warning disable CS0618
            decorView.SystemUiVisibility = (Android.Views.StatusBarVisibility)(
                (int)decorView.SystemUiVisibility
                & ~(int)Android.Views.SystemUiFlags.Fullscreen
                & ~(int)Android.Views.SystemUiFlags.ImmersiveSticky
                & ~(int)Android.Views.SystemUiFlags.LayoutFullscreen);
#pragma warning restore CS0618
        }
        catch (Exception ex)
        {
            Log.Debug("DesktopMainPage.xaml", $"[Desktop] ShowSystemStatusBar failed: {ex.Message}");
        }
    }
#else
    /// <summary>非 Android 平台空实现（Windows 桌面端无系统状态栏概念）。</summary>
    private void HideSystemStatusBar() { }
    /// <summary>非 Android 平台空实现。</summary>
    private void ShowSystemStatusBar() { }
#endif

}
