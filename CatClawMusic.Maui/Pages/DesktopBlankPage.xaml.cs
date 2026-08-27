using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Pages;

/// <summary>
/// 桌面端重建主页面（布局原型参考旧 DesktopMainPage）：
/// 左侧栏（Logo + 导航 + 我的歌单 + 设置）| 右上自定义标题栏（拖动区）| 右侧主页内容区 | 底部播放器。
/// 窗口无边框由 App.xaml.cs 处理（ExtendsContentIntoTitleBar=true，保留标题栏实体）。
/// 拖拽：原生方案 AppWindow.TitleBar.SetDragRectangles（TitlbarWinUI3 同款），
/// 拖动区 = 左侧导航区整条 + 顶部标题栏。
/// 首页：默认 tab = 发现页（DesktopDiscoverPage，嵌入 MainArea）。
/// </summary>
public partial class DesktopBlankPage : ContentPage, ISongContextMenuHost
{
    private readonly NowPlayingViewModel _npVm;
    private readonly IServiceProvider _services;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly PlaylistViewModel _playlistVm;
    private readonly PlaylistDetailViewModel _playlistDetailVm;

    private enum DesktopTab { Discover, Library, Playlists, Settings }
    private DesktopTab _currentTab = DesktopTab.Discover;

    // Cached page contents（与 DesktopMainPage 同款：提取 Content + 重绑 VM，避免页面嵌套）
    private readonly Dictionary<DesktopTab, View> _pageCache = new();
    private readonly Dictionary<DesktopTab, ContentPage> _pageHostCache = new();

    // 侧边栏歌单名称标签（响应式折叠预留）
    private readonly List<Label> _playlistNameLabels = new();
    private readonly object _playlistListLock = new();

    /// <summary>全局实例，供嵌入的子页面（如 SearchPage）请求切换 tab</summary>
    public static DesktopBlankPage? Instance { get; private set; }

    /// <summary>窗口级根网格（全窗覆盖层宿主：弹窗临时挂载于此可覆盖标题栏/播放条，不受 MainArea 裁剪影响）。</summary>
    public Grid WindowRoot => BlankRoot;

    /// <summary>当前嵌入 MainArea 的子页面（OpenEmbeddedPage 打开、SwitchTab/关闭时清空）。</summary>
    private ContentPage? _embeddedPage;

    /// <summary>
    /// 歌曲上下文菜单宿主转发：嵌入子页面的 Content 被摘出后，行父链走不到子页面，
    /// 只能走到本壳页面——这里转发给当前嵌入的子页面。
    /// </summary>
    public void ShowSongMenu(Song song, View row, Point position)
    {
        if (_embeddedPage is ISongContextMenuHost host)
            host.ShowSongMenu(song, row, position);
    }

    public DesktopBlankPage(NowPlayingViewModel npVm, IServiceProvider services)
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        _npVm = npVm;
        _services = services;
        _audioPlayer = services.GetRequiredService<IAudioPlayerService>();
        _playlistVm = services.GetRequiredService<PlaylistViewModel>();
        _playlistDetailVm = services.GetRequiredService<PlaylistDetailViewModel>();
        BindingContext = _npVm;
        Instance = this;

        InitVolumeSlider();

        // 构造时仅创建默认 tab 内容（发现页），不触发生命周期
        _currentTab = DesktopTab.Discover;
        UpdateNavHighlight();
        if (!_pageCache.TryGetValue(_currentTab, out var content))
        {
            content = CreatePageContent(_currentTab);
            if (content != null)
                _pageCache[_currentTab] = content;
        }
        if (content != null)
            MainArea.Children.Add(content);

        // 首次显示也要触发 OnAppearing（加载数据）：SwitchTab 只在点击导航时触发，
        // 启动默认停在发现页时若不调用，页面数据/AI 歌单永远不会加载
        if (_pageHostCache.TryGetValue(_currentTab, out var initialHost))
            InvokeLifecycle(initialHost, "OnAppearing");

        _ = LoadPlaylistsAsync();
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
#if WINDOWS
        // 原生标题栏配置（TitlbarWinUI3 同款）：Tall 高度 + 按钮透明融入 + 拖动区
        try
        {
            var titleBar = GetAppWindowTitleBar();
            if (titleBar == null) return;
            titleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Tall;
            var transparent = Microsoft.UI.Colors.Transparent;
            titleBar.BackgroundColor = transparent;
            titleBar.InactiveBackgroundColor = transparent;
            titleBar.ButtonBackgroundColor = transparent;
            titleBar.ButtonInactiveBackgroundColor = transparent;
        }
        catch { }
#endif
#if WINDOWS
        UpDateTitlebar();
#endif
    }

#if WINDOWS
    private void OnSizeChanged(object? sender, EventArgs e) => UpDateTitlebar();
#else
    private void OnSizeChanged(object? sender, EventArgs e) { }
#endif

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _npVm.LoadCurrentSongAsync();
        // 主题色可能在设置页切换后返回，按当前主题/深浅模式刷新播放控制条图标
        _npVm.RefreshPlayerCtrlIcons();
        // 返回主页时刷新歌单
        _ = _playlistVm.RefreshIfChangedAsync()
            .ContinueWith(_ => MainThread.BeginInvokeOnMainThread(BuildPlaylistList));
    }

    // ─── Navigation ───

    private void OnNavDiscoverTapped(object? sender, TappedEventArgs e) => SwitchTab(DesktopTab.Discover);
    private void OnNavLibraryTapped(object? sender, TappedEventArgs e) => SwitchTab(DesktopTab.Library);
    private void OnNavPlaylistsTapped(object? sender, TappedEventArgs e) => SwitchTab(DesktopTab.Playlists);

    private void SwitchTab(DesktopTab tab)
    {
        // 通知旧 tab 消失（触发数据保存等生命周期）
        if (_pageHostCache.TryGetValue(_currentTab, out var oldHost))
            InvokeLifecycle(oldHost, "OnDisappearing");

        // 嵌入页离开（如 WebView 登录页）：先触发 OnDisappearing 让其停定时器/关闭 WebView2，
        // 再 Children.Clear()。否则 WebView2 异步回调撞上被移除的控件会抛 COMException 闪退。
        // 与 OpenEmbeddedPage 末尾的 InvokeLifecycle(page, "OnAppearing") 对称。
        if (_embeddedPage != null)
            InvokeLifecycle(_embeddedPage, "OnDisappearing");

        _embeddedPage = null;
        _currentTab = tab;
        UpdateNavHighlight();

        if (!_pageCache.TryGetValue(tab, out var content))
        {
            content = CreatePageContent(tab);
            if (content != null)
                _pageCache[tab] = content;
        }

        MainArea.Children.Clear();
        if (content != null)
            MainArea.Children.Add(content);

        // 通知新 tab 显示（页面在此加载数据）
        if (_pageHostCache.TryGetValue(tab, out var newHost))
            InvokeLifecycle(newHost, "OnAppearing");
    }

    /// <summary>由入口按钮切换到设置页（如发现页右上角齿轮）。</summary>
    public void SwitchToSettings() => SwitchTab(DesktopTab.Settings);

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

        // Extract content from the page and rebind（页面不嵌套，只搬 Content）
        var content = page.Content;
        page.Content = null;
        content.BindingContext = page.BindingContext;
        content.VerticalOptions = LayoutOptions.Fill;
        content.HorizontalOptions = LayoutOptions.Fill;

        if (content is ScrollView
            || page is DesktopPlaylistPage or DesktopLibraryPage
            || page is DesktopArtistsPage or DesktopAlbumsPage or DesktopAllSongsPage
            || page is DesktopDiscoverPage)
        {
            return content;
        }
        return new ScrollView { Content = content };
    }

    /// <summary>通过反射调用 ContentPage 的 OnAppearing/OnDisappearing（嵌入 MainArea 的页面不会自动触发）</summary>
    private static void InvokeLifecycle(ContentPage page, string methodName)
    {
        try
        {
            var method = page.GetType().GetMethods(
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly)
                .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == 0);
            if (method == null)
            {
                method = typeof(ContentPage).GetMethods(
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == 0);
            }
            method?.Invoke(page, null);
        }
        catch (Exception ex)
        {
            Log.Debug("DesktopBlankPage.xaml", $"[Desktop] InvokeLifecycle {methodName} on {page.GetType().Name} FAILED: {ex.Message}");
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
        // 设置入口已迁移至顶栏齿轮按钮（方案A晨雾框架侧栏仅保留三大导航+歌单）
    }

    /// <summary>方案A晨雾框架导航选中态（与安卓横屏 DesktopMainPage 同款）。图标跟随文字颜色切换：
    /// <para>浅色模式：未选中项用主题色文字 + 主题色图标，选中项用白色文字 + 白色图标（渐变高亮底上白字清晰）。</para>
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
            TextColor = (Color)(Application.Current?.Resources["TextPrimaryColor"] ?? Colors.Black),
            LineBreakMode = LineBreakMode.TailTruncation,
        };
        _playlistNameLabels.Add(nameLabel);

        var moreButton = new Label
        {
            Text = "⋮",
            FontSize = 18,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            TextColor = (Color)(Application.Current?.Resources["TextHintColor"] ?? Colors.Gray),
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

        // ⋮ 按钮弹出操作菜单
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

    /// <summary>
    /// 桌面无 Shell 模式：把子页面 Content 摘出嵌入 MainArea（保留左侧导航栏）。
    /// 供发现页等无 Shell 页面打开专辑/艺术家等详情页；返回按钮的隐藏由调用方按页面结构处理。
    /// </summary>
    public void OpenEmbeddedPage(ContentPage page)
    {
        try
        {
            if (page == null) return;
            var content = page.Content;
            if (content == null) return;
            page.Content = null;
            content.BindingContext = page.BindingContext;
            content.VerticalOptions = LayoutOptions.Fill;
            content.HorizontalOptions = LayoutOptions.Fill;

            MainArea.Children.Clear();
            MainArea.Children.Add(content);
            _embeddedPage = page;

            InvokeLifecycle(page, "OnAppearing");
        }
        catch (Exception ex)
        {
            Log.Debug("DesktopBlankPage.xaml", $"[Desktop] OpenEmbeddedPage failed: {ex}");
        }
    }

    /// <summary>桌面无 Shell 模式：关闭嵌入的子页面，恢复当前 tab 的默认内容（子页面返回按钮调用）。</summary>
    public void CloseEmbeddedPage()
    {
        try
        {
            SwitchTab(_currentTab);
        }
        catch (Exception ex)
        {
            Log.Debug("DesktopBlankPage.xaml", $"[Desktop] CloseEmbeddedPage failed: {ex}");
        }
    }

    private void OpenPlaylist(Playlist pl)
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
            content.VerticalOptions = LayoutOptions.Fill;
            content.HorizontalOptions = LayoutOptions.Fill;

            MainArea.Children.Clear();
            MainArea.Children.Add(content);

            InvokeLifecycle(page, "OnAppearing");
        }
        catch (Exception ex)
        {
            Log.Debug("DesktopBlankPage.xaml", $"[Desktop] OpenPlaylistEmbedded failed: {ex}");
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
            Log.Debug("DesktopBlankPage.xaml", $"[Desktop] PlayPlaylist failed: {ex}");
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

    // ─── 音量：内联横向滑条（方案A晨雾框架播放卡右侧）───

    private void InitVolumeSlider()
    {
        try
        {
            VolumeSlider.Value = _audioPlayer.Volume;
            VolumeSlider.ValueChanged += (_, e) => _audioPlayer.Volume = e.NewValue;
        }
        catch (Exception ex)
        {
            Log.Debug("DesktopBlankPage.xaml", $"[Desktop] InitVolumeSlider failed: {ex}");
        }
    }

    // ─── 点击播放器条空白处 → 播放页嵌入主页（覆盖层，窗口内部不可拖走）───

    // 当前覆盖层里的播放页实例（关闭时通知 OnDisappearing）
    private ContentPage? _overlayPlayerPage;

    private void OnPlayerSongInfoTapped(object? sender, EventArgs e)
    {
        try
        {
            if (PlayerOverlay.IsVisible) return; // 已打开

            var page = _services.GetRequiredService<NowPlayingPage>();
            var content = page.Content;
            if (content == null) return;
            page.Content = null;
            content.BindingContext = page.BindingContext;
            content.VerticalOptions = LayoutOptions.Fill;
            content.HorizontalOptions = LayoutOptions.Fill;

            // 播放页自带收起按钮 → hook 到关闭覆盖层
            // （原 handler 走 Shell 导航栈，覆盖层模式下无效）
            // ① WindowsStage 顶栏「收起」胶囊（Windows 桌面布局实际显示的）
            if (page.FindByName<Border>("WinCollapseBtn") is Border winCollapse)
            {
                winCollapse.GestureRecognizers.Clear();
                var winTap = new TapGestureRecognizer();
                winTap.Tapped += (_, _) => ClosePlayerOverlay();
                winCollapse.GestureRecognizers.Add(winTap);
            }
            // ② 手机版布局顶部收起按钮（保险）
            if (page.FindByName<Border>("CollapseButton") is Border collapseBtn)
            {
                collapseBtn.GestureRecognizers.Clear();
                var tap = new TapGestureRecognizer();
                tap.Tapped += (_, _) => ClosePlayerOverlay();
                collapseBtn.GestureRecognizers.Add(tap);
            }

            PlayerOverlay.Children.Clear();
            PlayerOverlay.Children.Add(content);
            PlayerOverlay.IsVisible = true;
            _overlayPlayerPage = page;

            // WindowsStage 的可见性由 ApplyWindowsLayout 控制（OnSizeAllocated / RootGrid.SizeChanged
            // 触发）。Content 被提取后这些事件可能不触发 → 布局完成后手动补触发一次初始化。
            Dispatcher.Dispatch(() =>
            {
                try
                {
                    var w = PlayerOverlay.Width;
                    var h = PlayerOverlay.Height;
                    if (w <= 0 || h <= 0) return;
                    var m = typeof(NowPlayingPage).GetMethod("OnSizeAllocated",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    m?.Invoke(page, new object[] { w, h });
                }
                catch { /* 布局初始化失败不影响显示 */ }
            });

            InvokeLifecycle(page, "OnAppearing");
        }
        catch (Exception ex)
        {
            Log.Debug("DesktopBlankPage.xaml", $"[Desktop] OpenNowPlaying failed: {ex}");
        }
    }

    /// <summary>关闭播放页覆盖层（由播放页收起按钮触发）。</summary>
    public void ClosePlayerOverlay()
    {
        if (_overlayPlayerPage != null)
        {
            InvokeLifecycle(_overlayPlayerPage, "OnDisappearing");
            _overlayPlayerPage = null;
        }
        PlayerOverlay.Children.Clear();
        PlayerOverlay.IsVisible = false;
    }

    // ─── 原生标题栏（TitlbarWinUI3 同款）───

#if WINDOWS
    private Microsoft.UI.Windowing.AppWindowTitleBar? GetAppWindowTitleBar()
    {
        var nativeWindow = App.CurrentNativeWindow;
        if (nativeWindow == null) return null;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        return Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId)?.TitleBar;
    }

    /// <summary>
    /// 原生拖动区（物理像素矩形，DPI 换算）：
    /// 顶栏整行可拖（TitleBarHost 铺满顶栏，顶栏上无任何按钮）。
    /// X = 侧栏列宽（244：浮层卡 232 + 左留边 12）。
    /// </summary>
    private void UpDateTitlebar()
    {
        try
        {
            var nativeWindow = App.CurrentNativeWindow;
            if (nativeWindow == null) return;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            if (appWindow?.TitleBar == null) return;

            var scale = Win32.GetScaleAdjustment(nativeWindow);
            var colDef = BlankRoot.ColumnDefinitions[0];
            var sidebarWidth = colDef.Width.IsAbsolute ? colDef.Width.Value : NavArea.Width;
            var rect = new global::Windows.Graphics.RectInt32
            {
                X = (int)(sidebarWidth * scale),
                Y = 0,
                Width = (int)(TitleBarHost.Width * scale),
                Height = (int)(TitleBarHost.Height * scale),
            };
            appWindow.TitleBar.SetDragRectangles(new[] { rect });
        }
        catch { /* 拖动区设置失败不影响显示 */ }
    }
#endif
}
