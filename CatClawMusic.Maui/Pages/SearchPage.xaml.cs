using System.ComponentModel;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.Pages.Base;
using CatClawMusic.Maui.Services;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.Pages;

/// <summary>竖屏发现页（搜索 + 推荐 + AI 聊天）。
/// 共享事件处理逻辑见 <see cref="DiscoverPageBase"/>；本类仅保留竖屏专属的
/// Hero 轮播、设置抽屉、聊天迷你播放器、查看全部导航等差异部分。</summary>
public partial class SearchPage : DiscoverPageBase
{
    private readonly SearchViewModel _vm;
    private readonly PlayQueue _queue;
    private readonly MusicDatabase _db;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly IServiceProvider _services;
    private readonly Services.IInteractionStateService? _interactionState;
    private readonly NowPlayingViewModel _nowPlayingVm;
    private readonly ListeningStatsView _statsView;
    private SettingsPage? _settingsPage;
    private bool _isSettingsPanelOpen;

#if ANDROID
    private readonly List<global::Android.Views.View> _settingsBlurredViews = new();
#endif
    private IDispatcherTimer? _heroAutoScrollTimer;
    private int _heroCurrentPosition;

    /// <summary>初始化 <see cref="SearchPage"/> 类的新实例，并注入所需的服务与视图模型。</summary>
    public SearchPage(MusicDatabase db, PlayQueue queue, SearchViewModel vm, IAudioPlayerService audioPlayer, IServiceProvider services, NowPlayingViewModel nowPlayingVm, ListeningStatsView statsView)
    {
        InitializeComponent();
        _db = db;
        _queue = queue;
        _vm = vm;
        _audioPlayer = audioPlayer;
        _services = services;
        _interactionState = services.GetService<Services.IInteractionStateService>();
        _nowPlayingVm = nowPlayingVm;
        _statsView = statsView;
        BindingContext = _vm;
        UpdateTabVisualState(0);
        SetupHeroAutoScroll();

        // 将听歌统计视图添加到"报告"面板
        PanelStats.Children.Add(_statsView);

        ChatBackButton.Clicked += OnChatBackClicked;

        ChatMiniPlayer.BindingContext = _nowPlayingVm;

        // 静态/单例事件：通过 HandlerChanged 管理订阅生命周期，支持页面实例复用（Singleton MainPage）。
        // 页面挂载时订阅、分离时取消，避免横竖屏切换后旧订阅残留或新挂载时漏订阅。
        HandlerChanged += (_, _) =>
        {
            if (Handler == null)
            {
                // 页面分离：取消订阅
                if (_interactionState != null)
                    _interactionState.InteractionStateChanged -= OnInteractionStateChangedForHero;
                _nowPlayingVm.PropertyChanged -= OnNowPlayingPropertyChanged;
                _vm.ChatHistoryLoaded -= OnChatHistoryLoaded;
                _vm.ScrollToLatestMessageRequested -= OnScrollToLatestMessageRequested;
                _vm.PropertyChanged -= OnSearchVmPropertyChanged;
                _heroAutoScrollTimer?.Stop();
            }
            else
            {
                // 页面挂载（或重新挂载）：订阅事件（先 -= 再 += 避免重复）
                if (_interactionState != null)
                {
                    _interactionState.InteractionStateChanged -= OnInteractionStateChangedForHero;
                    _interactionState.InteractionStateChanged += OnInteractionStateChangedForHero;
                }
                _nowPlayingVm.PropertyChanged -= OnNowPlayingPropertyChanged;
                _nowPlayingVm.PropertyChanged += OnNowPlayingPropertyChanged;
                _vm.ChatHistoryLoaded -= OnChatHistoryLoaded;
                _vm.ChatHistoryLoaded += OnChatHistoryLoaded;
                _vm.ScrollToLatestMessageRequested -= OnScrollToLatestMessageRequested;
                _vm.ScrollToLatestMessageRequested += OnScrollToLatestMessageRequested;
                _vm.PropertyChanged -= OnSearchVmPropertyChanged;
                _vm.PropertyChanged += OnSearchVmPropertyChanged;
            }
        };

        // 空闲时预热设置抽屉内容：首次打开设置若在主线程即时 inflate 整个 SettingsPage
        // 会卡顿数百毫秒。启动完成后的空闲时段提前创建，打开抽屉时就只剩纯动画。
        Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(3), () =>
        {
            try
            {
                if (!_isSettingsPanelOpen)
                    EnsureSettingsContent();
            }
            catch (Exception ex)
            {
                Log.Debug("SearchPage.xaml", $"Settings prewarm error: {ex.Message}");
            }
        });

        // 开放所有接口：渲染插件贡献的发现子 tab（鸭子类型 IDiscoverTabPlugin）与整页入口（IViewContributorPlugin）
        InitializePluginUi();
    }

    // === 基类抽象属性实现 ===

    protected override SearchViewModel Vm => _vm;
    protected override PlayQueue Queue => _queue;
    protected override IAudioPlayerService AudioPlayer => _audioPlayer;
    protected override ListeningStatsView StatsView => _statsView;
    protected override CollectionView ChatMessagesListControl => ChatMessagesList;
    protected override Entry SearchBoxControl => SearchBox;
    protected override (Border, Label)[] TabControls => new[]
    {
        (TabRec, TabRecLabel),
        (TabRank, TabRankLabel),
        (TabArtist, TabArtistLabel),
        (TabAlbum, TabAlbumLabel),
        (TabStats, TabStatsLabel)
    }.Concat(_pluginTabControls).ToArray();

    protected override IServiceProvider Services => _services;
    protected override Grid CategoryTabBarControl => CategoryTabBar;
    protected override Layout PluginEntriesRootControl => PluginEntriesBar;

    /// <summary>覆盖基类播放逻辑：确保被点击的歌曲（如 AI 推荐歌）一定在播放队列里，
    /// 否则 PlayQueue.SelectSong 找不到该 Id 会把 CurrentSong 置空，导致声音在播但歌词/封面无法刷新。</summary>
    protected override async Task PlaySongAsync(Song song, IReadOnlyList<Song> songs)
    {
        try
        {
            var queueSongs = songs.ToList();
            if (queueSongs.All(s => s.Id != song.Id))
                queueSongs.Insert(0, song);

            if (queueSongs.Count > 0)
                _queue.SetSongs(queueSongs);

            _queue.SelectSong(song.Id);
            if (!string.IsNullOrWhiteSpace(song.FilePath))
                await _audioPlayer.PlayAsync(song.FilePath);

            // 不再跳转播放页，迷你播放器会自动弹出
        }
        catch (Exception ex)
        {
            await DisplayAlert("播放失败", ex.Message, "确定");
        }
    }

    // === 聊天迷你播放器 ===

    private void OnNowPlayingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NowPlayingViewModel.Title) ||
            e.PropertyName == nameof(NowPlayingViewModel.CurrentSong))
        {
            MainThread.BeginInvokeOnMainThread(UpdateChatMiniPlayerVisibility);
        }
    }

    /// <summary>SearchViewModel 属性变更处理：聊天模式切换时管理迷你播放器可见性和输入框焦点。</summary>
    private void OnSearchVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_vm.IsChatMode) && _vm.IsChatMode)
        {
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(300), () =>
            {
                ChatInputBox?.Focus();
            });
            UpdateChatMiniPlayerVisibility();
        }
        else if (e.PropertyName == nameof(_vm.IsChatMode) && !_vm.IsChatMode)
        {
            UpdateChatMiniPlayerVisibility();
        }
    }

    /// <summary>滚动到最新聊天消息。</summary>
    private void OnScrollToLatestMessageRequested(object? sender, EventArgs e) => ScrollToLatestMessage();

    private void UpdateChatMiniPlayerVisibility()
    {
        if (!_vm.IsChatMode)
        {
            ChatMiniPlayer.IsVisible = false;
            ChatMiniPlayer.HeightRequest = 0;
            return;
        }
        var hasSong = !string.IsNullOrEmpty(_nowPlayingVm.Title);
        ChatMiniPlayer.IsVisible = hasSong;
        ChatMiniPlayer.HeightRequest = hasSong ? 52 : 0;
    }

    private void OnChatMiniPlayerTapped(object? sender, EventArgs e)
    {
#if WINDOWS
        DesktopMainPage.Instance?.SwitchToNamedTab("playing");
#else
        MainPage.Instance?.SwitchToTab(0);
#endif
    }

    // === Hero 自动轮播（竖屏 CarouselView） ===

    private void SetupHeroAutoScroll()
    {
        _heroAutoScrollTimer = Dispatcher.CreateTimer();
        _heroAutoScrollTimer.Interval = TimeSpan.FromSeconds(4);
        _heroAutoScrollTimer.Tick += OnHeroAutoScrollTick;

        HeroCarousel.PositionChanged += (s, e) =>
        {
            _heroCurrentPosition = e.CurrentPosition;
            RestartHeroTimer();
        };
    }

    private void OnHeroAutoScrollTick(object? sender, EventArgs e)
    {
        if (_vm.HeroCards.Count == 0) return;
        if (!IsVisible) return;
        // 设置抽屉打开时轮播被遮挡、聊天模式下轮播被隐藏，均不做无用的滚动
        if (_isSettingsPanelOpen || _vm.IsChatMode) return;
        _heroCurrentPosition = (_heroCurrentPosition + 1) % _vm.HeroCards.Count;
        HeroCarousel.ScrollTo(_heroCurrentPosition, position: ScrollToPosition.Center, animate: true);
    }

    /// <summary>用户交互（触摸/滚动/Tab 滑动）期间暂停英雄卡自动轮播，交互结束后恢复倒计时。</summary>
    private void OnInteractionStateChangedForHero(object? sender, bool interacting)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (interacting)
            {
                _heroAutoScrollTimer?.Stop();
            }
            else if (IsVisible && !_isSettingsPanelOpen && !_vm.IsChatMode && _vm.HeroCards.Count > 0)
            {
                RestartHeroTimer();
            }
        });
    }

    private void RestartHeroTimer()
    {
        if (_heroAutoScrollTimer == null) return;
        _heroAutoScrollTimer.Stop();
        _heroAutoScrollTimer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _heroAutoScrollTimer?.Stop();
        // 离开聊天页：注销浏览器预览宿主（Agent 浏览器请求由协调器超时兜底）
        CatClawMusic.Maui.Services.AgentBrowser.AgentBrowserCoordinator.Instance.UnregisterHost(AgentBrowserPreview);
    }

    /// <summary>当页面显示在屏幕上时触发。若扫描后有 NeedsReload 标记则强制重载，否则仅首次加载以避免重复解码封面。</summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // 注册为 Agent 浏览器宿主（browser_open 工具在聊天页顶部弹出预览）
        CatClawMusic.Maui.Services.AgentBrowser.AgentBrowserCoordinator.Instance.RegisterHost(AgentBrowserPreview);
        _vm.GreetingText = CalculateGreeting();
        _vm.RefreshOnlineProviders(); // 刷新已启用在线音源（插件安装/启用后入口即时更新）

        if (_vm.HeroCards.Count > 0)
        {
            RestartHeroTimer();
        }

        if (LocalScanService.NeedsReload)
        {
            LocalScanService.NeedsReload = false;
            try { await _vm.ReloadAfterScanAsync(); }
            catch (Exception ex) { Log.Debug("SearchPage.xaml", $"SearchPage reload after scan: {ex.Message}"); }
            if (_vm.HeroCards.Count > 0)
            {
                _heroCurrentPosition = 0;
                RestartHeroTimer();
            }
            return;
        }

        if (_vm.DailyRecommendSongs.Count > 0 || _vm.TopPlayedSongs.Count > 0) return;

        try
        {
            await _vm.LoadExploreDataAsync();
        }
        catch (Exception ex)
        {
            Log.Debug("SearchPage.xaml", $"SearchPage OnAppearing error: {ex.Message}");
        }

        if (_vm.HeroCards.Count > 0)
        {
            _heroCurrentPosition = 0;
            RestartHeroTimer();
        }
    }

    // === 竖屏专属：搜索结果与歌曲卡片点击 ===

    /// <summary>在搜索结果中选中某首歌曲时触发，清除选中状态并播放该歌曲。</summary>
    private async void OnSearchResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Song song) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;

        SearchBox.Text = "";
        _vm.ClearSearchDropdown();

        var allSongs = _vm.DailyRecommendSongs
            .Concat(_vm.TopPlayedSongs)
            .Concat(_vm.RecentAddedSongs)
            .ToList();
        await PlaySongAsync(song, allSongs);
    }

    /// <summary>选中在线音乐搜索结果时触发：取播放直链 → 构造临时 Song → 接入现有播放链路。</summary>
    private async void OnOnlineSearchResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is CollectionView cv) cv.SelectedItem = null;
        if (e.CurrentSelection.FirstOrDefault() is not OnlineSong song) return;

        string? playUrl = null;
        try
        {
            playUrl = await _services.GetRequiredService<OnlineMusicAggregator>().GetPlayUrlAsync(song);
        }
        catch (Exception ex)
        {
            Log.Debug("SearchPage.xaml", $"[OnlineMusic] GetPlayUrl failed: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(playUrl))
        {
            await DisplayAlert("暂不可播放", $"{song.PlatformName} 当前未接入播放直链，可尝试其他音源", "确定");
            return;
        }

        SearchBox.Text = "";
        _vm.ClearSearchDropdown();

        // 构造临时 Song 接入现有播放链路（不落库；FilePath 为临时直链，播放页正常显示标题/进度）
        var tmp = new Song
        {
            Id = -1,
            Title = song.Title,
            Artist = song.Artist,
            Album = song.Album,
            Duration = (int)(song.DurationMs / 1000),
            FilePath = playUrl,
            RemoteId = $"{song.Platform}:{song.Id}",
            Source = SongSource.Local,
            AllArtists = song.Artist
        };
        await PlaySongAsync(tmp, new List<Song> { tmp });
    }

    /// <summary>点击 AI 助手头像时触发，进入 AI 聊天模式。</summary>
    private void OnYukiAvatarClicked(object? sender, EventArgs e)
    {
        _vm.EnterChatModeCommand.Execute(null);
    }

    /// <summary>点击"每日推荐"快捷入口时触发，切换到推荐Tab。</summary>
    private void OnQuickDailyTapped(object? sender, TappedEventArgs e) => _vm.CurrentCategory = 0;

    /// <summary>点击"最热播放"快捷入口时触发，切换到排行榜Tab。</summary>
    private void OnQuickTopPlayedTapped(object? sender, TappedEventArgs e) => _vm.CurrentCategory = 1;

    /// <summary>点击"最近添加"快捷入口时触发，切换到推荐Tab。</summary>
    private void OnQuickRecentTapped(object? sender, TappedEventArgs e) => _vm.CurrentCategory = 0;

    /// <summary>点击"查看全部"按钮（推荐艺人/推荐歌手）时触发，导航到全部艺术家列表页。</summary>
    private void OnViewAllArtistsClicked(object? sender, EventArgs e) => DesktopNavigation.TryGoToShell("artists");

    /// <summary>点击"查看全部"按钮（推荐专辑）时触发，导航到全部专辑列表页。</summary>
    private void OnViewAllAlbumsClicked(object? sender, EventArgs e) => DesktopNavigation.TryGoToShell("albums");

    /// <summary>点击"查看全部"按钮（最多播放）时触发，导航到最多播放歌单详情页（系统虚拟歌单 Id=-4）。</summary>
    private void OnViewAllTopPlayedClicked(object? sender, EventArgs e)
        => DesktopNavigation.TryGoToShell($"playlistdetail?playlistId=-4&name={Uri.EscapeDataString("最多播放")}");

    /// <summary>点击"查看全部"按钮（我的最爱）时触发，导航到收藏歌曲歌单详情页（系统虚拟歌单 Id=-2）。</summary>
    private void OnViewAllFavoritesClicked(object? sender, EventArgs e)
        => DesktopNavigation.TryGoToShell($"playlistdetail?playlistId=-2&name={Uri.EscapeDataString("收藏歌曲")}");

    /// <summary>滚动到指定元素位置（适配 CollectionView 的实现）。</summary>
    private async Task ScrollToElementAsync(VisualElement element)
    {
        try
        {
#if ANDROID
            if (DiscoverCollection.Handler?.PlatformView is global::Android.Views.View nativeView
                && element.Handler?.PlatformView is global::Android.Views.View targetView)
            {
                int[] location = new int[2];
                targetView.GetLocationInWindow(location);
                int[] collectionLocation = new int[2];
                nativeView.GetLocationInWindow(collectionLocation);
                int top = location[1] - collectionLocation[1];
                nativeView.ScrollY = top;
            }
#else
            throw new NotSupportedException();
#endif
        }
        catch
        {
            if (DiscoverCollection.ItemsSource is System.Collections.IEnumerable items && items.Cast<object>().Any())
            {
                DiscoverCollection.ScrollTo(items.Cast<object>().First(), position: ScrollToPosition.Start, animate: true);
            }
        }
    }

    /// <summary>点击"前往音乐库"按钮时触发，切换到主界面的音乐库标签页。</summary>
    private void OnGoLibraryClicked(object? sender, EventArgs e)
    {
#if WINDOWS
        DesktopMainPage.Instance?.SwitchToNamedTab("library");
#else
        MainPage.Instance?.SwitchToTab(3);
#endif
    }

    /// <summary>点击歌曲卡片时触发，根据卡片所属区块播放该歌曲及对应列表。</summary>
    private async void OnSongCardTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Border border || border.BindingContext is not Song song) return;

        IReadOnlyList<Song> songs = border.ClassId switch
        {
            "Daily" => _vm.DailyRecommendSongs.ToList(),
            "TopPlayed" => _vm.TopPlayedSongs.ToList(),
            "Recent" => _vm.RecentAddedSongs.ToList(),
            _ => new List<Song>()
        };

        await PlaySongAsync(song, songs);
    }

    /// <summary>点击主推歌曲卡片时触发，播放该主推歌曲及每日推荐列表。</summary>
    private async void OnHeroCardTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border border && border.BindingContext is HeroCardItem heroItem && heroItem.Song != null)
            await PlaySongAsync(heroItem.Song, _vm.DailyRecommendSongs.ToList());
    }

    /// <summary>在最近添加列表中选中某首歌曲时触发，清除选中状态并播放该歌曲。</summary>
    private async void OnRecentSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Song song) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;
        await PlaySongAsync(song, _vm.RecentAddedSongs.ToList());
    }

    /// <summary>点击聊天中的歌曲搜索结果卡片：以该消息的搜索结果作为播放队列播放。</summary>
    private async void OnChatSongTapped(object? sender, EventArgs e)
    {
        if (sender is not VisualElement el || el.BindingContext is not Song song) return;
        var queue = FindMessageSongs(el) ?? new List<Song> { song };
        await PlaySongAsync(song, queue);
    }

    /// <summary>沿可视树向上查找所属聊天消息的 Songs 列表（作为播放队列）。</summary>
    private static List<Song>? FindMessageSongs(VisualElement element)
    {
        Element? current = element;
        while (current != null)
        {
            if (current.BindingContext is ViewModels.ObservableChatMessage msg && msg.Songs is { Count: > 0 })
                return msg.Songs;
            current = current.Parent;
        }
        return null;
    }

    /// <summary>点击搜索结果底部的"问问 Yuki"入口时触发，将当前搜索词作为消息发送给 AI。</summary>
    private async void OnAskYukiTapped(object? sender, TappedEventArgs e)
    {
        var searchQuery = _vm.SearchQuery?.Trim();
        SearchBox.Text = "";
        _vm.ClearSearchDropdown();

        var message = string.IsNullOrWhiteSpace(searchQuery)
            ? "你好"
            : $"帮我找一下关于「{searchQuery}」的歌曲";

        await _vm.SendMessageFromSearchAsync(message);
    }

    // === Hero 播放按钮、换一批、刷新 ===

    private async void OnHeroPlayTapped(object? sender, EventArgs e)
    {
        if (sender is ImageButton btn && btn.BindingContext is HeroCardItem heroItem && heroItem.Song != null)
            await PlaySongAsync(heroItem.Song, _vm.DailyRecommendSongs.ToList());
    }

    private void OnShuffleDailyClicked(object? sender, EventArgs e) => _vm.ShuffleDailyCommand.Execute(null);

    /// <summary>点击 AI 歌单卡片：播放歌单全部歌曲</summary>
    private async void OnAiPlaylistTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not AiPlaylist playlist) return;
        if (playlist.Songs.Count == 0) return;
        await PlaySongAsync(playlist.Songs[0], playlist.Songs);
    }

    private async void OnRefreshClicked(object? sender, EventArgs e)
    {
        if (_vm.IsLoading) return;
        _ = _vm.RefreshCommand.ExecuteAsync(null);
    }

    // === 设置抽屉（竖屏专属：从左侧滑出的毛玻璃面板） ===

    /// <summary>点击汉堡菜单按钮，从左到右滑出设置面板</summary>
    private void OnHamburgerClicked(object? sender, EventArgs e)
    {
        DesktopNavigation.TryGoToShell("settings");
    }

    /// <summary>创建并嵌入设置页面内容（幂等）。点击汉堡按钮与空闲预热时调用。</summary>
    private void EnsureSettingsContent()
    {
        if (_settingsPage != null) return;
        _settingsPage = _services.GetRequiredService<SettingsPage>();
        var settingsContent = _settingsPage.Content;
        _settingsPage.Content = null;
        settingsContent.BindingContext = _settingsPage.BindingContext;
        // 抽屉自身是半透明毛玻璃（透出下方发现页），需清掉内容自带的页面背景，避免 opaque 渐变盖住玻璃
        if (settingsContent is Grid settingsGrid)
            settingsGrid.Background = null;
        SettingsPanelContent.Content = settingsContent;
    }

    /// <summary>点击背景遮罩收起设置面板</summary>
    private async void OnSettingsBackdropTapped(object? sender, TappedEventArgs e) => await CloseSettingsPanel();

    /// <summary>收起设置面板：面板滑出 + 背景淡出</summary>
    private async Task CloseSettingsPanel()
    {
        if (!_isSettingsPanelOpen) return;
        _isSettingsPanelOpen = false;
        // 抽屉关闭后恢复英雄卡自动轮播
        if (IsVisible && _vm.HeroCards.Count > 0)
            RestartHeroTimer();

        var panelWidth = SettingsPanel.Width > 0 ? SettingsPanel.Width : Width * 0.85;

        await Task.WhenAll(
            SettingsBackdrop.FadeTo(0, 250, Easing.CubicIn),
            SettingsPanel.TranslateTo(-panelWidth, 0, 280, Easing.CubicIn)
        );

#if ANDROID
        RemoveBlurFromSettingsSiblings();
#endif

        SettingsPanelOverlay.IsVisible = false;
    }

#if ANDROID
    /// <summary>对设置面板背后的兄弟视图（发现页主内容）应用高斯模糊 RenderEffect，形成与播放列表弹窗一致的全屏磨砂遮罩</summary>
    private void ApplyBlurToSettingsSiblings()
    {
        _settingsBlurredViews.Clear();

        if (SettingsPanelOverlay.Parent is Microsoft.Maui.Controls.Layout layout)
        {
            foreach (var child in layout.Children)
            {
                if (child == SettingsPanelOverlay) continue;
                if (child is Microsoft.Maui.Controls.View view &&
                    view.Handler?.PlatformView is global::Android.Views.View nativeView)
                {
                    nativeView.SetRenderEffect(
                        global::Android.Graphics.RenderEffect.CreateBlurEffect(
                            24, 24, global::Android.Graphics.Shader.TileMode.Clamp));
                    _settingsBlurredViews.Add(nativeView);
                }
            }
        }
    }

    /// <summary>移除设置面板背后兄弟视图上的高斯模糊</summary>
    private void RemoveBlurFromSettingsSiblings()
    {
        foreach (var view in _settingsBlurredViews)
        {
            try { view.SetRenderEffect(null); } catch { }
        }
        _settingsBlurredViews.Clear();
    }
#endif

    private void OnSettingsClicked(object? sender, EventArgs e) => OnHamburgerClicked(sender, e);
}
