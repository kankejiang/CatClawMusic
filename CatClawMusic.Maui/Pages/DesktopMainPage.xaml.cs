using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Pages;

/// <summary>
/// Desktop (Windows) main page: left sidebar + content area + bottom player bar.
/// Modeled after NetEase Cloud Music PC client layout, with PC-only enhancements:
/// sidebar playlists, top command-bar search, keyboard shortcuts, responsive sidebar,
/// and right-click context menus (replacing mobile swipe gestures).
/// </summary>
public partial class DesktopMainPage : ContentPage
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

    // 侧边栏歌单名称标签（用于响应式折叠时隐藏）
    private readonly List<Label> _playlistNameLabels = new();

    // 响应式：窄窗折叠为图标栏
    private bool _compact;
    private const double SidebarWidth = 220;
    private const double CompactThreshold = 1000;

    public DesktopMainPage(NowPlayingViewModel npVm, IServiceProvider services)
    {
        InitializeComponent();
        _npVm = npVm;
        _services = services;
        _audioPlayer = services.GetRequiredService<IAudioPlayerService>();
        _playlistVm = services.GetRequiredService<PlaylistViewModel>();
        _playlistDetailVm = services.GetRequiredService<PlaylistDetailViewModel>();
        BindingContext = _npVm;

        SizeChanged += OnPageSizeChanged;
        InitVolumeSlider();

        // Load default tab
        SwitchTab(DesktopTab.Discover);
        _ = LoadPlaylistsAsync();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _npVm.LoadCurrentSongAsync();
        // 回到桌面主页时刷新歌单（例如从歌单详情页返回）
        _ = _playlistVm.RefreshIfChangedAsync()
            .ContinueWith(_ => MainThread.BeginInvokeOnMainThread(BuildPlaylistList));
        AttachKeyboard();
    }

    // ─── Navigation ───

    private void OnNavDiscoverTapped(object? sender, TappedEventArgs e) => SwitchTab(DesktopTab.Discover);
    private void OnNavLibraryTapped(object? sender, TappedEventArgs e) => SwitchTab(DesktopTab.Library);
    private void OnNavPlaylistsTapped(object? sender, TappedEventArgs e) => SwitchTab(DesktopTab.Playlists);
    private void OnNavSettingsTapped(object? sender, TappedEventArgs e) => SwitchTab(DesktopTab.Settings);

    private void SwitchTab(DesktopTab tab)
    {
        _currentTab = tab;
        UpdateNavHighlight();

        if (!_pageCache.TryGetValue(tab, out var content))
        {
            content = CreatePageContent(tab);
            if (content != null)
                _pageCache[tab] = content;
        }

        ContentArea.Children.Clear();
        if (content != null)
            ContentArea.Children.Add(content);
    }

    private View? CreatePageContent(DesktopTab tab)
    {
        ContentPage? page = tab switch
        {
            DesktopTab.Discover => _services.GetRequiredService<SearchPage>(),
            DesktopTab.Library => _services.GetRequiredService<LibraryPage>(),
            DesktopTab.Playlists => _services.GetRequiredService<PlaylistPage>(),
            DesktopTab.Settings => _services.GetRequiredService<SettingsPage>(),
            _ => null
        };

        if (page == null) return null;

        // Extract content from the page and rebind
        var content = page.Content;
        page.Content = null;
        content.BindingContext = page.BindingContext;

        // Wrap in ScrollView if not already scrollable
        if (content is not ScrollView)
        {
            return new ScrollView { Content = content };
        }
        return content;
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
        PlaylistHost.Children.Clear();
        _playlistNameLabels.Clear();

        foreach (var pl in _playlistVm.Playlists)
        {
            PlaylistHost.Children.Add(CreatePlaylistRow(pl));
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
        _ = Shell.Current.GoToAsync(
            $"playlistdetail?playlistId={pl.Id}&name={Uri.EscapeDataString(pl.Name)}");
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
            System.Diagnostics.Debug.WriteLine($"[Desktop] PlayPlaylist failed: {ex}");
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
        // 跳转到全屏歌词页（已注册全局路由 fullyrics）
        _ = Shell.Current.GoToAsync("//fullyrics");
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
            System.Diagnostics.Debug.WriteLine($"[Desktop] InitVolumeSlider failed: {ex}");
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
        // 在搜索框中输入时不拦截按键（保证正常打字，空格应插入文本）
        if (SearchEntry.IsFocused) return;

        switch (e.Key)
        {
            case Windows.System.VirtualKey.Space:
                _npVm.TogglePlayPauseCommand.Execute(null);
                break;
            case Windows.System.VirtualKey.Left:
                _npVm.PlayPreviousCommand.Execute(null);
                break;
            case Windows.System.VirtualKey.Right:
                _npVm.PlayNextCommand.Execute(null);
                break;
            case Windows.System.VirtualKey.Up:
                ChangeVolume(+0.05);
                break;
            case Windows.System.VirtualKey.Down:
                ChangeVolume(-0.05);
                break;
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
        bool compact = Width < CompactThreshold;
        if (compact == _compact) return;
        _compact = compact;

        if (_compact)
        {
            RootGrid.ColumnDefinitions[0].Width = new GridLength(64);
            LogoText.IsVisible = false;
            NavDiscoverLabel.IsVisible = false;
            NavLibraryLabel.IsVisible = false;
            NavPlaylistsLabel.IsVisible = false;
            SettingsLabel.IsVisible = false;
            PlaylistHeader.IsVisible = false;
            AddPlaylistButton.IsVisible = false;
            foreach (var l in _playlistNameLabels) l.IsVisible = false;
        }
        else
        {
            RootGrid.ColumnDefinitions[0].Width = new GridLength(SidebarWidth);
            LogoText.IsVisible = true;
            NavDiscoverLabel.IsVisible = true;
            NavLibraryLabel.IsVisible = true;
            NavPlaylistsLabel.IsVisible = true;
            SettingsLabel.IsVisible = true;
            PlaylistHeader.IsVisible = true;
            AddPlaylistButton.IsVisible = true;
            foreach (var l in _playlistNameLabels) l.IsVisible = true;
        }
    }
}
