using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
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
public partial class DesktopBlankPage : ContentPage
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
        UpdateDesktopLyricIcon();

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
        UpDateTitlebar();
    }

    private void OnSizeChanged(object? sender, EventArgs e) => UpDateTitlebar();

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
    private void OnNavSettingsTapped(object? sender, TappedEventArgs e) => SwitchTab(DesktopTab.Settings);

    private void SwitchTab(DesktopTab tab)
    {
        // 通知旧 tab 消失（触发数据保存等生命周期）
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

        MainArea.Children.Clear();
        if (content != null)
            MainArea.Children.Add(content);

        // 通知新 tab 显示（页面在此加载数据）
        if (_pageHostCache.TryGetValue(tab, out var newHost))
            InvokeLifecycle(newHost, "OnAppearing");
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
        var activeColor = (Color)(Application.Current?.Resources["ChipActiveColor"] ?? Colors.Purple);

        NavDiscover.BackgroundColor = _currentTab == DesktopTab.Discover ? activeColor.WithAlpha(0.15f) : Colors.Transparent;
        NavLibrary.BackgroundColor = _currentTab == DesktopTab.Library ? activeColor.WithAlpha(0.15f) : Colors.Transparent;
        NavPlaylists.BackgroundColor = _currentTab == DesktopTab.Playlists ? activeColor.WithAlpha(0.15f) : Colors.Transparent;
        NavSettings.BackgroundColor = _currentTab == DesktopTab.Settings ? activeColor.WithAlpha(0.15f) : Colors.Transparent;
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

    // ─── 音量：hover 弹出垂直音量条 ───

    private void OnVolumePointerEntered(object? sender, PointerEventArgs e) => VolumePop.IsVisible = true;

    private void OnVolumePointerExited(object? sender, PointerEventArgs e) => VolumePop.IsVisible = false;

    private void InitVolumeSlider()
    {
        try
        {
            VolumePopSlider.Value = _audioPlayer.Volume;
            VolumePctLabel.Text = $"{_audioPlayer.Volume * 100:0}%";
            VolumePopSlider.ValueChanged += (_, e) =>
            {
                _audioPlayer.Volume = e.NewValue;
                VolumePctLabel.Text = $"{e.NewValue * 100:0}%";
            };
        }
        catch (Exception ex)
        {
            Log.Debug("DesktopBlankPage.xaml", $"[Desktop] InitVolumeSlider failed: {ex}");
        }
    }

    /// <summary>主页底部播放器歌词按钮图标：安卓通知栏媒体控件同款 ic_notif_lyric_on
    /// （主题感知：深色=白色原版，浅色=主题色变体，与 VM 播放控件图标同一机制）。</summary>
    private void UpdateDesktopLyricIcon()
    {
        try
        {
            if (DesktopLyricIcon == null) return;
            DesktopLyricIcon.Source =
                ImageSourceHelper.FromNamePlayerCtrl("ic_notif_lyric_on", "ic_notif_lyric_on");
        }
        catch (Exception ex)
        {
            Log.Debug("DesktopBlankPage.xaml", $"[Desktop] UpdateDesktopLyricIcon failed: {ex}");
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
    private void ClosePlayerOverlay()
    {
        if (_overlayPlayerPage != null)
        {
            InvokeLifecycle(_overlayPlayerPage, "OnDisappearing");
            _overlayPlayerPage = null;
        }
        PlayerOverlay.Children.Clear();
        PlayerOverlay.IsVisible = false;
    }

    // ─── 预留按钮：歌词 / 更多（后续接入功能）───

    private void OnLyricsTapped(object? sender, TappedEventArgs e)
    {
        // 预留：后续接歌词面板/歌词页（BlankPage 不在 Shell 导航栈，不能直接 PushAsync）
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
    /// 仅顶部标题栏可拖（X 从导航区 + 分割线右边缘开始）。
    /// 左侧导航区已取消拖拽绑定（2026-08-09，用户要求——导航区后续放可交互的导航项）。
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
            var rect = new global::Windows.Graphics.RectInt32
            {
                X = (int)((NavArea.Width + NavDivider.Width) * scale),
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
