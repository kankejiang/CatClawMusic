using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CatClawMusic.Maui.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Text;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 探索/搜索页 ViewModel：加载每日推荐、艺术家、专辑、最多播放、最新音乐等内容，
/// 提供搜索过滤、Tab 切换、AI 聊天模式入口及消息收发等能力。
/// </summary>
public partial class SearchViewModel : ObservableObject
{
    private readonly ExploreDataService _exploreDataService;
    private readonly IAgentService _agentService;
    private readonly IMusicLibraryService _libraryService;
    private readonly ChatMemoryService _chatMemoryService;
    private readonly MusicDatabase _database;

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

    /// <summary>Agent 是否正在思考（等待 AI 响应或工具调用中）</summary>
    [ObservableProperty]
    private bool _isAgentThinking;

    /// <summary>思考过程面板是否展开（点击切换）</summary>
    [ObservableProperty]
    private bool _isThinkingExpanded;

    /// <summary>思考过程单行摘要（折叠时显示）</summary>
    [ObservableProperty]
    private string _thinkingSummary = "";

    /// <summary>思考过程步骤详情（展开时显示）</summary>
    [ObservableProperty]
    private ObservableCollection<string> _thinkingSteps = new();

    /// <summary>是否有思考步骤可展示</summary>
    public bool HasThinkingSteps => ThinkingSteps.Count > 0;

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

    /// <summary>搜索框非空但无任何匹配结果时为 true，用于展示"问问 Yuki"入口</summary>
    [ObservableProperty]
    private bool _hasNoSearchResults;

    /// <summary>当前分类索引（0=推荐, 1=排行榜, 2=歌手, 3=推荐专辑）</summary>
    [ObservableProperty]
    private int _currentCategory;

    /// <summary>问候语文本</summary>
    [ObservableProperty]
    private string _greetingText = "";

    /// <summary>英雄卡片集合</summary>
    [ObservableProperty]
    private ObservableCollection<HeroCardItem> _heroCards = new();

    /// <summary>收藏歌曲集合</summary>
    [ObservableProperty]
    private ObservableCollection<Song> _favoriteSongs = new();

    /// <summary>搜索框是否展开</summary>
    [ObservableProperty]
    private bool _isSearchOpen;

    /// <summary>是否启用 AI 智能推荐 Hero 卡</summary>
    [ObservableProperty]
    private bool _isAiRecommendationEnabled;

    /// <summary>AI 推荐的歌曲</summary>
    private Song? _aiRecommendedSong;

    /// <summary>AI 推荐理由文字</summary>
    [ObservableProperty]
    private string _aiRecommendReason = "AI 根据你的听歌口味为你精选";

    /// <summary>AI 是否正在生成推荐</summary>
    [ObservableProperty]
    private bool _isAiRecommending;

    /// <summary>当天 AI 推荐批次（歌曲 ID + 理由），每天仅向 AI 获取一次并整批缓存</summary>
    private List<AiRecItem> _aiRecommendBatch = new();
    /// <summary>AI 推荐批次对应的日期（"yyyy-MM-dd"），用于判定是否需要重新获取</summary>
    private string? _aiRecommendBatchDate;
    /// <summary>当天是否已尝试向 AI 请求（无论成功失败），避免失败后在同一天反复调用浪费 token</summary>
    private string? _aiAttemptDate;
    /// <summary>是否正在向 AI 请求推荐批次，防止并发重复请求</summary>
    private bool _aiFetchInProgress;
    /// <summary>Hero 卡当前展示的 AI 推荐索引，换批时轮换（仅读缓存，不消耗 token）</summary>
    private int _aiHeroIndex;
    /// <summary>AI 每日推荐磁盘缓存文件路径（复用探索缓存目录）</summary>
    private readonly string _aiCacheFilePath = Path.Combine(FileSystem.AppDataDirectory, "cache", "ai_recommend.json");

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
    /// <summary>刷新命令</summary>
    public IAsyncRelayCommand RefreshCommand { get; }
    /// <summary>随机每日推荐命令</summary>
    public IRelayCommand ShuffleDailyCommand { get; }
    /// <summary>切换思考面板展开/折叠</summary>
    public IRelayCommand ToggleThinkingCommand { get; }

    /// <summary>请求进入聊天模式时触发，供页面订阅</summary>
    public event EventHandler? EnterChatModeRequested;
    /// <summary>请求退出聊天模式时触发，供页面订阅</summary>
    public event EventHandler? ExitChatModeRequested;

    /// <summary>
    /// 初始化 <see cref="SearchViewModel"/> 实例，创建各交互命令并触发首次数据加载。
    /// </summary>
    /// <param name="exploreDataService">探索页数据服务</param>
    /// <param name="agentService">Agent 服务，用于 AI 聊天</param>
    /// <param name="libraryService">音乐库服务</param>
    /// <param name="chatMemoryService">聊天记忆服务</param>
    public SearchViewModel(ExploreDataService exploreDataService, IAgentService agentService, IMusicLibraryService libraryService, ChatMemoryService chatMemoryService, MusicDatabase database)
    {
        _exploreDataService = exploreDataService;
        _agentService = agentService;
        _libraryService = libraryService;
        _chatMemoryService = chatMemoryService;
        _database = database;
        AgentName = _agentService.GetCurrentAgent().Name;

        SwitchTabCommand = new RelayCommand<int>(SwitchTab);
        LoadDataCommand = new AsyncRelayCommand(LoadDataAsync);
        LoadExploreDataCommand = new AsyncRelayCommand(LoadExploreDataAsync);
        SendMessageCommand = new AsyncRelayCommand(SendMessageAsync);
        EnterChatModeCommand = new RelayCommand(EnterChatMode);
        ExitChatModeCommand = new RelayCommand(ExitChatMode);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ShuffleDailyCommand = new RelayCommand(ShuffleDaily);
        ToggleThinkingCommand = new RelayCommand(() => IsThinkingExpanded = !IsThinkingExpanded);

        // 读取 AI 推荐开关持久化状态
        IsAiRecommendationEnabled = Preferences.Default.Get("ai_recommendation_enabled", false);

        GreetingText = CalculateGreeting();

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

    /// <summary>最早已加载消息的数据库Id（用于向上翻页加载更多），0表示未加载</summary>
    private int _oldestLoadedMessageId;
    /// <summary>是否还有更多历史记录可加载</summary>
    [ObservableProperty]
    private bool _hasMoreChatHistory;
    /// <summary>是否正在加载更多历史记录（防止重复触发）</summary>
    private bool _isLoadingMoreHistory;

    /// <summary>进入聊天模式时加载最近20条历史记录</summary>
    private async Task LoadRecentChatHistoryAsync()
    {
        try
        {
            var records = await _database.GetRecentChatMessagesAsync(20);
            ChatMessages.Clear();
            if (records.Count == 0)
            {
                // 没有历史记录，添加欢迎消息
                ChatMessages.Add(new ChatMessage
                {
                    Role = "assistant",
                    Content = _agentService.IsConfigured
                        ? "Yuki 在这里喵，可以帮你找歌、放歌、建歌单。"
                        : "Yuki 在这里喵，不过 AI 还没配置，先去设置页完成配置吧。"
                });
                _oldestLoadedMessageId = 0;
                HasMoreChatHistory = false;
            }
            else
            {
                foreach (var r in records)
                {
                    ChatMessages.Add(new ChatMessage { Role = r.Role, Content = r.Content });
                }
                _oldestLoadedMessageId = records[0].Id;
                var total = await _database.GetChatMessageCountAsync();
                HasMoreChatHistory = total > ChatMessages.Count;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SearchVM] LoadChatHistory failed: {ex.Message}");
        }
    }

    /// <summary>向上翻页时加载更多历史记录，插入到列表头部</summary>
    public async Task LoadMoreChatHistoryAsync()
    {
        if (_isLoadingMoreHistory || !HasMoreChatHistory || _oldestLoadedMessageId <= 0)
            return;

        _isLoadingMoreHistory = true;
        try
        {
            var older = await _database.GetRecentChatMessagesAsync(20, _oldestLoadedMessageId);
            if (older.Count > 0)
            {
                // 插入到列表头部
                for (int i = 0; i < older.Count; i++)
                {
                    ChatMessages.Insert(i, new ChatMessage { Role = older[i].Role, Content = older[i].Content });
                }
                _oldestLoadedMessageId = older[0].Id;
                var total = await _database.GetChatMessageCountAsync();
                HasMoreChatHistory = total > ChatMessages.Count;
            }
            else
            {
                HasMoreChatHistory = false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SearchVM] LoadMoreChatHistory failed: {ex.Message}");
        }
        finally
        {
            _isLoadingMoreHistory = false;
        }
    }

    private void EnterChatMode()
    {
        IsChatMode = true;
        // 异步加载历史记录
        _ = LoadRecentChatHistoryAsync();
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

                var artistsResult = artistsTask.Result;
                var albumsResult = albumsTask.Result;

                // 批量解析新加载歌曲的封面
                var newSongs = _allTopPlayedSongs.Concat(_allRecentAddedSongs).ToList();
                if (newSongs.Count > 0)
                    await Task.Run(() => Services.CoverHelper.BatchResolveCovers(newSongs));

                // 为艺人/专辑解析采样封面（SampleCoverPath 在扫描后为空，需从音频文件提取）
                await Task.Run(() => ResolveSampleCovers(artistsResult, albumsResult, newSongs));

                _allArtists = artistsResult.Select(a => new SearchArtistItem { Id = a.Id, Name = a.Name, Subtitle = $"{a.SongCount} 首歌曲", CoverSource = PathToImageSource(FirstNonEmpty(a.SampleCoverPath, a.Cover)) }).ToList();
                _allAlbums = albumsResult.Select(a => new SearchAlbumItem { Id = a.Id, Title = a.Title, ArtistName = a.ArtistName, Subtitle = $"{a.SongCount} 首歌曲", CoverSource = PathToImageSource(FirstNonEmpty(a.SampleCoverPath, a.CoverArtPath, a.Cover)) }).ToList();

                ApplyFilters();
                await LoadFavoritesAndGenerateHeroCards();
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
            var artistsResult = artistsTask.Result;
            var albumsResult = albumsTask.Result;
            _allTopPlayedSongs = topPlayedTask.Result;
            _allRecentAddedSongs = recentTask.Result;

            // 批量解析所有歌曲的封面
            var allSongs = _allDailyRecommendSongs
                .Concat(_allTopPlayedSongs)
                .Concat(_allRecentAddedSongs)
                .ToList();
            await Task.Run(() => Services.CoverHelper.BatchResolveCovers(allSongs));

            // 为艺人/专辑解析采样封面（SampleCoverPath 在扫描后为空，需从音频文件提取）
            await Task.Run(() => ResolveSampleCovers(artistsResult, albumsResult, allSongs));

            _allArtists = artistsResult
                .Select(a => new SearchArtistItem
                {
                    Id = a.Id,
                    Name = a.Name,
                    Subtitle = $"{a.SongCount} 首歌曲",
                    CoverSource = PathToImageSource(FirstNonEmpty(a.SampleCoverPath, a.Cover))
                })
                .ToList();
            _allAlbums = albumsResult
                .Select(a => new SearchAlbumItem
                {
                    Id = a.Id,
                    Title = a.Title,
                    ArtistName = a.ArtistName,
                    Subtitle = $"{a.SongCount} 首歌曲",
                    CoverSource = PathToImageSource(FirstNonEmpty(a.SampleCoverPath, a.CoverArtPath, a.Cover))
                })
                .ToList();

            // 为专辑卡片补充封面（从专辑内歌曲封面获取，作为采样封面未命中时的回退）
            foreach (var album in _allAlbums)
            {
                if (album.CoverSource != null) continue;
                var sampleSong = allSongs.FirstOrDefault(s =>
                    s.Album?.Equals(album.Title, StringComparison.OrdinalIgnoreCase) == true);
                if (sampleSong != null && !string.IsNullOrWhiteSpace(sampleSong.CoverArtPath))
                    album.CoverSource = ImageSource.FromFile(sampleSong.CoverArtPath);
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
            await LoadFavoritesAndGenerateHeroCards();
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

    /// <summary>
    /// 扫描完成后重新加载探索数据：清除所有缓存并强制全量刷新。
    /// 在 LocalScanService.NeedsReload 标记为 true 时由页面 OnAppearing 调用。
    /// </summary>
    public async Task ReloadAfterScanAsync()
    {
        try
        {
            _exploreDataService.InvalidateDailyRecommendCache();
            InvalidateAiCache();
            Services.CoverHelper.ClearCache();
            Preferences.Default.Remove("explore_last_load_date");

            _allDailyRecommendSongs = [];
            _allTopPlayedSongs = [];
            _allArtists = [];
            _allAlbums = [];
            _allRecentAddedSongs = [];
            ApplyFilters();

            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SearchVM] ReloadAfterScan failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 为艺人/专辑解析采样歌曲的封面。
    /// ExploreDataService 返回的 SampleCoverPath 在扫描后为 null（DB 中 CoverArtPath 未写入），
    /// 此方法根据 SampleSongId 和 SampleFilePath 从音频文件提取嵌入封面并回填 SampleCoverPath。
    /// </summary>
    /// <param name="artists">艺人列表（会被修改 SampleCoverPath）</param>
    /// <param name="albums">专辑列表（会被修改 SampleCoverPath）</param>
    /// <param name="alreadyResolved">已解析封面的歌曲集合，用于跳过重复解析</param>
    private void ResolveSampleCovers(
        List<Data.ArtistWithCount> artists,
        List<Data.AlbumWithCount> albums,
        List<Song> alreadyResolved)
    {
        // 从已解析歌曲中建立 songId → CoverArtPath 映射，避免重复提取
        var resolvedMap = new Dictionary<int, string?>();
        foreach (var s in alreadyResolved)
        {
            if (s.Id > 0 && !resolvedMap.ContainsKey(s.Id))
                resolvedMap[s.Id] = s.CoverArtPath;
        }

        // 收集需要解析封面的采样歌曲（去重）
        var pending = new Dictionary<int, Song>();
        foreach (var a in artists)
        {
            if (a.SampleSongId > 0 && !string.IsNullOrEmpty(a.SampleFilePath)
                && !resolvedMap.ContainsKey(a.SampleSongId) && !pending.ContainsKey(a.SampleSongId))
            {
                pending[a.SampleSongId] = new Song { Id = a.SampleSongId, FilePath = a.SampleFilePath };
            }
        }
        foreach (var a in albums)
        {
            if (a.SampleSongId > 0 && !string.IsNullOrEmpty(a.SampleFilePath)
                && !resolvedMap.ContainsKey(a.SampleSongId) && !pending.ContainsKey(a.SampleSongId))
            {
                pending[a.SampleSongId] = new Song { Id = a.SampleSongId, FilePath = a.SampleFilePath };
            }
        }

        if (pending.Count > 0)
        {
            Services.CoverHelper.BatchResolveCovers(pending.Values);
            foreach (var kv in pending)
                resolvedMap[kv.Key] = kv.Value.CoverArtPath;
        }

        // 回填 SampleCoverPath
        foreach (var a in artists)
        {
            if (string.IsNullOrEmpty(a.SampleCoverPath)
                && resolvedMap.TryGetValue(a.SampleSongId, out var path)
                && !string.IsNullOrEmpty(path))
                a.SampleCoverPath = path;
        }
        foreach (var a in albums)
        {
            if (string.IsNullOrEmpty(a.SampleCoverPath)
                && resolvedMap.TryGetValue(a.SampleSongId, out var path)
                && !string.IsNullOrEmpty(path))
                a.SampleCoverPath = path;
        }
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
        _searchDebounceCts?.Dispose();
        _searchDebounceCts = new CancellationTokenSource();
        _ = UpdateSearchDropdownAsync(value, _searchDebounceCts.Token);
    }

    private async Task UpdateSearchDropdownAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            ShowSearchResults = false;
            HasNoSearchResults = false;
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
                var hasResults = songs.Count > 0 || artists.Count > 0 || albums.Count > 0;
                ShowSearchResults = hasResults;
                HasNoSearchResults = !hasResults;
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
        HasNoSearchResults = false;
        SearchResults.Clear();
        SearchArtistResults.Clear();
        SearchAlbumResults.Clear();
    }

    /// <summary>从搜索入口直接发送消息（自动进入聊天模式）</summary>
    /// <param name="message">要发送的消息</param>
    public async Task SendMessageFromSearchAsync(string message)
    {
        EnterChatMode();
        ChatInput = message;
        await SendMessageAsync();
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

        // 发送新消息时折叠思考面板，清空上一次的步骤
        IsThinkingExpanded = false;
        ThinkingSteps.Clear();
        ThinkingSummary = "";

        var userMsg = new ChatMessage
        {
            Role = "user",
            Content = userMessage
        };
        ChatMessages.Add(userMsg);
        _ = _database.SaveChatMessageAsync(new ChatMessageRecord { Role = "user", Content = userMessage, Timestamp = DateTime.UtcNow });
        _ = _chatMemoryService.AppendMessageAsync(userMsg);

        if (!_agentService.IsConfigured)
        {
            var notConfiguredMsg = new ChatMessage
            {
                Role = "assistant",
                Content = "AI 还没有配置好喵，先到“设置 > AI 设置”里填一下模型信息吧。"
            };
            ChatMessages.Add(notConfiguredMsg);
            _ = _database.SaveChatMessageAsync(new ChatMessageRecord { Role = "assistant", Content = notConfiguredMsg.Content, Timestamp = DateTime.UtcNow });
            _ = _chatMemoryService.AppendMessageAsync(notConfiguredMsg);
            return;
        }

        // 开始思考：显示思考面板
        IsAgentThinking = true;
        ThinkingSummary = "思考中...";

        try
        {
            var response = await _agentService.SendMessageAsync(userMessage, OnPartialMessage);
            var assistantMsg = new ChatMessage
            {
                Role = "assistant",
                Content = BuildAssistantMessage(response),
                Songs = response.Songs
            };
            ChatMessages.Add(assistantMsg);
            _ = _database.SaveChatMessageAsync(new ChatMessageRecord { Role = "assistant", Content = assistantMsg.Content, Timestamp = DateTime.UtcNow });
            _ = _chatMemoryService.AppendMessageAsync(assistantMsg);

            // 思考完成：更新摘要
            IsAgentThinking = false;
            if (ThinkingSteps.Count > 0)
                ThinkingSummary = $"完成 · {ThinkingSteps.Count} 个步骤";
            else
                ThinkingSummary = "";
        }
        catch (Exception ex)
        {
            IsAgentThinking = false;
            var errorMsg = new ChatMessage
            {
                Role = "assistant",
                Content = $"出错了喵：{ex.Message}"
            };
            ChatMessages.Add(errorMsg);
            _ = _database.SaveChatMessageAsync(new ChatMessageRecord { Role = "assistant", Content = errorMsg.Content, Timestamp = DateTime.UtcNow });
            _ = _chatMemoryService.AppendMessageAsync(errorMsg);
            ThinkingSummary = "";
        }
    }

    /// <summary>Agent 中间消息回调：处理工具调用过程展示</summary>
    private void OnPartialMessage(ChatMessage partial)
    {
        if (partial.Role == "assistant" && partial.ToolCalls != null && partial.ToolCalls.Count > 0)
        {
            var toolNames = string.Join(", ", partial.ToolCalls.Select(tc => tc.Function?.Name ?? "?"));
            var step = $"🔧 调用工具: {toolNames}";
            ThinkingSteps.Add(step);
            ThinkingSummary = step;
        }
        else if (partial.Role == "tool" && !string.IsNullOrEmpty(partial.Name))
        {
            var step = $"✅ {partial.Name} 完成";
            ThinkingSteps.Add(step);
            ThinkingSummary = step;
        }
        OnPropertyChanged(nameof(HasThinkingSteps));
    }

    /// <summary>根据当前 SearchQuery 重新过滤各分区集合（供 PC 端顶栏搜索调用）</summary>
    public void ApplyFilters()
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

    /// <summary>将文件路径转换为 ImageSource，路径为空时返回 null</summary>
    private static ImageSource? PathToImageSource(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? null : ImageSource.FromFile(path);
    }

    /// <summary>根据当前时间计算问候语</summary>
    private string CalculateGreeting()
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

    /// <summary>生成英雄卡片</summary>
    private void GenerateHeroCards()
    {
        var cards = new List<HeroCardItem>();
        var gradients = new (Color Start, Color End)[]
        {
            (Color.FromArgb("#667eea"), Color.FromArgb("#764ba2")),
            (Color.FromArgb("#f093fb"), Color.FromArgb("#f5576c")),
            (Color.FromArgb("#4facfe"), Color.FromArgb("#00f2fe")),
            (Color.FromArgb("#43e97b"), Color.FromArgb("#38f9d7")),
            (Color.FromArgb("#fa709a"), Color.FromArgb("#fee140"))
        };

        // AI 智能推荐卡（首位）
        if (IsAiRecommendationEnabled)
        {
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            if (_aiRecommendBatchDate == today && _aiRecommendBatch.Count > 0)
            {
                // 命中当天缓存：直接从批次中轮换取一首展示，不再调用 AI（零 token 消耗）
                AiRecItem? item = null;
                Song? aiSong = null;
                var start = _aiHeroIndex % _aiRecommendBatch.Count;
                for (int i = 0; i < _aiRecommendBatch.Count; i++)
                {
                    var cand = _aiRecommendBatch[(start + i) % _aiRecommendBatch.Count];
                    var s = ResolveSongById(cand.SongId);
                    if (s != null) { item = cand; aiSong = s; break; }
                }
                if (aiSong != null)
                {
                    _aiRecommendedSong = aiSong;
                    cards.Add(new HeroCardItem
                    {
                        Tag = "✨ AI 智能推荐",
                        Title = aiSong.Title ?? "未知歌曲",
                        Description = string.IsNullOrWhiteSpace(item?.Reason) ? AiRecommendReason : item!.Reason,
                        Song = aiSong,
                        GradientStart = gradients[4].Start,
                        GradientEnd = gradients[4].End
                    });
                }
            }
            else
            {
                // 当天尚无缓存：先用本地挑一首占位，并在后台向 AI 获取「当天全部推荐」（每天仅一次）
                var aiSong = PickAiRecommendedSong();
                _aiRecommendedSong = aiSong;
                if (aiSong != null)
                {
                    cards.Add(new HeroCardItem
                    {
                        Tag = "✨ AI 智能推荐",
                        Title = aiSong.Title ?? "未知歌曲",
                        Description = IsAiRecommending ? "AI 正在分析你的口味…" : AiRecommendReason,
                        Song = aiSong,
                        GradientStart = gradients[4].Start,
                        GradientEnd = gradients[4].End
                    });
                }

                if (_agentService.IsConfigured && !_aiFetchInProgress && _aiAttemptDate != today)
                {
                    _ = EnsureDailyAiRecommendationsAsync(regenerateAfter: true);
                }
            }
        }

        var tags = new[] { "每日推荐", "最多播放", "我的最爱", "随机播放" };

        if (_allDailyRecommendSongs.Count > 0)
        {
            var song = _allDailyRecommendSongs[0];
            cards.Add(new HeroCardItem
            {
                Tag = tags[0],
                Title = song.Title ?? "未知歌曲",
                Description = $"{song.Artist ?? "未知艺术家"} · {song.Album ?? "未知专辑"}",
                Song = song,
                GradientStart = gradients[0].Start,
                GradientEnd = gradients[0].End
            });
        }

        if (_allTopPlayedSongs.Count > 0)
        {
            var song = _allTopPlayedSongs[0];
            cards.Add(new HeroCardItem
            {
                Tag = tags[1],
                Title = song.Title ?? "未知歌曲",
                Description = $"{song.Artist ?? "未知艺术家"} · {song.Album ?? "未知专辑"}",
                Song = song,
                GradientStart = gradients[1].Start,
                GradientEnd = gradients[1].End
            });
        }

        if (FavoriteSongs.Count > 0)
        {
            var song = FavoriteSongs[0];
            cards.Add(new HeroCardItem
            {
                Tag = tags[2],
                Title = song.Title ?? "未知歌曲",
                Description = $"{song.Artist ?? "未知艺术家"} · {song.Album ?? "未知专辑"}",
                Song = song,
                GradientStart = gradients[2].Start,
                GradientEnd = gradients[2].End
            });
        }

        if (_allDailyRecommendSongs.Count > 0)
        {
            var random = new Random();
            var index = random.Next(_allDailyRecommendSongs.Count);
            var song = _allDailyRecommendSongs[index];
            cards.Add(new HeroCardItem
            {
                Tag = tags[3],
                Title = song.Title ?? "未知歌曲",
                Description = $"{song.Artist ?? "未知艺术家"} · {song.Album ?? "未知专辑"}",
                Song = song,
                GradientStart = gradients[3].Start,
                GradientEnd = gradients[3].End
            });
        }

        // 统一设播放图标（WinUI 上 XAML 字面量 Source="ic_xxx" 不渲染，必须代码赋 ImageSource）
        // 用深色图标：播放按钮背景是半透明白底 (#50FFFFFF)，浅色图标会看不见
        // 必须在赋值 HeroCards 之前设好——HeroCardItem 无属性变更通知，赋值后再改绑定不会刷新
        var playIcon = Helpers.ImageSourceHelper.FromNameOriginal("ic_play_dark");
        foreach (var c in cards) c.PlayIcon = playIcon;

        HeroCards = new ObservableCollection<HeroCardItem>(cards.Take(4));
    }

    /// <summary>
    /// 基于听歌数据智能挑选一首 AI 推荐歌曲。
    /// 策略：优先从「常听但非榜首」中随机选择，避免永远推荐同一首。
    /// </summary>
    private Song? PickAiRecommendedSong()
    {
        var candidates = new List<Song>();

        // 常听歌曲第2-10首（排除榜首，避免和"热门歌曲"重复）
        if (_allTopPlayedSongs.Count > 1)
            candidates.AddRange(_allTopPlayedSongs.Skip(1).Take(9));

        // 收藏中随机
        if (FavoriteSongs.Count > 0)
            candidates.AddRange(FavoriteSongs);

        // 每日推荐随机3首
        if (_allDailyRecommendSongs.Count > 0)
        {
            var random = new Random();
            candidates.AddRange(_allDailyRecommendSongs.OrderBy(_ => random.Next()).Take(3));
        }

        if (candidates.Count == 0)
        {
            // 回退：取每日推荐第一首
            return _allDailyRecommendSongs.FirstOrDefault();
        }

        // 去重并随机选一首
        var unique = candidates.GroupBy(s => s.Id).Select(g => g.First()).ToList();
        var rng = new Random();
        return unique[rng.Next(unique.Count)];
    }

    /// <summary>
    /// 确保当天的 AI 推荐批次已就绪：内存缓存 → 磁盘缓存 → 调用 AI（每天仅一次）。
    /// 命中缓存则不消耗 token；调用完成后可选地重新生成 Hero 卡以刷新展示。
    /// </summary>
    private async Task EnsureDailyAiRecommendationsAsync(bool regenerateAfter = false)
    {
        if (!IsAiRecommendationEnabled || !_agentService.IsConfigured) return;
        var today = DateTime.Today.ToString("yyyy-MM-dd");

        // 内存命中
        if (_aiRecommendBatchDate == today && _aiRecommendBatch.Count > 0) return;
        if (_aiFetchInProgress) return;

        _aiFetchInProgress = true;
        var calledAi = false;
        try
        {
            // 磁盘命中（跨重启复用当天结果）
            var disk = await LoadAiCacheFromDiskAsync(today);
            if (disk != null && disk.Count > 0)
            {
                _aiRecommendBatch = disk;
                _aiRecommendBatchDate = today;
                _aiAttemptDate = today;
                return;
            }

            // 今天已尝试过且无缓存，避免失败后反复调用
            if (_aiAttemptDate == today) return;

            // 调用 AI 获取当天全部推荐（每天仅一次）
            calledAi = true;
            IsAiRecommending = true;
            if (regenerateAfter) MainThread.BeginInvokeOnMainThread(GenerateHeroCards);

            var batch = await FetchAiRecommendationBatchAsync();
            _aiAttemptDate = today;
            if (batch.Count > 0)
            {
                _aiRecommendBatch = batch;
                _aiRecommendBatchDate = today;
                // 用第一条推荐理由更新默认文案（占位卡也能显示合理文字）
                var firstReason = batch[0].Reason;
                if (!string.IsNullOrWhiteSpace(firstReason)) AiRecommendReason = firstReason;
                SaveAiCacheToDisk(today, batch);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SearchVM] AI 每日推荐获取失败: {ex.Message}");
            _aiAttemptDate = today;
        }
        finally
        {
            if (calledAi) IsAiRecommending = false;
            _aiFetchInProgress = false;
            if (regenerateAfter) MainThread.BeginInvokeOnMainThread(GenerateHeroCards);
        }
    }

    /// <summary>
    /// 向 AI 请求当天推荐批次：把用户曲库中的候选歌（含 ID）交给 AI，让它挑选若干首并给出理由，
    /// 返回严格 JSON，随后按 ID 匹配回本地曲库，避免 AI 编造不存在的歌曲。
    /// </summary>
    private async Task<List<AiRecItem>> FetchAiRecommendationBatchAsync()
    {
        var pool = BuildAiCandidatePool();
        if (pool.Count == 0) return new();

        var sb = new StringBuilder();
        foreach (var s in pool)
            sb.AppendLine($"{s.Id}. {s.Title ?? "未知"} - {s.Artist ?? "未知艺术家"}");

        var count = Math.Min(8, pool.Count);
        var systemPrompt = "你是Yuki，猫爪音乐的AI音乐推荐助手，说话温柔可爱带点喵口癖。";
        var userPrompt =
            $"下面是用户曲库里的候选歌曲（每行格式：ID. 歌名 - 艺术家）：\n{sb}\n" +
            $"请从这些候选里挑选 {count} 首你最想推荐给用户的歌，为每首写一句温柔的推荐理由（不超过18字，不要加引号）。\n" +
            "只返回严格的 JSON 数组，不要任何多余文字或代码块标记，格式：[{\"id\":数字,\"reason\":\"理由\"}]";

        var raw = await _agentService.QuickAskAsync(systemPrompt, userPrompt);
        return ParseAiBatch(raw, pool);
    }

    /// <summary>构建 AI 推荐候选池：合并常听、收藏、每日推荐并去重</summary>
    private List<Song> BuildAiCandidatePool()
    {
        var candidates = new List<Song>();
        if (_allTopPlayedSongs.Count > 0) candidates.AddRange(_allTopPlayedSongs.Take(15));
        if (FavoriteSongs.Count > 0) candidates.AddRange(FavoriteSongs.Take(15));
        if (_allDailyRecommendSongs.Count > 0) candidates.AddRange(_allDailyRecommendSongs.Take(20));
        return candidates.GroupBy(s => s.Id).Select(g => g.First()).ToList();
    }

    /// <summary>解析 AI 返回的 JSON 推荐数组，仅保留候选池中真实存在的歌曲 ID</summary>
    private static List<AiRecItem> ParseAiBatch(string raw, List<Song> pool)
    {
        var result = new List<AiRecItem>();
        if (string.IsNullOrWhiteSpace(raw)) return result;
        try
        {
            var start = raw.IndexOf('[');
            var end = raw.LastIndexOf(']');
            if (start < 0 || end <= start) return result;
            var json = raw.Substring(start, end - start + 1);

            var items = System.Text.Json.JsonSerializer.Deserialize<List<AiRecItem>>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (items == null) return result;

            var validIds = pool.Select(s => s.Id).ToHashSet();
            foreach (var it in items)
            {
                if (!validIds.Contains(it.SongId)) continue;
                var reason = it.Reason?.Trim()?.Trim('"', '「', '」', '\n', '\r') ?? "";
                if (reason.Length > 40) reason = reason.Substring(0, 40);
                result.Add(new AiRecItem { SongId = it.SongId, Reason = reason });
                validIds.Remove(it.SongId); // 去重
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SearchVM] AI 推荐解析失败: {ex.Message}");
        }
        return result;
    }

    /// <summary>按歌曲 ID 从已加载的内存池（常听/收藏/每日推荐）解析出 Song 对象</summary>
    private Song? ResolveSongById(int id)
    {
        return _allTopPlayedSongs.FirstOrDefault(s => s.Id == id)
            ?? FavoriteSongs.FirstOrDefault(s => s.Id == id)
            ?? _allDailyRecommendSongs.FirstOrDefault(s => s.Id == id);
    }

    /// <summary>从磁盘读取当天的 AI 推荐缓存；日期不匹配或为空时返回 null</summary>
    private async Task<List<AiRecItem>?> LoadAiCacheFromDiskAsync(string date)
    {
        try
        {
            if (!File.Exists(_aiCacheFilePath)) return null;
            var json = await File.ReadAllTextAsync(_aiCacheFilePath);
            var cache = System.Text.Json.JsonSerializer.Deserialize<AiRecCache>(json);
            if (cache?.Date != date || cache.Items == null || cache.Items.Count == 0) return null;
            return cache.Items;
        }
        catch { return null; }
    }

    /// <summary>将当天的 AI 推荐批次整批写入磁盘缓存</summary>
    private void SaveAiCacheToDisk(string date, List<AiRecItem> items)
    {
        try
        {
            var dir = Path.GetDirectoryName(_aiCacheFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var cache = new AiRecCache { Date = date, Items = items };
            File.WriteAllText(_aiCacheFilePath, System.Text.Json.JsonSerializer.Serialize(cache));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SearchVM] AI 推荐缓存写入失败: {ex.Message}");
        }
    }

    /// <summary>使 AI 每日推荐缓存失效（内存 + 磁盘 + 尝试标记），用于手动刷新或重新扫描</summary>
    private void InvalidateAiCache()
    {
        _aiRecommendBatch = new();
        _aiRecommendBatchDate = null;
        _aiAttemptDate = null;
        _aiHeroIndex = 0;
        try { if (File.Exists(_aiCacheFilePath)) File.Delete(_aiCacheFilePath); } catch { }
    }

    /// <summary>AI 推荐开关切换时：持久化并重新生成 Hero 卡</summary>
    partial void OnIsAiRecommendationEnabledChanged(bool value)
    {
        Preferences.Default.Set("ai_recommendation_enabled", value);
        if (HeroCards.Count > 0 || _allDailyRecommendSongs.Count > 0)
        {
            GenerateHeroCards();
        }
    }

    /// <summary>刷新数据</summary>
    private async Task RefreshAsync()
    {
        try
        {
            _exploreDataService.InvalidateDailyRecommendCache();
            InvalidateAiCache();
            Services.CoverHelper.ClearCache();
            Preferences.Default.Remove("explore_last_load_date");

            _allDailyRecommendSongs = [];
            _allTopPlayedSongs = [];
            _allArtists = [];
            _allAlbums = [];
            _allRecentAddedSongs = [];
            ApplyFilters();

            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SearchVM] RefreshAsync failed: {ex.Message}");
        }
    }

    /// <summary>随机重新排序每日推荐</summary>
    private void ShuffleDaily()
    {
        if (_allDailyRecommendSongs.Count == 0) return;

        var random = new Random();
        _allDailyRecommendSongs = _allDailyRecommendSongs.OrderBy(_ => random.Next()).ToList();
        var shuffled = _allDailyRecommendSongs.Take(20).ToList();
        DailyRecommendSongs = new ObservableCollection<Song>(shuffled);
        _aiHeroIndex++; // 轮换到当天缓存里的下一首 AI 推荐（不重新调用 AI）
        GenerateHeroCards();
    }

    /// <summary>加载收藏歌曲并生成英雄卡片</summary>
    private async Task LoadFavoritesAndGenerateHeroCards()
    {
        try
        {
            var favoriteSongs = await _libraryService.GetFavoriteSongsAsync();
            if (favoriteSongs.Count > 0)
            {
                await Task.Run(() => Services.CoverHelper.BatchResolveCovers(favoriteSongs));
            }
            FavoriteSongs = new ObservableCollection<Song>(favoriteSongs);
            GenerateHeroCards();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SearchVM] LoadFavorites failed: {ex.Message}");
            GenerateHeroCards();
        }
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
    /// <summary>封面图源</summary>
    public ImageSource? CoverSource { get; set; }
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
    /// <summary>封面图源</summary>
    public ImageSource? CoverSource { get; set; }
}

/// <summary>首页英雄卡片项</summary>
/// <summary>AI 单条推荐项：歌曲 ID + 推荐理由（用于 JSON 缓存与解析 AI 返回）</summary>
public class AiRecItem
{
    /// <summary>歌曲 ID（对应本地曲库）</summary>
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public int SongId { get; set; }
    /// <summary>推荐理由文案</summary>
    [System.Text.Json.Serialization.JsonPropertyName("reason")]
    public string Reason { get; set; } = "";
}

/// <summary>AI 每日推荐磁盘缓存结构：日期 + 当天全部推荐项</summary>
public class AiRecCache
{
    /// <summary>缓存日期 "yyyy-MM-dd"</summary>
    public string Date { get; set; } = "";
    /// <summary>当天推荐项列表</summary>
    public List<AiRecItem> Items { get; set; } = new();
}

public class HeroCardItem
{
    /// <summary>标签</summary>
    public string Tag { get; set; } = "";
    /// <summary>标题</summary>
    public string Title { get; set; } = "";
    /// <summary>描述</summary>
    public string Description { get; set; } = "";
    /// <summary>关联歌曲</summary>
    public Song? Song { get; set; }
    /// <summary>渐变起始色</summary>
    public Color GradientStart { get; set; } = Colors.Blue;
    /// <summary>渐变结束色</summary>
    public Color GradientEnd { get; set; } = Colors.Purple;
    /// <summary>播放按钮图标（WinUI 需代码赋值，XAML 字面量不渲染）</summary>
    public ImageSource? PlayIcon { get; set; }
}
