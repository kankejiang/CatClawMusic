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

        // 聊天对话框推理力度 chips：进入页面时按全局设置同步高亮
        Loaded += (_, _) => UpdateReasoningEffortChips();

        // Android：外层 ViewPager2 与内层横向卡片列表的手势仲裁——
        // 卡片横向滑动时不误切 tab（Hero/AI 歌单/推荐专辑/每日推荐/艺人）
#if ANDROID
        foreach (var cv in new CollectionView[] { HeroGrid, AiPlaylistRow, RecommendAlbumGrid, DailyList, ArtistsList })
        {
            // 双保险挂载：HandlerChanged（Handler 创建/重建时）+ Loaded（视图就绪后），
            // 避免某一时机 Handler/PlatformView 未就绪导致监听漏挂
            cv.HandlerChanged += (s, _) => TryAttachPagerGuard(s);
            cv.Loaded += (s, _) => TryAttachPagerGuard(s);
        }
#endif

        // 将听歌统计视图添加到"统计"面板
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
                _nowPlayingVm.PropertyChanged -= OnNowPlayingPropertyChanged;
                _vm.ChatHistoryLoaded -= OnChatHistoryLoaded;
                _vm.ScrollToLatestMessageRequested -= OnScrollToLatestMessageRequested;
                _vm.PropertyChanged -= OnSearchVmPropertyChanged;
            }
            else
            {
                // 页面挂载（或重新挂载）：订阅事件（先 -= 再 += 避免重复）
                _nowPlayingVm.PropertyChanged -= OnNowPlayingPropertyChanged;
                _nowPlayingVm.PropertyChanged += OnNowPlayingPropertyChanged;
                _vm.ChatHistoryLoaded -= OnChatHistoryLoaded;
                _vm.ChatHistoryLoaded += OnChatHistoryLoaded;
                _vm.ScrollToLatestMessageRequested -= OnScrollToLatestMessageRequested;
                _vm.ScrollToLatestMessageRequested += OnScrollToLatestMessageRequested;
                _vm.PropertyChanged -= OnSearchVmPropertyChanged;
                _vm.PropertyChanged += OnSearchVmPropertyChanged;
                // Hero 轮播组合源（AI 卡 + 英雄卡）：订阅时立即构建一次（数据可能已就绪）
                RebuildHeroDisplay();
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
    protected override VerticalStackLayout PluginExtensionsRootControl => PluginExtensionsBox;

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
        else if (e.PropertyName == nameof(_vm.HeroCards))
        {
            // 英雄卡数据更新（含首次加载）：重建轮播组合源（AI 卡 + 英雄卡）
            MainThread.BeginInvokeOnMainThread(RebuildHeroDisplay);
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

    // === Hero 卡片并排网格（不轮播，2 列方形铺开） ===

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
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

        if (LocalScanService.NeedsReload)
        {
            LocalScanService.NeedsReload = false;
            try { await _vm.ReloadAfterScanAsync(); }
            catch (Exception ex) { Log.Debug("SearchPage.xaml", $"SearchPage reload after scan: {ex.Message}"); }
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

    /// <summary>滚动到指定元素位置（适配 ScrollView 的实现）。</summary>
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
            await DiscoverCollection.ScrollToAsync(0, 0, true);
        }
    }

    /// <summary>
    /// 由 MainPage 调用：随迷你播放器/TabBar 显隐联动调整发现页滚动内容底部预留高度，
    /// 使推荐艺人等底部区块能完整滚到悬浮条上方露出，避免被遮挡无法点选。
    /// </summary>
    public void SetBottomReservedHeight(double extra)
    {
        if (DiscoverContent == null) return;
        var basePad = 24.0;
        var target = Math.Max(basePad, extra);
        if (Math.Abs(DiscoverContent.Padding.Bottom - target) < 0.5) return;
        DiscoverContent.Padding = new Thickness(DiscoverContent.Padding.Left, DiscoverContent.Padding.Top, DiscoverContent.Padding.Right, target);
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
        if (sender is not Border border || border.BindingContext is not HeroCardItem heroItem) return;
        // AI 助手卡：点击整卡 = 进入聊天模式
        if (heroItem.Tag == AiCardTag)
        {
            _vm.EnterChatModeCommand.Execute(null);
            return;
        }
        // 插件快捷入口卡：执行权交给插件（直接播放等）
        if (_quickEntryMap.TryGetValue(heroItem, out var quickEntry))
        {
            quickEntry.Plugin.ExecuteQuickEntry(quickEntry.Entry.Id, Services);
            return;
        }
        if (heroItem.Song != null)
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
        if (sender is not ImageButton btn || btn.BindingContext is not HeroCardItem heroItem) return;
        // AI 助手卡：点击播放按钮 = 进入聊天模式（与横屏 AI 卡行为一致）
        if (heroItem.Tag == AiCardTag)
        {
            _vm.EnterChatModeCommand.Execute(null);
            return;
        }
        // 插件快捷入口卡：执行权交给插件（直接播放等）
        if (_quickEntryMap.TryGetValue(heroItem, out var quickEntry))
        {
            quickEntry.Plugin.ExecuteQuickEntry(quickEntry.Entry.Id, Services);
            return;
        }
        if (heroItem.Song != null)
            await PlaySongAsync(heroItem.Song, _vm.DailyRecommendSongs.ToList());
    }

    private void OnShuffleDailyClicked(object? sender, EventArgs e) => _vm.ShuffleDailyCommand.Execute(null);

    /// <summary>手势仲裁挂载兜底：Handler/PlatformView 未就绪时延迟重试一次</summary>
    private static void TryAttachPagerGuard(object? sender)
    {
        if (sender is not CollectionView cv) return;
#if ANDROID
        if (cv.Handler?.PlatformView != null)
        {
            CatClawMusic.Maui.Platforms.Android.HorizontalSwipeHelper.Attach(cv);
            return;
        }
        // Handler 尚未就绪：延迟一帧重试（Loaded 时 Handler 通常已就绪）
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (cv.Handler?.PlatformView != null)
                CatClawMusic.Maui.Platforms.Android.HorizontalSwipeHelper.Attach(cv);
        });
#endif
    }

    // === AI 助手卡（Hero 网格第一张，对齐横屏布局） ===

    /// <summary>AI 助手卡标记（HeroDisplayItems 首项，点击进入聊天模式）</summary>
    private const string AiCardTag = "AI 助手";

    /// <summary>Hero 并排网格数据源：AI 助手卡 + 插件快捷入口卡 + 英雄卡（AI 卡固定第一张）</summary>
    private readonly List<HeroCardItem> _heroDisplayItems = new();

    /// <summary>快捷入口卡 → (插件, 条目) 映射（路由用对象引用作键，Tag 仅作徽章显示文本）</summary>
    private readonly Dictionary<HeroCardItem, (IQuickEntryPlugin Plugin, QuickEntryInfo Entry)> _quickEntryMap = new();

    /// <summary>重建 Hero 网格数据源：AI 助手卡 + 插件快捷入口卡 + 当前 HeroCards（数据变化时调用）</summary>
    private void RebuildHeroDisplay()
    {
        _heroDisplayItems.Clear();
        _quickEntryMap.Clear();
        _heroDisplayItems.Add(new HeroCardItem
        {
            Tag = AiCardTag,
            Title = "🐾 和 Yuki 聊聊",
            Description = "找歌、推荐、聊天都可以",
            GradientStart = Color.FromArgb("#667eea"),
            GradientEnd = Color.FromArgb("#764ba2"),
            PlayIcon = ImageSource.FromFile("ic_play_dark")
        });

        // 插件快捷入口卡（通用机制，排序规则同桌面：SortOrder 升序，并列按插件注册顺序）
        if (Services.GetService(typeof(IPluginManager)) is IPluginManager pluginManager)
        {
            var quickEntries = new List<(int PluginOrder, IQuickEntryPlugin Plugin, QuickEntryInfo Entry)>();
            var plugins = pluginManager.GetEnabledPlugins<IQuickEntryPlugin>();
            for (int pi = 0; pi < plugins.Count; pi++)
            {
                foreach (var entry in plugins[pi].QuickEntries)
                    quickEntries.Add((pi, plugins[pi], entry));
            }
            foreach (var (_, plugin, entry) in quickEntries
                .OrderBy(q => q.Entry.SortOrder).ThenBy(q => q.PluginOrder))
            {
                var item = new HeroCardItem
                {
                    Tag = string.IsNullOrEmpty(entry.Icon) ? entry.Title : entry.Icon,
                    Title = entry.Title,
                    Description = entry.Subtitle,
                    GradientStart = Color.FromArgb(entry.Color1),
                    GradientEnd = Color.FromArgb(entry.Color2),
                    PlayIcon = ImageSource.FromFile("ic_play_dark"),
                };
                _quickEntryMap[item] = (plugin, entry);
                _heroDisplayItems.Add(item);
            }
        }

        foreach (var h in _vm.HeroCards)
        {
            if (h != null) _heroDisplayItems.Add(h);
        }
        // 每次赋新 List 实例：CollectionView 对同一实例的 Clear/Add 不感知，
        // 否则 HeroCards 后到（先只渲染 AI 卡）时网格不会刷新
        HeroGrid.ItemsSource = _heroDisplayItems.ToList();
    }

    // === 左右箭头导航（对齐横屏布局：每日推荐 / 推荐艺人） ===

    /// <summary>每日推荐横滑：滚回开头</summary>
    private void OnDailyPrevClicked(object? sender, EventArgs e)
    {
        if (DailyList.ItemsSource is not System.Collections.IList list || list.Count == 0) return;
        DailyList.ScrollTo(0, -1, ScrollToPosition.Start, true);
    }

    /// <summary>每日推荐横滑：滚到尾部</summary>
    private void OnDailyNextClicked(object? sender, EventArgs e)
    {
        if (DailyList.ItemsSource is not System.Collections.IList list || list.Count == 0) return;
        DailyList.ScrollTo(Math.Max(0, list.Count - 1), -1, ScrollToPosition.End, true);
    }

    /// <summary>推荐艺人横滑：滚回开头</summary>
    private void OnArtistPrevClicked(object? sender, EventArgs e)
    {
        if (ArtistsList.ItemsSource is not System.Collections.IList list || list.Count == 0) return;
        ArtistsList.ScrollTo(0, -1, ScrollToPosition.Start, true);
    }

    /// <summary>推荐艺人横滑：滚到尾部</summary>
    private void OnArtistNextClicked(object? sender, EventArgs e)
    {
        if (ArtistsList.ItemsSource is not System.Collections.IList list || list.Count == 0) return;
        ArtistsList.ScrollTo(Math.Max(0, list.Count - 1), -1, ScrollToPosition.End, true);
    }

    /// <summary>点击 AI 歌单卡片：播放歌单全部歌曲</summary>
    private async void OnAiPlaylistTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not AiPlaylist playlist) return;
        if (playlist.Songs.Count == 0) return;
        await PlaySongAsync(playlist.Songs[0], playlist.Songs);
    }

    /// <summary>手动重新生成 AI 歌单（清缓存后强制调用 AI）</summary>
    private async void OnAiPlaylistRegenerateTapped(object? sender, TappedEventArgs e)
    {
        await _vm.RegenerateAiPlaylistsAsync();
    }

    private void OnRefreshClicked(object? sender, EventArgs e)
    {
        // 刷新中重复点击忽略（按钮已切换为转圈反馈）；不再用 IsLoading 拦截——
        // 加载中点击也要有反馈（转圈），否则"点了没反应"
        if (_vm.IsRefreshing) return;
        _ = _vm.RefreshCommand.ExecuteAsync(null);
    }

    // === 聊天对话框推理力度（上拉菜单：点击"推理"按钮弹出底部面板选择） ===

    private void OnReasoningEffortButtonTapped(object? sender, TappedEventArgs e)
    {
        UpdateReasoningEffortSheet();
        EffortSheetOverlay.IsVisible = true;
        // 从底部滑入（上拉菜单动效）：面板先下移一屏，再动画回到原位
        EffortSheet.TranslationY = EffortSheet.Height > 0 ? EffortSheet.Height : 500;
        _ = EffortSheet.TranslateTo(0, 0, 220, Easing.CubicOut);
    }

    private void OnEffortSheetBackdropTapped(object? sender, TappedEventArgs e)
    {
        _ = CloseEffortSheetAsync();
    }

    private async Task CloseEffortSheetAsync()
    {
        if (!EffortSheetOverlay.IsVisible) return;
        await EffortSheet.TranslateTo(0, EffortSheet.Height, 160, Easing.CubicIn);
        EffortSheetOverlay.IsVisible = false;
    }

    private void OnReasoningEffortOptionTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is string effort)
        {
            CatClawMusic.Core.Services.AI.AgentService.SetReasoningEffort(effort);
            _ = CloseEffortSheetAsync();
            UpdateReasoningEffortChips();
        }
    }

    /// <summary>刷新推理按钮文字与上拉菜单选中标记</summary>
    private void UpdateReasoningEffortChips()
    {
        var current = CatClawMusic.Core.Services.AI.AgentService.GetReasoningEffort();
        EffortButtonLabel.Text = $"推理：{EffortDisplayName(current)}";
        UpdateReasoningEffortSheet();
    }

    private static string EffortDisplayName(string effort) => effort switch
    {
        "auto" => "自动",
        "disabled" => "关闭",
        "low" => "低",
        "high" => "高",
        "max" => "最强",
        _ => effort
    };

    private void UpdateReasoningEffortSheet()
    {
        if (EffortOptDisabled == null) return; // Loaded 前
        var current = CatClawMusic.Core.Services.AI.AgentService.GetReasoningEffort();
        EffortOptDisabledCheck.IsVisible = current == "disabled";
        EffortOptLowCheck.IsVisible = current == "low";
        EffortOptHighCheck.IsVisible = current == "high";
        EffortOptMaxCheck.IsVisible = current == "max";
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
