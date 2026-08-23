using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.Maui.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Text;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.ApplicationModel;
using System.ComponentModel;

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
    private readonly OnlineMusicAggregator _onlineMusic;
    private readonly IPluginManager _pluginManager;
    private readonly IServiceProvider _services;

    private List<Song> _allDailyRecommendSongs = [];
    private List<SearchArtistItem> _allArtists = [];
    private List<SearchAlbumItem> _allAlbums = [];
    private List<Song> _allTopPlayedSongs = [];
    private List<Song> _allRecentAddedSongs = [];

    /// <summary>LoadDataAsync 重入守卫（Interlocked 用）：启动时多处会并发触发加载，只让第一个真正执行。</summary>
    private int _loadInProgress;

    // 搜索专用：包含全部艺术家/专辑/歌曲（非每日推荐10个），确保搜索栏能匹配到库内任意艺术家/专辑/歌曲
    private List<SearchArtistItem> _allArtistsForSearch = [];
    private List<SearchAlbumItem> _allAlbumsForSearch = [];
    private List<Song> _allSongsForSearch = [];

    /// <summary>每日推荐歌曲集合（已应用筛选）</summary>
    [ObservableProperty]
    private ObservableCollection<Song> _dailyRecommendSongs = new();

    /// <summary>搜索关键字</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOnlineEntry))]
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

    /// <summary>首页"推荐歌单"网格：已应用筛选的专辑前 8 张（4×2 布局）</summary>
    [ObservableProperty]
    private ObservableCollection<SearchAlbumItem> _recommendAlbums = new();

    /// <summary>最多播放歌曲集合（已应用筛选）</summary>
    [ObservableProperty]
    private ObservableCollection<Song> _topPlayedSongs = new();

    /// <summary>最新添加歌曲集合（已应用筛选）</summary>
    [ObservableProperty]
    private ObservableCollection<Song> _recentAddedSongs = new();

    /// <summary>当前 Agent 名称</summary>
    [ObservableProperty]
    private string _agentName = BuiltinAgent.Yuki.Name;

    /// <summary>聊天消息集合（使用 ObservableChatMessage 以支持气泡内思考过程展示）</summary>
    [ObservableProperty]
    private ObservableCollection<ObservableChatMessage> _chatMessages = new();

    /// <summary>当前正在思考的消息引用（用于 OnPartialMessage 追加步骤）</summary>
    private ObservableChatMessage? _currentThinkingMessage;

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
    [NotifyPropertyChangedFor(nameof(ShowSearchDropdown))]
    [NotifyPropertyChangedFor(nameof(ShowOnlineEntry))]
    private bool _showSearchResults;

    /// <summary>搜索框非空但无任何匹配结果时为 true，用于展示"问问 Yuki"入口</summary>
    [ObservableProperty]
    private bool _hasNoSearchResults;

    /// <summary>在线音乐搜索结果（音源插件聚合搜索，多平台合并）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSearchDropdown))]
    [NotifyPropertyChangedFor(nameof(ShowOnlineEntry))]
    private ObservableCollection<OnlineSong> _onlineSearchResults = new();

    /// <summary>是否正在在线搜索（展示加载提示）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSearchDropdown))]
    private bool _isSearchingOnline;

    /// <summary>是否有在线搜索结果（控制在线区块可见性）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSearchDropdown))]
    [NotifyPropertyChangedFor(nameof(ShowOnlineEntry))]
    private bool _hasOnlineSearchResults;

    /// <summary>已启用的在线音源插件列表（入口卡展示）</summary>
    [ObservableProperty]
    private ObservableCollection<OnlineProviderView> _onlineProviders = new();

    /// <summary>是否有已启用的在线音源插件</summary>
    [ObservableProperty]
    private bool _hasOnlineProviders;

    /// <summary>搜索下拉区显隐：本地结果 / 在线结果 / 在线搜索中任一为真即显示（在线结果不再被本地空结果挡住）</summary>
    public bool ShowSearchDropdown => ShowSearchResults || HasOnlineSearchResults || IsSearchingOnline;

    /// <summary>无搜索输入时是否显示"在线音乐"入口卡（音源列表 + 示例搜索）</summary>
    public bool ShowOnlineEntry =>
        !IsSearchOpen
        && string.IsNullOrWhiteSpace(SearchQuery)
        && !ShowSearchResults
        && !HasOnlineSearchResults;

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
    [NotifyPropertyChangedFor(nameof(ShowOnlineEntry))]
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
    public SearchViewModel(ExploreDataService exploreDataService, IAgentService agentService, IMusicLibraryService libraryService, ChatMemoryService chatMemoryService, MusicDatabase database, OnlineMusicAggregator onlineMusic, IPluginManager pluginManager, IServiceProvider services)
    {
        _exploreDataService = exploreDataService;
        _agentService = agentService;
        _libraryService = libraryService;
        _chatMemoryService = chatMemoryService;
        _database = database;
        _onlineMusic = onlineMusic;
        _pluginManager = pluginManager;
        _services = services;
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

        RefreshOnlineProviders();

        _ = LoadDataAsync();
    }

    /// <summary>刷新已启用在线音源列表（入口卡展示；插件安装/启用状态变化后调用）</summary>
    public void RefreshOnlineProviders()
    {
        var list = new List<OnlineProviderView>();
        try
        {
            foreach (var p in _onlineMusic.GetProviders())
            {
                var name = string.IsNullOrWhiteSpace(p.PlatformName) ? "在线音源" : p.PlatformName;
                if (string.IsNullOrWhiteSpace(name)) continue;
                list.Add(new OnlineProviderView { Name = name, Icon = ProviderIcon(name) });
            }
        }
        catch (Exception ex)
        {
            Log.Debug("SearchViewModel", $"[SearchVM] RefreshOnlineProviders failed: {ex.Message}");
        }
        OnlineProviders = new ObservableCollection<OnlineProviderView>(list);
        HasOnlineProviders = list.Count > 0;
    }

    private static string ProviderIcon(string name)
    {
        if (name.Contains("Apple", StringComparison.OrdinalIgnoreCase)) return "🍎";
        if (name.Contains("酷狗", StringComparison.OrdinalIgnoreCase)) return "🐶";
        if (name.Contains("网易", StringComparison.OrdinalIgnoreCase)) return "🎵";
        if (name.Contains("QQ", StringComparison.OrdinalIgnoreCase)) return "🐧";
        if (name.Contains("Soda", StringComparison.OrdinalIgnoreCase)) return "🧃";
        return "🎧";
    }

    /// <summary>从在线音乐入口卡点击示例关键词/音源触发搜索</summary>
    [RelayCommand]
    public void SearchKeyword(string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return;
        SearchQuery = keyword.Trim();
        IsSearchOpen = true;
    }

    /// <summary>是否正在手动刷新（发现页右上角刷新按钮的加载反馈）</summary>
    [ObservableProperty]
    private bool _isRefreshing;

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

    /// <summary>聊天历史加载完成事件（首次加载或加载更多后触发，供页面处理滚动）</summary>
    public event EventHandler<ChatHistoryLoadedEventArgs>? ChatHistoryLoaded;

    /// <summary>进入聊天模式时加载最近30条历史记录（倒序存储：index 0 = 最新）</summary>
    /// <summary>根据当前 SearchQuery 重新过滤各分区集合（供 PC 端顶栏搜索调用）</summary>
    public void ApplyFilters()
    {
        var query = SearchQuery?.Trim();
        var hasQuery = !string.IsNullOrWhiteSpace(query);

        // 注意：必须原地更新既有 ObservableCollection（Clear + Add），而非重新赋值一个新实例。
        // .NET 11 WinUI 的 ItemsView2 对直接替换 ItemsSource 的新实例可能不触发渲染（表现为
        // 数据已填充但界面空白）；而 AiPlaylists 等原地更新集合的分区能正常显示。
        UpdateCollection(DailyRecommendSongs, FilterSongs(_allDailyRecommendSongs, query));
        UpdateCollection(Artists, hasQuery
            ? _allArtists.Where(a =>
                a.Name.Contains(query!, StringComparison.OrdinalIgnoreCase) ||
                a.Subtitle.Contains(query!, StringComparison.OrdinalIgnoreCase))
            : _allArtists);
        UpdateCollection(Albums, hasQuery
            ? _allAlbums.Where(a =>
                a.Title.Contains(query!, StringComparison.OrdinalIgnoreCase) ||
                a.ArtistName.Contains(query!, StringComparison.OrdinalIgnoreCase) ||
                a.Subtitle.Contains(query!, StringComparison.OrdinalIgnoreCase))
            : _allAlbums);
        // 首页"推荐歌单"网格只取前 8 张（4 列 × 2 行），与专辑 tab 的全量网格分开
        UpdateCollection(RecommendAlbums, Albums.Take(8));
        UpdateCollection(TopPlayedSongs, FilterSongs(_allTopPlayedSongs, query));
        UpdateCollection(RecentAddedSongs, FilterSongs(_allRecentAddedSongs, query));

        RefreshEmptyState();
    }

    /// <summary>原地重置 ObservableCollection&lt;T&gt; 内容，避免替换 ItemsSource 新实例在 WinUI 下不渲染。</summary>
    private static void UpdateCollection<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
            target.Add(item);
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
}
