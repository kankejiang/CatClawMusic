using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Maui.Services;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Pages;

/// <summary>搜索页面，提供歌曲、艺术家、专辑的搜索功能，并展示每日推荐、最热播放等发现内容。</summary>
public partial class SearchPage : ContentPage
{
    private readonly SearchViewModel _vm;
    private readonly PlayQueue _queue;
    private readonly MusicDatabase _db;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly IServiceProvider _services;
    private SettingsPage? _settingsPage;
    private bool _isSettingsPanelOpen;
    private IDispatcherTimer? _heroAutoScrollTimer;
    private int _heroCurrentPosition;

    /// <summary>初始化 <see cref="SearchPage"/> 类的新实例，并注入所需的服务与视图模型。</summary>
    /// <param name="db">音乐数据库访问对象。</param>
    /// <param name="queue">播放队列。</param>
    /// <param name="vm">搜索页面对应的视图模型。</param>
    /// <param name="audioPlayer">音频播放服务。</param>
    /// <param name="services">服务提供程序，用于解析设置页面。</param>
    public SearchPage(MusicDatabase db, PlayQueue queue, SearchViewModel vm, IAudioPlayerService audioPlayer, IServiceProvider services)
    {
        InitializeComponent();
        _db = db;
        _queue = queue;
        _vm = vm;
        _audioPlayer = audioPlayer;
        _services = services;
        BindingContext = _vm;
        UpdateTabVisualState(0);
        SetupHeroAutoScroll();

        ChatBackButton.Clicked += OnChatBackClicked;

        _vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(_vm.IsChatMode) && _vm.IsChatMode)
            {
                Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(200), () =>
                {
                    if (_vm.ChatMessages.Count > 0)
                        ChatMessagesList.ScrollTo(_vm.ChatMessages.Count - 1);
                    ChatInputBox?.Focus();
                });
            }
        };
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
        _heroCurrentPosition = (_heroCurrentPosition + 1) % _vm.HeroCards.Count;
        HeroCarousel.ScrollTo(_heroCurrentPosition, position: ScrollToPosition.Center, animate: true);
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
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"SearchPage reload after scan: {ex.Message}"); }
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
            System.Diagnostics.Debug.WriteLine($"SearchPage OnAppearing error: {ex.Message}");
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

        // 首次打开时创建设置页面内容
        if (_settingsPage == null)
        {
            _settingsPage = _services.GetRequiredService<SettingsPage>();
            var settingsContent = _settingsPage.Content;
            _settingsPage.Content = null;
            settingsContent.BindingContext = _settingsPage.BindingContext;
            SettingsPanelContent.Content = settingsContent;
        }

        // 每次打开面板时刷新设置状态（嵌入面板不会触发 OnAppearing）
        if (_settingsPage.BindingContext is SettingsViewModel svm)
        {
            _ = svm.LoadStatusCommand.ExecuteAsync(null);
        }

        // 设置面板宽度为屏幕宽度的 85%
        var panelWidth = Width > 0 ? Width * 0.85 : 300;
        SettingsPanel.WidthRequest = panelWidth;
        SettingsPanel.TranslationX = -panelWidth;

        _isSettingsPanelOpen = true;
        SettingsPanelOverlay.IsVisible = true;

        // 确保布局已计算完成
        await Task.Delay(16);

        // 背景渐入 + 面板从左侧滑入
        await Task.WhenAll(
            SettingsBackdrop.FadeTo(0.5, 250, Easing.CubicOut),
            SettingsPanel.TranslateTo(0, 0, 280, Easing.CubicOut)
        );
    }

    /// <summary>点击背景遮罩收起设置面板</summary>
    private async void OnSettingsBackdropTapped(object? sender, TappedEventArgs e)
    {
        await CloseSettingsPanel();
    }

    /// <summary>点击关闭按钮收起设置面板</summary>
    private async void OnSettingsCloseClicked(object? sender, EventArgs e)
    {
        await CloseSettingsPanel();
    }

    /// <summary>收起设置面板：面板滑出 + 背景淡出</summary>
    private async Task CloseSettingsPanel()
    {
        if (!_isSettingsPanelOpen) return;
        _isSettingsPanelOpen = false;

        var panelWidth = SettingsPanel.Width > 0 ? SettingsPanel.Width : Width * 0.85;

        await Task.WhenAll(
            SettingsBackdrop.FadeTo(0, 250, Easing.CubicIn),
            SettingsPanel.TranslateTo(-panelWidth, 0, 280, Easing.CubicIn)
        );

        SettingsPanelOverlay.IsVisible = false;
    }

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

    /// <summary>聊天消息列表滚动时检测是否需要加载更多历史记录</summary>
    private async void OnChatMessagesScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        // 当滚动到接近顶部时自动加载更多
        if (e.VerticalOffset < 50 && _vm.HasMoreChatHistory)
        {
            // 记录当前滚动位置和内容高度，加载后恢复位置
            var previousCount = _vm.ChatMessages.Count;
            await _vm.LoadMoreChatHistoryAsync();
            var newCount = _vm.ChatMessages.Count;
            if (newCount > previousCount)
            {
                // 加载了新条目，向下滚动到原来位置（避免跳动）
                var addedCount = newCount - previousCount;
                // 估算每条高度约60px，滚动到原位置
                Dispatcher.Dispatch(async () =>
                {
                    await Task.Delay(50);
                    if (ChatMessagesList.Handler != null)
                    {
                        // 滚动到原第一条消息（现在偏移了addedCount条）
                        var targetIndex = addedCount;
                        if (targetIndex < _vm.ChatMessages.Count)
                            ChatMessagesList.ScrollTo(targetIndex, position: ScrollToPosition.Start, animate: false);
                    }
                });
            }
        }
    }
}
