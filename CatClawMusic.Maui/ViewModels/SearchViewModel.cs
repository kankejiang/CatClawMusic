using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

public partial class SearchViewModel : ObservableObject
{
    private readonly ExploreDataService _exploreDataService;
    private readonly IAgentService _agentService;

    private List<Song> _allDailyRecommendSongs = [];
    private List<SearchArtistItem> _allArtists = [];
    private List<SearchAlbumItem> _allAlbums = [];
    private List<Song> _allTopPlayedSongs = [];
    private List<Song> _allRecentAddedSongs = [];

    [ObservableProperty]
    private ObservableCollection<Song> _dailyRecommendSongs = new();

    [ObservableProperty]
    private string _searchQuery = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _currentTabIndex;

    [ObservableProperty]
    private string _sectionTitle = "每日推荐";

    [ObservableProperty]
    private ObservableCollection<SearchArtistItem> _artists = new();

    [ObservableProperty]
    private ObservableCollection<SearchAlbumItem> _albums = new();

    [ObservableProperty]
    private ObservableCollection<Song> _topPlayedSongs = new();

    [ObservableProperty]
    private ObservableCollection<Song> _recentAddedSongs = new();

    [ObservableProperty]
    private string _agentName = BuiltinAgent.Yuki.Name;

    [ObservableProperty]
    private ObservableCollection<ChatMessage> _chatMessages = new();

    [ObservableProperty]
    private string _chatInput = "";

    [ObservableProperty]
    private bool _isChatMode;

    [ObservableProperty]
    private string _emptyStateText = "这里还没有内容";

    [ObservableProperty]
    private bool _isCurrentTabEmpty;

    // Featured hero card
    [ObservableProperty]
    private bool _hasFeaturedSong;

    [ObservableProperty]
    private string _featuredSongTitle = "";

    [ObservableProperty]
    private string _featuredSongArtist = "";

    [ObservableProperty]
    private ImageSource? _featuredSongCover;

    private Song? _featuredSong;

    public Song? FeaturedSong => _featuredSong;

    // Search dropdown
    [ObservableProperty]
    private ObservableCollection<Song> _searchResults = new();

    [ObservableProperty]
    private ObservableCollection<SearchArtistItem> _searchArtistResults = new();

    [ObservableProperty]
    private ObservableCollection<SearchAlbumItem> _searchAlbumResults = new();

    [ObservableProperty]
    private bool _showSearchResults;

    public IRelayCommand<int> SwitchTabCommand { get; }
    public IAsyncRelayCommand LoadDataCommand { get; }
    public IAsyncRelayCommand LoadExploreDataCommand { get; }
    public IRelayCommand EnterChatModeCommand { get; }
    public IRelayCommand ExitChatModeCommand { get; }
    public IAsyncRelayCommand SendMessageCommand { get; }

    public event EventHandler? EnterChatModeRequested;
    public event EventHandler? ExitChatModeRequested;

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

    private async Task LoadDataAsync()
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

    public async Task LoadExploreDataAsync()
    {
        await LoadDataAsync();
    }

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

    partial void OnSearchQueryChanged(string value)
    {
        UpdateSearchDropdown(value);
    }

    private void UpdateSearchDropdown(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            ShowSearchResults = false;
            SearchResults.Clear();
            SearchArtistResults.Clear();
            SearchAlbumResults.Clear();
            return;
        }

        var q = query.Trim();

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

        SearchResults = new ObservableCollection<Song>(songs);
        SearchArtistResults = new ObservableCollection<SearchArtistItem>(artists);
        SearchAlbumResults = new ObservableCollection<SearchAlbumItem>(albums);
        ShowSearchResults = songs.Count > 0 || artists.Count > 0 || albums.Count > 0;
    }

    public void ClearSearchDropdown()
    {
        ShowSearchResults = false;
        SearchResults.Clear();
        SearchArtistResults.Clear();
        SearchAlbumResults.Clear();
    }

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

public class SearchArtistItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string? CoverSource { get; set; }
}

public class SearchAlbumItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string ArtistName { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string? CoverSource { get; set; }
}
