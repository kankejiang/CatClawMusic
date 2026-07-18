using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CatClawMusic.Maui.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Globalization;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// "全部歌曲"二级页 ViewModel：支持搜索、多维度排序、播放全部/随机播放、A-Z 索引。
/// </summary>
public partial class AllSongsViewModel : ObservableObject
{
    private readonly MusicDatabase _db;
    private readonly PlayQueue _queue;
    private readonly IAudioPlayerService _audioService;
    private readonly IMusicLibraryService _musicLibrary;

    private List<Song> _allSongs = new();
    private CancellationTokenSource? _filterCts;

    public AllSongsViewModel(MusicDatabase db, PlayQueue queue, IAudioPlayerService audioService, IMusicLibraryService musicLibrary)
    {
        _db = db;
        _queue = queue;
        _audioService = audioService;
        _musicLibrary = musicLibrary;

        // 初始高亮默认排序项（标题）
        foreach (var option in SortOptions)
            option.IsActive = option.Key == _sortKey;
    }

    // === 歌曲列表内存缓存（跨页实例共享，避免每次进入都全量重查 DB） ===
    private static readonly object _songCacheLock = new();
    private static readonly Dictionary<string, List<Song>> _songCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, DateTime> _songCacheStamp = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan _songCacheTtl = TimeSpan.FromMinutes(2);

    private static bool TryGetCached(string source, out List<Song> songs)
    {
        lock (_songCacheLock)
        {
            if (_songCache.TryGetValue(source, out var list)
                && _songCacheStamp.TryGetValue(source, out var stamp)
                && DateTime.UtcNow - stamp < _songCacheTtl)
            {
                songs = list;
                return true;
            }
        }
        songs = new List<Song>();
        return false;
    }

    private static void SetCached(string source, List<Song> songs)
    {
        lock (_songCacheLock)
        {
            _songCache[source] = songs;
            _songCacheStamp[source] = DateTime.UtcNow;
        }
    }

    /// <summary>扫描完成 / 歌曲变化时清空缓存，下次进入重新拉取最新列表。</summary>
    public static void InvalidateCache()
    {
        lock (_songCacheLock)
        {
            _songCache.Clear();
            _songCacheStamp.Clear();
        }
    }

    // === 页面标题与统计 ===

    [ObservableProperty] private string _pageTitle = "全部歌曲";
    [ObservableProperty] private string _songCountText = "0 首歌曲";
    [ObservableProperty] private bool _isLoading;

    // === 搜索 ===

    [ObservableProperty] private string _searchQuery = "";

    partial void OnSearchQueryChanged(string value)
    {
        _filterCts?.Cancel();
        _filterCts?.Dispose();
        _filterCts = new CancellationTokenSource();
        _ = FilterAndSortAsync(_filterCts.Token);
    }

    // === 排序 ===

    [ObservableProperty] private string _sortKey = "title";
    [ObservableProperty] private bool _sortAscending = true;

    /// <summary>排序选项列表</summary>
    public ObservableCollection<SortOption> SortOptions { get; } = new()
    {
        new SortOption("title", "标题", true),
        new SortOption("artist", "艺术家", false),
        new SortOption("album", "专辑", false),
        new SortOption("added", "最近添加", false),
        new SortOption("dur", "时长", false),
        new SortOption("plays", "播放次数", false),
    };

    // === 歌曲列表 ===

    [ObservableProperty] private ObservableCollection<Song> _songs = new();
    [ObservableProperty] private Song? _selectedSong;

    /// <summary>A-Z 分组标签列表（按标题排序时显示）</summary>
    [ObservableProperty] private ObservableCollection<GroupHeader> _groupHeaders = new();

    /// <summary>是否显示 A-Z 索引（仅按标题排序且无搜索时）</summary>
    [ObservableProperty] private bool _showIndexRail;

    // === 数据源类型 ===

    private string _source = "local"; // "local" | "network" | "all"

    /// <summary>
    /// 加载歌曲数据。通过 source 参数决定加载哪个数据源。
    /// </summary>
    public async Task LoadAsync(string source)
    {
        _source = source;
        IsLoading = true;

        try
        {
            var title = ResolveTitle(source);

            // 命中内存缓存（2 分钟有效）→ 立即首屏渲染，后台静默刷新抓取新增/变化的歌曲
            if (TryGetCached(source, out var cached))
            {
                await ApplyList(cached, title);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var fresh = await QuerySongsAsync(source);
                        SetCached(source, fresh);
                        await ApplyList(fresh, title);
                    }
                    catch { }
                });
                return;
            }

            var songs = await QuerySongsAsync(source);
            SetCached(source, songs);

            // 后台分块解析封面（不阻塞 UI，分块让出 CPU 避免整体卡顿）
            _ = Task.Run(async () => await CoverHelper.BatchResolveCoversAsync(songs));

            await ApplyList(songs, title);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AllSongsVM] Load failed: {ex.Message}");
            IsLoading = false;
        }
    }

    private static string ResolveTitle(string source) => source switch
    {
        "local" => "本地音乐",
        "network" => "网络音乐",
        "favorites" => "我喜欢的",
        "recent" => "最近播放",
        _ => "全部歌曲"
    };

    /// <summary>后台执行 DB 查询 + 排序（按当前排序键），不阻塞 UI 线程。</summary>
    private async Task<List<Song>> QuerySongsAsync(string source)
    {
        var loaded = source switch
        {
            "local" => await _db.GetSongsAsync(),
            "network" => await _db.GetCachedNetworkSongsAsync(),
            "favorites" => await _db.GetFavoriteSongsAsync(),
            "recent" => await _db.GetRecentSongsAsync(),
            _ => (await _db.GetSongsAsync()).Concat(await _db.GetCachedNetworkSongsAsync()).ToList()
        };
        return SortSongs(loaded);
    }

    /// <summary>将（已排序的）歌曲列表赋值到 UI 集合，并刷新标题 / 统计 / A-Z 索引条。</summary>
    private Task ApplyList(List<Song> songs, string title)
    {
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            PageTitle = title;
            _allSongs = songs;
            var filtered = SortSongs(songs.ToList());
            Songs = new ObservableCollection<Song>(filtered);
            SongCountText = $"{filtered.Count:N0} 首歌曲";
            ShowIndexRail = SortKey == "title" && string.IsNullOrWhiteSpace(SearchQuery);
            if (ShowIndexRail)
                BuildGroupHeaders(filtered);
            else
                GroupHeaders.Clear();
            IsLoading = false;
        });
    }

    /// <summary>切换排序字段（再次点击同一切换升降序）</summary>
    public void ToggleSort(string key)
    {
        if (SortKey == key)
            SortAscending = !SortAscending;
        else
        {
            SortKey = key;
            SortAscending = key != "added" && key != "plays"; // 时间/次数默认降序
        }
        foreach (var option in SortOptions)
            option.IsActive = option.Key == SortKey;
        _ = FilterAndSortAsync();
    }

    private async Task FilterAndSortAsync(CancellationToken ct = default)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(SearchQuery))
                await Task.Delay(250, ct);
            ct.ThrowIfCancellationRequested();

            var filtered = await Task.Run(() =>
            {
                IEnumerable<Song> q = _allSongs;
                if (!string.IsNullOrWhiteSpace(SearchQuery))
                {
                    var search = SearchQuery.ToLowerInvariant();
                    q = q.Where(s =>
                        (s.Title?.ToLowerInvariant().Contains(search) == true) ||
                        (s.Artist?.ToLowerInvariant().Contains(search) == true) ||
                        (s.Album?.ToLowerInvariant().Contains(search) == true));
                }
                return SortSongs(q.ToList());
            }, ct);

            ct.ThrowIfCancellationRequested();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Songs = new ObservableCollection<Song>(filtered);
                SongCountText = $"{filtered.Count:N0} 首歌曲";
                ShowIndexRail = SortKey == "title" && string.IsNullOrWhiteSpace(SearchQuery);
                if (ShowIndexRail)
                    BuildGroupHeaders(filtered);
                else
                    GroupHeaders.Clear();
            });
        }
        catch (OperationCanceledException) { }
    }

    private List<Song> SortSongs(List<Song> songs)
    {
        var collator = CultureInfo.CurrentCulture.CompareInfo;
        var dir = SortAscending ? 1 : -1;

        return SortKey switch
        {
            "title" => songs.OrderBy(s => s.Title ?? "", StringComparer.CurrentCulture).ToList(),
            "artist" => songs.OrderBy(s => s.Artist ?? "", StringComparer.CurrentCulture)
                             .ThenBy(s => s.Title ?? "", StringComparer.CurrentCulture).ToList(),
            "album" => songs.OrderBy(s => s.Album ?? "", StringComparer.CurrentCulture)
                            .ThenBy(s => s.Title ?? "", StringComparer.CurrentCulture).ToList(),
            "added" => songs.OrderByDescending(s => s.DateAdded).ToList(),
            "dur" => songs.OrderBy(s => s.Duration).ToList(),
            "plays" => songs.OrderByDescending(s => s.PlayCount).ToList(),
            _ => songs
        };
    }

    private static readonly CompareInfo _compareInfo = CultureInfo.GetCultureInfo("zh-Hans-CN").CompareInfo;

    private void BuildGroupHeaders(List<Song> sorted)
    {
        var headers = new ObservableCollection<GroupHeader>();
        string? lastGroup = null;

        for (int i = 0; i < sorted.Count; i++)
        {
            var g = GetInitial(sorted[i].Title ?? "");
            if (g != lastGroup)
            {
                headers.Add(new GroupHeader { Label = g, Index = i });
                lastGroup = g;
            }
        }

        GroupHeaders = headers;
    }

    private static string GetInitial(string title)
    {
        if (string.IsNullOrEmpty(title)) return "#";
        var c = title[0];
        if (char.IsAsciiLetter(c)) return char.ToUpperInvariant(c).ToString();
        if (char.IsDigit(c)) return "#";
        return c.ToString(); // 中文直接用首字
    }

    // === 播放 ===

    [RelayCommand]
    private async Task PlayAllAsync()
    {
        if (Songs.Count == 0) return;
        _queue.SetSongs([.. Songs]);
        _queue.SelectSong(Songs[0].Id);
        try { await _audioService.PlayAsync(Songs[0].FilePath); }
        catch { }
    }

    [RelayCommand]
    private async Task ShufflePlayAsync()
    {
        if (Songs.Count == 0) return;
        _queue.SetSongs([.. Songs]);
        // 仅设置 Shuffle 模式即可触发洗牌（PlayQueue.PlayMode 的 setter 内会 EnableShuffle，
        // 此时无当前曲 → 随机起点）。切勿再额外调用 EnableShuffle：二次洗牌会把原列表第一首
        // 固定到洗牌后的第 0 位，导致"随机播放"总是从第一首开始。
        _queue.PlayMode = PlayMode.Shuffle;
        var first = _queue.CurrentSong;
        if (first != null)
        {
            try { await _audioService.PlayAsync(first.FilePath); }
            catch { }
        }
    }

    [RelayCommand]
    private async Task PlaySongAsync(Song? song)
    {
        if (song == null) return;
        _queue.SetSongs([.. Songs]);
        _queue.SelectSong(song.Id);
        try { await _audioService.PlayAsync(song.FilePath); }
        catch { }
    }
}

// === 辅助类型 ===

// 注意：排序 chip 与筛选 chip 现统一复用 ViewModels/ChipModels.cs 中的 SortOption / FilterChip，
// 与艺术家页、专辑页完全一致的实现，避免再次出现"空框"渲染问题。

public class GroupHeader
{
    public string Label { get; set; } = "";
    public int Index { get; set; }
}
