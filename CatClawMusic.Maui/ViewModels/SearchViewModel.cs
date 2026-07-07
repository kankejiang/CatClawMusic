using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 探索/搜索页 ViewModel：加载每日推荐、艺术家、专辑、最多播放、最新音乐等内容，
/// 提供搜索过滤、Tab 切换、AI 聊天模式入口及消息收发等能力。
/// </summary>
public partial class SearchViewModel : ObservableObject
{
    private readonly ExploreDataService _exploreDataService;
    private readonly IAgentService _agentService;

    private List<Song> _allDailyRecommendSongs = [];
    private List<SearchArtistItem> _allArtists = [];
    private List<SearchAlbumItem> _allAlbums = [];
    private List<Song> _allTopPlayedSongs = [];
    private List<Song> _allRecentAddedSongs = [];

    /// <summary>每日推荐歌曲集合（已应用筛选）</summary>
    [ObservableProperty]
    private ObservableCollection<Song> _dailyRecommendSongs = new();

    /// <summary>搜索关键字</summary>
    [ObservableProperty]
    private string _searchQuery = "";

    /// <summary>是否正在加载数据</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>当前 Tab 索引（0=每日推荐, 1=艺术家, 2=专辑, 3=最多播放, 4=最新音乐）</summary>
    [ObservableProperty]
    private int _currentTabIndex;

    /// <summary>当前分区标题</summary>
    [ObservableProperty]
    private string _sectionTitle = "每日推荐";

    /// <summary>艺术家集合（已应用筛选）</summary>
    [ObservableProperty]
    private ObservableCollection<SearchArtistItem> _artists = new();

    /// <summary>专辑集合（已应用筛选）</summary>
    [ObservableProperty]
    private ObservableCollection<SearchAlbumItem> _albums = new();

    /// <summary>最多播放歌曲集合（已应用筛选）</summary>
    [ObservableProperty]
    private ObservableCollection<Song> _topPlayedSongs = new();

    /// <summary>最新添加歌曲集合（已应用筛选）</summary>
    [ObservableProperty]
    private ObservableCollection<Song> _recentAddedSongs = new();

    /// <summary>当前 Agent 名称</summary>
    [ObservableProperty]
    private string _agentName = BuiltinAgent.Yuki.Name;

    /// <summary>聊天消息集合</summary>
    [ObservableProperty]
    private ObservableCollection<ChatMessage> _chatMessages = new();

    /// <summary>聊天输入框文本</summary>
    [ObservableProperty]
    private string _chatInput = "";

    /// <summary>是否处于聊天模式</summary>
    [ObservableProperty]
    private bool _isChatMode;

    /// <summary>空状态提示文本</summary>
    [ObservableProperty]
    private string _emptyStateText = "这里还没有内容";

    /// <summary>当前 Tab 是否为空</summary>
    [ObservableProperty]
    private bool _isCurrentTabEmpty;

    // Featured hero card
    /// <summary>是否存在英雄卡片展示的歌曲</summary>
    [ObservableProperty]
    private bool _hasFeaturedSong;

    /// <summary>英雄卡片歌曲标题</summary>
    [ObservableProperty]
    private string _featuredSongTitle = "";

    /// <summary>英雄卡片歌曲艺术家</summary>
    [ObservableProperty]
    private string _featuredSongArtist = "";

    /// <summary>英雄卡片歌曲封面</summary>
    [ObservableProperty]
    private ImageSource? _featuredSongCover;

    private Song? _featuredSong;

    /// <summary>英雄卡片对应的歌曲</summary>
    public Song? FeaturedSong => _featuredSong;

    // Search dropdown
    /// <summary>搜索下拉歌曲结果</summary>
    [ObservableProperty]
    private ObservableCollection<Song> _searchResults = new();

    /// <summary>搜索下拉艺术家结果</summary>
    [ObservableProperty]
    private ObservableCollection<SearchArtistItem> _searchArtistResults = new();

    /// <summary>搜索下拉专辑结果</summary>
    [ObservableProperty]
    private ObservableCollection<SearchAlbumItem> _searchAlbumResults = new();

    /// <summary>是否显示搜索下拉结果</summary>
    [ObservableProperty]
    private bool _showSearchResults;

    /// <summary>发现页 CollectionView 的占位数据源（内容全部放在 Header 中，使用 CollectionView 获得更好的手势处理）</summary>
    public ObservableCollection<int> DiscoverPageItems { get; } = new() { 0 };

    /// <summary>切换 Tab 命令（参数为 Tab 索引）</summary>
    public IRelayCommand<int> SwitchTabCommand { get; }
    /// <summary>加载探索数据命令</summary>
    public IAsyncRelayCommand LoadDataCommand { get; }
    /// <summary>加载探索数据命令（与 LoadDataCommand 等价）</summary>
    public IAsyncRelayCommand LoadExploreDataCommand { get; }
    /// <summary>进入聊天模式命令</summary>
    public IRelayCommand EnterChatModeCommand { get; }
    /// <summary>退出聊天模式命令</summary>
    public IRelayCommand ExitChatModeCommand { get; }
    /// <summary>发送聊天消息命令</summary>
    public IAsyncRelayCommand SendMessageCommand { get; }

    /// <summary>请求进入聊天模式时触发，供页面订阅</summary>
    public event EventHandler? EnterChatModeRequested;
    /// <summary>请求退出聊天模式时触发，供页面订阅</summary>
    public event EventHandler? ExitChatModeRequested;

    /// <summary>
    /// 初始化 <see cref="SearchViewModel"/> 实例，创建各交互命令并触发首次数据加载。
    /// </summary>
    /// <param name="exploreDataService">探索页数据服务</param>
    /// <param name="agentService">Agent 服务，用于 AI 聊天</param>
    public SearchViewModel(ExploreDataService exploreDataService, IAgentService agentService)
    {
        _exploreDataService = exploreDataService;
        _agentService = agentService;
        AgentName = _agentService.GetCurrentAgent().Name;

        SwitchTabCommand = new RelayCommand<int>(SwitchTab);
        LoadDataCommand = new AsyncRelayCommand(LoadDataAsync);
        LoadExploreDataCommand = new AsyncRelayCommand(LoadExploreDataAsync);
        SendMessageCommand = new AsyncRelayCommand(SendMessageAsync);
        EnterChatModeCommand = new RelayCommand(EnterChatMode);
        ExitChatModeCommand = new RelayCommand(ExitChatMode);

        _ = LoadDataAsync();
    }

    private void SwitchTab(int index)
    {
        CurrentTabIndex = index;
        SectionTitle = index switch
        {
            0 => "每日推荐",
            1 => "艺术家",
            2 => "专辑",
            3 => "最多播放",
            4 => "最新音乐",
            _ => "每日推荐"
        };
        RefreshEmptyState();
    }

    private void EnterChatMode()
    {
        IsChatMode = true;
        if (ChatMessages.Count == 0)
        {
            ChatMessages.Add(new ChatMessage
            {
                Role = "assistant",
                Content = _agentService.IsConfigured
                    ? "Yuki 在这里喵，可以帮你找歌、放歌、建歌单。"
                    : "Yuki 在这里喵，不过 AI 还没配置，先去设置页完成配置吧。"
            });
        }
        EnterChatModeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ExitChatMode()
    {
        IsChatMode = false;
        ExitChatModeRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 异步加载探索数据：每日推荐、艺术家、专辑、最多播放、最新音乐。
    /// 同日重复加载时跳过每日推荐生成，仅刷新随播放变化的列表。
    /// </summary>
    public async Task LoadDataAsync()
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var savedDate = Preferences.Default.Get("explore_last_load_date", "");
        var isSameDay = savedDate == today;

        // 同一天且已有数据：只跳过每日推荐生成，但仍刷新常听/最近添加（这些数据会随播放变化）
        if (isSameDay && _allDailyRecommendSongs.Count > 0)
        {
            try
            {
                IsLoading = true;
                var topPlayedTask = _exploreDataService.GetTopPlayedSongsAsync(20);
                var recentTask = _exploreDataService.GetRecentlyAddedSongsAsync(20);
                await Task.WhenAll(topPlayedTask, recentTask);

                _allTopPlayedSongs = topPlayedTask.Result;
                _allRecentAddedSongs = recentTask.Result;

                var artistsTask = _exploreDataService.GetArtistsWithSongCountAsync();
                var albumsTask = _exploreDataService.GetAlbumsWithSongCountAsync();
                await Task.WhenAll(artistsTask, albumsTask);

                _allArtists = artistsTask.Result.Select(a => new SearchArtistItem { Id = a.Id, Name = a.Name, Subtitle = $"{a.SongCount} 首歌曲", CoverSource = FirstNonEmpty(a.SampleCoverPath, a.Cover) }).ToList();
                _allAlbums = albumsTask.Result.Select(a => new SearchAlbumItem { Id = a.Id, Title = a.Title, ArtistName = a.ArtistName, Subtitle = $"{a.SongCount} 首歌曲", CoverSource = FirstNonEmpty(a.SampleCoverPath, a.CoverArtPath, a.Cover) }).ToList();

                // 批量解析新加载歌曲的封面
                var newSongs = _allTopPlayedSongs.Concat(_allRecentAddedSongs).ToList();
                if (newSongs.Count > 0)
                    await Task.Run(() => Services.CoverHelper.BatchResolveCovers(newSongs));

                ApplyFilters();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SearchVM] LoadDataAsync(refresh) failed: {ex.Message}");
            }
            finally { IsLoading = false; }
            return;
        }


        try
        {
            IsLoading = true;

            var dailyTask = _exploreDataService.GetDailyRecommendAsync();
            var artistsTask = _exploreDataService.GetArtistsWithSongCountAsync();
            var albumsTask = _exploreDataService.GetAlbumsWithSongCountAsync();
            var topPlayedTask = _exploreDataService.GetTopPlayedSongsAsync(20);
            var recentTask = _exploreDataService.GetRecentlyAddedSongsAsync(20);

            await Task.WhenAll(dailyTask, artistsTask, albumsTask, topPlayedTask, recentTask);

            _allDailyRecommendSongs = dailyTask.Result;
            _allArtists = artistsTask.Result
                .Select(a => new SearchArtistItem
                {
                    Id = a.Id,
                    Name = a.Name,
                    Subtitle = $"{a.SongCount} 首歌曲",
                    CoverSource = FirstNonEmpty(a.SampleCoverPath, a.Cover)
                })
                .ToList();
            _allAlbums = albumsTask.Result
                .Select(a => new SearchAlbumItem
                {
                    Id = a.Id,
                    Title = a.Title,
                    ArtistName = a.ArtistName,
                    Subtitle = $"{a.SongCount} 首歌曲",
                    CoverSource = FirstNonEmpty(a.SampleCoverPath, a.CoverArtPath, a.Cover)
                })
                .ToList();
            _allTopPlayedSongs = topPlayedTask.Result;
            _allRecentAddedSongs = recentTask.Result;

            // 批量解析所有歌曲的封面
            var allSongs = _allDailyRecommendSongs
                .Concat(_allTopPlayedSongs)
                .Concat(_allRecentAddedSongs)
                .ToList();
            await Task.Run(() => Services.CoverHelper.BatchResolveCovers(allSongs));

            // 为专辑卡片补充封面（从专辑内歌曲封面获取）
            foreach (var album in _allAlbums)
            {
                if (!string.IsNullOrEmpty(album.CoverSource)) continue;
                var sampleSong = allSongs.FirstOrDefault(s =>
                    s.Album?.Equals(album.Title, StringComparison.OrdinalIgnoreCase) == true);
                if (sampleSong != null)
                    album.CoverSource = sampleSong.CoverArtPath;
            }

            // 设置今日推荐英雄卡片（取每日推荐的第一首歌）
            if (_allDailyRecommendSongs.Count > 0)
            {
                var featured = _allDailyRecommendSongs[0];
                _featuredSong = featured;
                HasFeaturedSong = true;
                FeaturedSongTitle = featured.Title ?? "";
                FeaturedSongArtist = featured.Artist ?? "";
                if (!string.IsNullOrEmpty(featured.CoverArtPath))
                    FeaturedSongCover = ImageSource.FromFile(featured.CoverArtPath);
            }
            else
            {
                HasFeaturedSong = false;
            }

            // 保存日期到 Preferences（跨重启持久化）
            Preferences.Default.Set("explore_last_load_date", today);

            ApplyFilters();
        }
        catch (Exception ex)
        {
            EmptyStateText = $"加载失败：{ex.Message}";
            IsCurrentTabEmpty = true;
            System.Diagnostics.Debug.WriteLine($"[SearchViewModel] 加载探索数据失败: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>加载探索数据（与 <see cref="LoadDataAsync"/> 等价）</summary>
    public async Task LoadExploreDataAsync()
    {
        await LoadDataAsync();
    }

    /// <summary>获取指定 Tab 下的歌曲列表（用于列表页播放交互）</summary>
    /// <param name="tabIndex">Tab 索引</param>
    /// <returns>该 Tab 下的歌曲只读列表</returns>
    public IReadOnlyList<Song> GetSongsForTab(int tabIndex)
    {
        return tabIndex switch
        {
            0 => DailyRecommendSongs.ToList(),
            3 => TopPlayedSongs.ToList(),
            4 => RecentAddedSongs.ToList(),
            _ => []
        };
    }

    /// <summary>搜索防抖令牌，避免每次按键都触发过滤</summary>
    private CancellationTokenSource? _searchDebounceCts;

    partial void OnSearchQueryChanged(string value)
    {
        // 防抖 250ms，避免连续按键重复过滤
        _searchDebounceCts?.Cancel();
        _searchDebounceCts = new CancellationTokenSource();
        _ = UpdateSearchDropdownAsync(value, _searchDebounceCts.Token);
    }

    private async Task UpdateSearchDropdownAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            ShowSearchResults = false;
            SearchResults.Clear();
            SearchArtistResults.Clear();
            SearchAlbumResults.Clear();
            return;
        }

        try
        {
            // 等待防抖窗口（250ms 内若再次按键则取消此次）
            await Task.Delay(250, ct).ConfigureAwait(false);

            var q = query.Trim();

            // 将 LINQ 过滤放到线程池线程执行
            var (songs, artists, albums) = await Task.Run(() =>
            {
                var songs = _allDailyRecommendSongs
                    .Concat(_allTopPlayedSongs)
                    .Concat(_allRecentAddedSongs)
                    .Where(s =>
                        (s.Title?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (s.Artist?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (s.Album?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
                    .GroupBy(s => s.Id)
                    .Select(g => g.First())
                    .Take(10)
                    .ToList();

                var artists = _allArtists
                    .Where(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                    .Take(6)
                    .ToList();

                var albums = _allAlbums
                    .Where(a =>
                        a.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        a.ArtistName.Contains(q, StringComparison.OrdinalIgnoreCase))
                    .Take(6)
                    .ToList();

                return (songs, artists, albums);
            }, ct).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            // 回到主线程更新 ObservableCollection
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (ct.IsCancellationRequested) return;
                SearchResults = new ObservableCollection<Song>(songs);
                SearchArtistResults = new ObservableCollection<SearchArtistItem>(artists);
                SearchAlbumResults = new ObservableCollection<SearchAlbumItem>(albums);
                ShowSearchResults = songs.Count > 0 || artists.Count > 0 || albums.Count > 0;
            });
        }
        catch (OperationCanceledException)
        {
            // 防抖正常行为，忽略
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Search] UpdateSearchDropdown failed: {ex.Message}");
        }
    }

    /// <summary>清空搜索下拉结果</summary>
    public void ClearSearchDropdown()
    {
        ShowSearchResults = false;
        SearchResults.Clear();
        SearchArtistResults.Clear();
        SearchAlbumResults.Clear();
    }

    /// <summary>发送聊天消息：将用户输入发送给 Agent 并追加回复</summary>
    public async Task SendMessageAsync()
    {
        var userMessage = ChatInput?.Trim();
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return;
        }

        ChatInput = "";
        ChatMessages.Add(new ChatMessage
        {
            Role = "user",
            Content = userMessage
        });

        if (!_agentService.IsConfigured)
        {
            ChatMessages.Add(new ChatMessage
            {
                Role = "assistant",
                Content = "AI 还没有配置好喵，先到“设置 > 探索设置”里填一下模型信息吧。"
            });
            return;
        }

        try
        {
            var response = await _agentService.SendMessageAsync(userMessage);
            ChatMessages.Add(new ChatMessage
            {
                Role = "assistant",
                Content = BuildAssistantMessage(response),
                Songs = response.Songs
            });
        }
        catch (Exception ex)
        {
            ChatMessages.Add(new ChatMessage
            {
                Role = "assistant",
                Content = $"出错了喵：{ex.Message}"
            });
        }
    }

    private void ApplyFilters()
    {
        var query = SearchQuery?.Trim();
        var hasQuery = !string.IsNullOrWhiteSpace(query);

        DailyRecommendSongs = new ObservableCollection<Song>(
            FilterSongs(_allDailyRecommendSongs, query));
        Artists = new ObservableCollection<SearchArtistItem>(
            hasQuery
                ? _allArtists.Where(a =>
                    a.Name.Contains(query!, StringComparison.OrdinalIgnoreCase) ||
                    a.Subtitle.Contains(query!, StringComparison.OrdinalIgnoreCase))
                : _allArtists);
        Albums = new ObservableCollection<SearchAlbumItem>(
            hasQuery
                ? _allAlbums.Where(a =>
                    a.Title.Contains(query!, StringComparison.OrdinalIgnoreCase) ||
                    a.ArtistName.Contains(query!, StringComparison.OrdinalIgnoreCase) ||
                    a.Subtitle.Contains(query!, StringComparison.OrdinalIgnoreCase))
                : _allAlbums);
        TopPlayedSongs = new ObservableCollection<Song>(
            FilterSongs(_allTopPlayedSongs, query));
        RecentAddedSongs = new ObservableCollection<Song>(
            FilterSongs(_allRecentAddedSongs, query));

        RefreshEmptyState();
    }

    private IEnumerable<Song> FilterSongs(IEnumerable<Song> songs, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return songs;
        }

        return songs.Where(song =>
            (song.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (song.Artist?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (song.AllArtists?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (song.Album?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    private void RefreshEmptyState()
    {
        var count = DailyRecommendSongs.Count
            + Artists.Count
            + Albums.Count
            + TopPlayedSongs.Count
            + RecentAddedSongs.Count;

        IsCurrentTabEmpty = !IsLoading && count == 0;
        if (!IsCurrentTabEmpty)
        {
            EmptyStateText = string.Empty;
            return;
        }

        EmptyStateText = string.IsNullOrWhiteSpace(SearchQuery)
            ? "先导入一些音乐或播放几首歌，这里就会出现推荐、艺人和专辑内容。"
            : "没有找到匹配的内容，试试换个关键词。";
    }

    private static string BuildAssistantMessage(ChatMessage response)
    {
        if (!string.IsNullOrWhiteSpace(response.Content))
        {
            return response.Content;
        }

        if (response.Songs?.Count > 0)
        {
            return $"帮你找到 {response.Songs.Count} 首相关歌曲喵。";
        }

        return "处理完成喵。";
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    }
}

/// <summary>搜索页艺术家展示项</summary>
public class SearchArtistItem
{
    /// <summary>艺术家 ID</summary>
    public int Id { get; set; }
    /// <summary>艺术家名称</summary>
    public string Name { get; set; } = "";
    /// <summary>副标题（如歌曲数量）</summary>
    public string Subtitle { get; set; } = "";
    /// <summary>封面来源路径</summary>
    public string? CoverSource { get; set; }
}

/// <summary>搜索页专辑展示项</summary>
public class SearchAlbumItem
{
    /// <summary>专辑 ID</summary>
    public int Id { get; set; }
    /// <summary>专辑标题</summary>
    public string Title { get; set; } = "";
    /// <summary>艺术家名称</summary>
    public string ArtistName { get; set; } = "";
    /// <summary>副标题（如歌曲数量）</summary>
    public string Subtitle { get; set; } = "";
    /// <summary>封面来源路径</summary>
    public string? CoverSource { get; set; }
}
