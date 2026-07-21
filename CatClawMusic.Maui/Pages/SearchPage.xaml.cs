using System.ComponentModel;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Services;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.Pages;

/// <summary>搜索页面，提供歌曲、艺术家、专辑的搜索功能，并展示每日推荐、最热播放等发现内容。</summary>
public partial class SearchPage : ContentPage
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
    /// <param name="db">音乐数据库访问对象。</param>
    /// <param name="queue">播放队列。</param>
    /// <param name="vm">搜索页面对应的视图模型。</param>
    /// <param name="audioPlayer">音频播放服务。</param>
    /// <param name="services">服务提供程序，用于解析设置页面。</param>
    /// <param name="nowPlayingVm">当前播放视图模型，用于驱动聊天模式下的迷你播放器。</param>
    /// <param name="statsView">听歌统计视图，嵌入到"报告"Tab。</param>
    public SearchPage(MusicDatabase db, PlayQueue queue, SearchViewModel vm, IAudioPlayerService audioPlayer, IServiceProvider services, NowPlayingViewModel nowPlayingVm, ListeningStatsView statsView)
    {
        InitializeComponent();
        _db = db;
        _queue = queue;
        _vm = vm;
        _audioPlayer = audioPlayer;
        _services = services;
        _interactionState = services.GetService<Services.IInteractionStateService>();
        if (_interactionState != null)
            _interactionState.InteractionStateChanged += OnInteractionStateChangedForHero;
        _nowPlayingVm = nowPlayingVm;
        _statsView = statsView;
        BindingContext = _vm;
        UpdateTabVisualState(0);
        SetupHeroAutoScroll();

        // 将听歌统计视图添加到"报告"面板
        PanelStats.Children.Add(_statsView);

        ChatBackButton.Clicked += OnChatBackClicked;

        ChatMiniPlayer.BindingContext = _nowPlayingVm;
        _nowPlayingVm.PropertyChanged += OnNowPlayingPropertyChanged;

        _vm.ChatHistoryLoaded += OnChatHistoryLoaded;
        _vm.ScrollToLatestMessageRequested += (s, e) => ScrollToLatestMessage();

        _vm.PropertyChanged += (s, e) =>
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
    }

    private void OnNowPlayingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NowPlayingViewModel.Title) ||
            e.PropertyName == nameof(NowPlayingViewModel.CurrentSong))
        {
            MainThread.BeginInvokeOnMainThread(UpdateChatMiniPlayerVisibility);
        }
    }

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

    private void OnChatHistoryLoaded(object? sender, ChatHistoryLoadedEventArgs e)
    {
        // 倒序模式下：首次加载需要滚到 index 0（最新消息，翻转后视觉在底部）
        // 加载更多历史时无需处理（末尾追加不改变已有项位置）
        if (e is { IsInitialLoad: true, ScrollToEnd: true } && _vm.ChatMessages.Count > 0)
        {
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(100), () =>
            {
                ChatMessagesList.ScrollTo(0, position: ScrollToPosition.Start, animate: false);
            });
        }
    }

    private void ScrollToLatestMessage()
    {
        // 倒序模式：最新消息在 index 0，翻转后视觉在底部，滚动到 Start 即可
        if (_vm.ChatMessages.Count > 0)
        {
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), () =>
            {
                ChatMessagesList.ScrollTo(0, position: ScrollToPosition.Start, animate: true);
            });
        }
    }

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

    /// <summary>用户交互（触摸/滚动/Tab 滑动）期间暂停英雄卡自动轮播，交互结束后恢复倒计时。
    /// 避免轮播 ScrollTo 与用户手势争抢主线程，也避免手指停留时卡片在眼前自己滚走。</summary>
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
    }

    /// <summary>当页面显示在屏幕上时触发。若扫描后有 NeedsReload 标记则强制重载，否则仅首次加载以避免重复解码封面。</summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _vm.GreetingText = CalculateGreeting();

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

    /// <summary>当搜索输入框完成输入（回车）时触发，提交搜索查询并取消输入框焦点。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnSearchCompleted(object? sender, EventArgs e)
    {
        var entry = sender as Entry;
        _vm.SearchQuery = entry?.Text?.Trim() ?? "";
        entry?.Unfocus();
    }

    /// <summary>当搜索输入框文本发生改变时触发，实时更新搜索查询以刷新下拉结果。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">文本变更事件参数。</param>
    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _vm.SearchQuery = e.NewTextValue ?? "";
    }

    /// <summary>点击清除搜索按钮时触发，清空搜索框文本并关闭下拉结果。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnClearSearchClicked(object? sender, EventArgs e)
    {
        SearchBox.Text = "";
        _vm.ClearSearchDropdown();
    }

    /// <summary>在搜索结果中选中某首歌曲时触发，清除选中状态并播放该歌曲。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">选择变更事件参数。</param>
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

    /// <summary>在搜索结果中选中某个艺术家时触发，清除选中状态并导航到该艺术家详情页。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">选择变更事件参数。</param>
    private async void OnSearchArtistSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SearchArtistItem artist) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;

        SearchBox.Text = "";
        _vm.ClearSearchDropdown();

        await Shell.Current.GoToAsync($"artistdetail?artistName={Uri.EscapeDataString(artist.Name)}");
    }

    /// <summary>在搜索结果中选中某个专辑时触发，清除选中状态并导航到该专辑详情页。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">选择变更事件参数。</param>
    private async void OnSearchAlbumSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SearchAlbumItem album) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;

        SearchBox.Text = "";
        _vm.ClearSearchDropdown();

        await Shell.Current.GoToAsync($"albumdetail?title={Uri.EscapeDataString(album.Title)}");
    }

    /// <summary>点击 AI 助手头像时触发，进入 AI 聊天模式。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnYukiAvatarClicked(object? sender, EventArgs e)
    {
        _vm.EnterChatModeCommand.Execute(null);
    }

    /// <summary>点击聊天界面返回按钮时触发，退出 AI 聊天模式。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnChatBackClicked(object? sender, EventArgs e)
    {
        _vm.ExitChatModeCommand.Execute(null);
    }

    /// <summary>当聊天输入框完成输入（回车）时触发，发送当前输入的消息。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnChatInputCompleted(object? sender, EventArgs e)
    {
        _ = _vm.SendMessageCommand.ExecuteAsync(null);
    }

    /// <summary>点击聊天发送按钮时触发，发送当前输入的消息。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnSendClicked(object? sender, EventArgs e)
    {
        _ = _vm.SendMessageCommand.ExecuteAsync(null);
    }

    /// <summary>点击“每日推荐”快捷入口时触发，切换到推荐Tab。</summary>
    private void OnQuickDailyTapped(object? sender, TappedEventArgs e)
    {
        _vm.CurrentCategory = 0;
    }

    /// <summary>点击“最热播放”快捷入口时触发，切换到排行榜Tab。</summary>
    private void OnQuickTopPlayedTapped(object? sender, TappedEventArgs e)
    {
        _vm.CurrentCategory = 1;
    }

    /// <summary>点击“最近添加”快捷入口时触发，切换到推荐Tab。</summary>
    private void OnQuickRecentTapped(object? sender, TappedEventArgs e)
    {
        _vm.CurrentCategory = 0;
    }

    /// <summary>点击“查看全部”按钮（推荐艺人/推荐歌手）时触发，导航到全部艺术家列表页。</summary>
    private async void OnViewAllArtistsClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("artists");
    }

    /// <summary>点击“查看全部”按钮（推荐专辑）时触发，导航到全部专辑列表页。</summary>
    private async void OnViewAllAlbumsClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("albums");
    }

    /// <summary>点击“查看全部”按钮（最多播放）时触发，导航到最多播放歌单详情页（系统虚拟歌单 Id=-4）。</summary>
    private async void OnViewAllTopPlayedClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync($"playlistdetail?playlistId=-4&name={Uri.EscapeDataString("最多播放")}");
    }

    /// <summary>点击“查看全部”按钮（我的最爱）时触发，导航到收藏歌曲歌单详情页（系统虚拟歌单 Id=-2）。</summary>
    private async void OnViewAllFavoritesClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync($"playlistdetail?playlistId=-2&name={Uri.EscapeDataString("收藏歌曲")}");
    }

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

    /// <summary>点击“前往音乐库”按钮时触发，切换到主界面的音乐库标签页。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnGoLibraryClicked(object? sender, EventArgs e)
    {
#if WINDOWS
        DesktopMainPage.Instance?.SwitchToNamedTab("library");
#else
        MainPage.Instance?.SwitchToTab(3);
#endif
    }

    /// <summary>在发现页艺术家列表中选中某个艺术家时触发，清除选中状态并导航到该艺术家详情页。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">选择变更事件参数。</param>
    private async void OnArtistSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SearchArtistItem artist)
        {
            return;
        }

        if (sender is CollectionView collectionView)
        {
            collectionView.SelectedItem = null;
        }

        await Shell.Current.GoToAsync($"artistdetail?artistName={Uri.EscapeDataString(artist.Name)}");
    }

    /// <summary>在发现页专辑列表中选中某个专辑时触发，清除选中状态并导航到该专辑详情页。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">选择变更事件参数。</param>
    private async void OnAlbumSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SearchAlbumItem album)
        {
            return;
        }

        if (sender is CollectionView collectionView)
        {
            collectionView.SelectedItem = null;
        }

        await Shell.Current.GoToAsync($"albumdetail?title={Uri.EscapeDataString(album.Title)}");
    }

    /// <summary>点击歌曲卡片时触发，根据卡片所属区块播放该歌曲及对应列表。</summary>
    /// <param name="sender">事件源，通常为携带歌曲上下文的边框控件。</param>
    /// <param name="e">点击事件参数。</param>
    private async void OnSongCardTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Border border || border.BindingContext is not Song song)
        {
            return;
        }

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
    /// <param name="sender">事件源。</param>
    /// <param name="e">点击事件参数。</param>
    private async void OnHeroCardTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border border && border.BindingContext is HeroCardItem heroItem && heroItem.Song != null)
        {
            await PlaySongAsync(heroItem.Song, _vm.DailyRecommendSongs.ToList());
        }
    }

    /// <summary>在每日推荐列表中选中某首歌曲时触发，清除选中状态并播放该歌曲。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">选择变更事件参数。</param>
    private async void OnDailySongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Song song) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;
        await PlaySongAsync(song, _vm.DailyRecommendSongs.ToList());
    }

    /// <summary>在最热播放列表中选中某首歌曲时触发，清除选中状态并播放该歌曲。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">选择变更事件参数。</param>
    private async void OnTopPlayedSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Song song) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;
        await PlaySongAsync(song, _vm.TopPlayedSongs.ToList());
    }

    /// <summary>在最近添加列表中选中某首歌曲时触发，清除选中状态并播放该歌曲。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">选择变更事件参数。</param>
    private async void OnRecentSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Song song) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;
        await PlaySongAsync(song, _vm.RecentAddedSongs.ToList());
    }

    private async Task PlaySongAsync(Song song, IReadOnlyList<Song> songs)
    {
        try
        {
            // 确保被点击的歌曲（如 AI 推荐歌）一定在播放队列里，
            // 否则 PlayQueue.SelectSong 找不到该 Id 会把 CurrentSong 置空，
            // 导致声音在播但歌词/封面无法刷新（Hero 卡片点击 Bug）。
            var queueSongs = songs.ToList();
            if (queueSongs.All(s => s.Id != song.Id))
            {
                queueSongs.Insert(0, song);
            }

            if (queueSongs.Count > 0)
            {
                _queue.SetSongs(queueSongs);
            }

            _queue.SelectSong(song.Id);
            if (!string.IsNullOrWhiteSpace(song.FilePath))
            {
                await _audioPlayer.PlayAsync(song.FilePath);
            }

            // 不再跳转播放页，迷你播放器会自动弹出
        }
        catch (Exception ex)
        {
            await DisplayAlert("播放失败", ex.Message, "确定");
        }
    }

    /// <summary>点击汉堡菜单按钮，从左到右滑出设置面板</summary>
    private async void OnHamburgerClicked(object? sender, EventArgs e)
    {
        if (_isSettingsPanelOpen) return;

        // 首次打开时创建设置页面内容（正常情况下已在空闲时预热，见构造函数）
        EnsureSettingsContent();

        // 每次打开面板时刷新设置状态（嵌入面板不会触发 OnAppearing）
        if (_settingsPage?.BindingContext is SettingsViewModel svm)
        {
            _ = svm.LoadStatusCommand.ExecuteAsync(null);
        }

        // 设置面板宽度为屏幕宽度的 85%
        var panelWidth = Width > 0 ? Width * 0.85 : 300;
        SettingsPanel.WidthRequest = panelWidth;
        SettingsPanel.TranslationX = -panelWidth;

        _isSettingsPanelOpen = true;
        SettingsPanelOverlay.IsVisible = true;
        // 抽屉遮住了英雄卡，暂停自动轮播（关闭时恢复）
        _heroAutoScrollTimer?.Stop();

        // 确保布局已计算完成
        await Task.Delay(16);

        // 背景渐入 + 面板从左侧滑入。先启动动画再应用模糊，
        // 让 RenderEffect 的设置开销与动画并行，而不是卡在动画开始前
        var animationTask = Task.WhenAll(
            SettingsBackdrop.FadeTo(0.5, 250, Easing.CubicOut),
            SettingsPanel.TranslateTo(0, 0, 280, Easing.CubicOut)
        );

#if ANDROID
        ApplyBlurToSettingsSiblings();
#endif

        await animationTask;
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
    private async void OnSettingsBackdropTapped(object? sender, TappedEventArgs e)
    {
        await CloseSettingsPanel();
    }

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

    private void OnSearchToggleClicked(object? sender, EventArgs e)
    {
        _vm.IsSearchOpen = !_vm.IsSearchOpen;
        if (_vm.IsSearchOpen)
        {
            SearchBox.Focus();
        }
        else
        {
            SearchBox.Unfocus();
            SearchBox.Text = "";
            _vm.ClearSearchDropdown();
        }
    }

    /// <summary>点击 AI 助手入口卡：进入聊天模式，关闭搜索下拉。</summary>
    private void OnAiEntryTapped(object? sender, TappedEventArgs e)
    {
        _vm.IsSearchOpen = false;
        SearchBox.Unfocus();
        SearchBox.Text = "";
        _vm.ClearSearchDropdown();
        _vm.EnterChatModeCommand.Execute(null);
    }

    private async void OnRefreshClicked(object? sender, EventArgs e)
    {
        if (_vm.IsLoading) return;
        _ = _vm.RefreshCommand.ExecuteAsync(null);
    }

    private void OnSettingsClicked(object? sender, EventArgs e)
    {
        OnHamburgerClicked(sender, e);
    }

    private void OnCategoryTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is string paramStr && int.TryParse(paramStr, out int index))
        {
            _vm.CurrentCategory = index;
            UpdateTabVisualState(index);
            // 切换到"报告"Tab 时触发统计数据加载
            if (index == 4)
            {
                _ = _statsView.LoadAsync();
            }
        }
    }

    private void UpdateTabVisualState(int selectedIndex)
    {
        var primaryColor = (Color)Application.Current?.Resources["PrimaryColor"]!;
        var cardBg = (Color)Application.Current?.Resources["CardBackgroundColor"]!;
        var white = Colors.White;
        var textSecondary = (Color)Application.Current?.Resources["TextSecondaryColor"]!;

        TabRec.BackgroundColor = selectedIndex == 0 ? primaryColor : cardBg;
        TabRecLabel.TextColor = selectedIndex == 0 ? white : textSecondary;

        TabRank.BackgroundColor = selectedIndex == 1 ? primaryColor : cardBg;
        TabRankLabel.TextColor = selectedIndex == 1 ? white : textSecondary;

        TabArtist.BackgroundColor = selectedIndex == 2 ? primaryColor : cardBg;
        TabArtistLabel.TextColor = selectedIndex == 2 ? white : textSecondary;

        TabAlbum.BackgroundColor = selectedIndex == 3 ? primaryColor : cardBg;
        TabAlbumLabel.TextColor = selectedIndex == 3 ? white : textSecondary;

        TabStats.BackgroundColor = selectedIndex == 4 ? primaryColor : cardBg;
        TabStatsLabel.TextColor = selectedIndex == 4 ? white : textSecondary;
    }

    private async void OnHeroPlayTapped(object? sender, EventArgs e)
    {
        if (sender is ImageButton btn && btn.BindingContext is HeroCardItem heroItem && heroItem.Song != null)
        {
            await PlaySongAsync(heroItem.Song, _vm.DailyRecommendSongs.ToList());
        }
    }

    private void OnShuffleDailyClicked(object? sender, EventArgs e)
    {
        _vm.ShuffleDailyCommand.Execute(null);
    }

    private async void OnRankPlayTapped(object? sender, EventArgs e)
    {
        if (sender is ImageButton btn && btn.BindingContext is Song song)
        {
            await PlaySongAsync(song, _vm.TopPlayedSongs.ToList());
        }
    }

    private async void OnFavPlayTapped(object? sender, EventArgs e)
    {
        if (sender is ImageButton btn && btn.BindingContext is Song song)
        {
            await PlaySongAsync(song, _vm.FavoriteSongs.ToList());
        }
    }

    private void OnRankItemBindingContextChanged(object? sender, EventArgs e)
    {
        if (sender is not Border border) return;
        if (border.BindingContext is not Song song) return;

        var list = _vm.TopPlayedSongs;
        var index = list.IndexOf(song);
        if (index < 0) return;

        var rank = index + 1;
        if (border.Content is Grid grid && grid.Children.Count > 0 && grid.Children[0] is Label rankLabel)
        {
            rankLabel.Text = rank.ToString();
            rankLabel.TextColor = rank <= 3
                ? (Color)Application.Current?.Resources["PrimaryColor"]!
                : (Color)Application.Current?.Resources["TextHintColor"]!;
        }
    }

    private static string CalculateGreeting()
    {
        var hour = DateTime.Now.Hour;
        return hour switch
        {
            >= 0 and < 6 => "凌晨好，为你精选深夜好歌",
            >= 6 and < 12 => "早上好，为你精选晨间好歌",
            >= 12 and < 18 => "下午好，为你精选午后好歌",
            _ => "晚上好，为你精选今日好歌"
        };
    }

    private async void OnRankItemTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border border && border.BindingContext is Song song)
        {
            await PlaySongAsync(song, _vm.TopPlayedSongs.ToList());
        }
    }

    private async void OnFavItemTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border border && border.BindingContext is Song song)
        {
            await PlaySongAsync(song, _vm.FavoriteSongs.ToList());
        }
    }

    private async void OnFavoriteSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Song song) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;
        await PlaySongAsync(song, _vm.FavoriteSongs.ToList());
    }

    /// <summary>点击搜索结果底部的"问问 Yuki"入口时触发，将当前搜索词作为消息发送给 AI</summary>
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

    /// <summary>聊天消息列表滚动时检测是否需要加载更多历史记录
    /// 倒序+翻转模式：视觉底部 = 数据源末尾 = 最旧消息，滚到底部时加载更旧的历史</summary>
    private async void OnChatMessagesScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        // 翻转后 VerticalOffset 语义反转：滚到视觉底部时 offset 接近 0
        if (e.VerticalOffset < 30 && _vm.HasMoreChatHistory)
        {
            await _vm.LoadMoreChatHistoryAsync();
        }
    }
}
