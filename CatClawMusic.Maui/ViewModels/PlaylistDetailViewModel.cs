using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Maui.ApplicationModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 歌单详情页 ViewModel：加载指定歌单（含"收藏/最近播放"等虚拟歌单）的歌曲列表，
/// 提供搜索、多维度排序、A-Z 索引、单曲播放、整列表播放/随机播放、移除歌曲等交互能力。
/// 样式与 <see cref="AllSongsViewModel"/> 保持一致。
/// </summary>
public partial class PlaylistDetailViewModel : ObservableObject
{
    private readonly IMusicLibraryService _musicLibrary;
    private readonly IAudioPlayerService? _audioPlayer;
    private readonly PlayQueue? _playQueue;
    private readonly MusicDatabase _db;

    /// <summary>原始歌曲列表（按已启用协议过滤后，未经来源筛选/搜索/排序）</summary>
    private List<Song> _allSongsRaw = new();

    /// <summary>当前筛选范围内的歌曲列表（经来源筛选，未经搜索/排序）</summary>
    private List<Song> _allSongs = new();

    private int _playlistId;
    private CancellationTokenSource? _filterCts;

    // === 歌曲列表 ===

    /// <summary>当前展示的歌曲集合（已应用搜索 + 排序）</summary>
    [ObservableProperty]
    private ObservableCollection<Song> _songs = new();

    [ObservableProperty]
    private Song? _selectedSong;

    // === 页面信息 ===

    [ObservableProperty]
    private Playlist _playlist = new();

    [ObservableProperty]
    private string _playlistName = "";

    /// <summary>歌曲数量文本（如 "42 首歌曲"）</summary>
    [ObservableProperty]
    private string _songCountText = "0 首歌曲";

    /// <summary>歌单封面路径（取歌单内第一首已解析封面的歌曲）</summary>
    [ObservableProperty]
    private string? _playlistCover;

    [ObservableProperty]
    private bool _isLoading = false;

    // === 搜索 ===

    [ObservableProperty]
    private string _searchQuery = "";

    partial void OnSearchQueryChanged(string value)
    {
        _filterCts?.Cancel();
        _filterCts?.Dispose();
        _filterCts = new CancellationTokenSource();
        _ = FilterAndSortAsync(_filterCts.Token);
    }

    // === 排序 ===

    [ObservableProperty]
    private string _sortKey = "title";

    [ObservableProperty]
    private bool _sortAscending = true;

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

    // === A-Z 索引 ===

    [ObservableProperty]
    private ObservableCollection<GroupHeader> _groupHeaders = new();

    [ObservableProperty]
    private bool _showIndexRail;

    // === 事件 ===

    /// <summary>请求播放某首歌曲时触发，供外部页面订阅以同步 UI 状态</summary>
    public event Action<Song>? SongPlayRequested;

    /// <summary>
    /// 初始化 <see cref="PlaylistDetailViewModel"/> 实例。
    /// </summary>
    public PlaylistDetailViewModel(IMusicLibraryService musicLibrary,
        MusicDatabase db,
        IAudioPlayerService? audioPlayer = null,
        PlayQueue? playQueue = null)
    {
        _musicLibrary = musicLibrary;
        _db = db;
        _audioPlayer = audioPlayer;
        _playQueue = playQueue;

        // 初始高亮默认排序项（标题）
        foreach (var option in SortOptions)
            option.IsActive = option.Key == _sortKey;
    }

    /// <summary>
    /// 设置歌单参数并加载：根据歌单 ID 选择不同数据源（收藏/最近/普通歌单），
    /// 并按已启用协议过滤歌曲。
    /// </summary>
    /// <param name="playlistId">歌单 ID（-2=收藏, -3=最近播放, -4=最多播放, 其他=普通歌单）</param>
    /// <param name="name">歌单名称</param>
    public async Task LoadPlaylistAsync(int playlistId, string name)
    {
        _playlistId = playlistId;
        PlaylistName = name;
        IsLoading = true;

        try
        {
            List<Song> songs;

            switch (playlistId)
            {
                case -2:
                    songs = await _musicLibrary.GetFavoriteSongsAsync();
                    break;
                case -3:
                    songs = await _musicLibrary.GetRecentSongsAsync();
                    break;
                case -4:
                    // 最多播放：按播放次数倒序取前 200 首
                    songs = await _musicLibrary.GetTopPlayedSongsAsync(200);
                    break;
                default:
                    songs = await _musicLibrary.GetPlaylistSongsAsync(playlistId);
                    break;
            }

            var enabledProtocols = await _db.GetEnabledProtocolsAsync();
            _allSongsRaw = _db.FilterByEnabledProtocols(songs, enabledProtocols);
            _allSongs = _allSongsRaw.ToList();

            // 后台分块解析封面（不阻塞 UI）；封面就绪经 Song.CoverArtPath(INPC) 自动刷新单元格。
            if (_allSongsRaw.Count > 0)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Services.CoverHelper.BatchResolveCoversAsync(_allSongsRaw);
                        var cover = _allSongsRaw.FirstOrDefault(s => !string.IsNullOrEmpty(s.CoverArtPath))?.CoverArtPath;
                        if (!string.IsNullOrEmpty(cover))
                            await MainThread.InvokeOnMainThreadAsync(() => PlaylistCover = cover);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("PlaylistDetailViewModel", $"[PlaylistDetailVM] 后台封面解析失败: {ex.Message}");
                    }
                });
            }

            Playlist = new Playlist
            {
                Id = playlistId,
                Name = name,
                SongCount = _allSongsRaw.Count
            };

            await ApplyList();
        }
        catch (Exception ex)
        {
            Log.Debug("PlaylistDetailViewModel", $"[PlaylistDetailVM] LoadAsync({playlistId}) failed: {ex}");
            SongCountText = "加载失败";
        }
        finally { IsLoading = false; }
    }

    /// <summary>将 _allSongs 排序后应用到 UI，并刷新统计与 A-Z 索引。</summary>
    private Task ApplyList()
    {
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            var filtered = SortSongs(_allSongs.ToList());
            Songs = new ObservableCollection<Song>(filtered);
            SongCountText = $"{filtered.Count:N0} 首歌曲";
            ShowIndexRail = SortKey == "title" && string.IsNullOrWhiteSpace(SearchQuery);
            if (ShowIndexRail)
                BuildGroupHeaders(filtered);
            else
                GroupHeaders.Clear();
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

    /// <summary>搜索 + 排序（带防抖与取消支持）</summary>
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

    /// <summary>
    /// 播放歌曲：若为当前曲则切换播放/暂停，否则将其设为播放队列当前曲并播放。
    /// </summary>
    [RelayCommand]
    public async Task PlaySongAsync(Song? song)
    {
        if (song == null || _audioPlayer == null || _playQueue == null) return;

        var currentSongInQueue = _playQueue.CurrentSong;
        if (currentSongInQueue != null && currentSongInQueue.Id == song.Id)
        {
            if (_audioPlayer.IsPlaying)
                await _audioPlayer.PauseAsync();
            else
                await _audioPlayer.ResumeAsync();
        }
        else
        {
            _playQueue.SetSongs([.. Songs]);
            _playQueue.SelectSong(song.Id);
            if (!string.IsNullOrEmpty(song.FilePath))
                await _audioPlayer.PlayAsync(song.FilePath);
            _ = RecordPlayAsync(song);
            SongPlayRequested?.Invoke(song);
        }
    }

    /// <summary>播放全部：将歌单全部歌曲加入播放队列并从首曲开始播放</summary>
    [RelayCommand]
    public async Task PlayAllAsync()
    {
        if (_audioPlayer == null || _playQueue == null || Songs.Count == 0) return;
        _playQueue.SetSongs([.. Songs]);
        var first = Songs[0];
        _playQueue.SelectSong(first.Id);
        if (!string.IsNullOrEmpty(first.FilePath))
            await _audioPlayer.PlayAsync(first.FilePath);
        _ = RecordPlayAsync(first);
        SongPlayRequested?.Invoke(first);
    }

    /// <summary>随机播放：将歌单全部歌曲加入播放队列并随机选择起点播放</summary>
    [RelayCommand]
    public async Task ShufflePlayAsync()
    {
        if (_audioPlayer == null || _playQueue == null || Songs.Count == 0) return;
        _playQueue.SetSongs([.. Songs]);
        // 仅设置 Shuffle 模式即可触发洗牌（PlayQueue.PlayMode 的 setter 内会 EnableShuffle，
        // 此时无当前曲 → 随机起点）。切勿再额外调用 EnableShuffle：二次洗牌会把原列表第一首
        // 固定到洗牌后的第 0 位，导致"随机播放"总是从第一首开始。
        _playQueue.PlayMode = PlayMode.Shuffle;
        var first = _playQueue.CurrentSong;
        if (first != null)
        {
            if (!string.IsNullOrEmpty(first.FilePath))
                await _audioPlayer.PlayAsync(first.FilePath);
            _ = RecordPlayAsync(first);
            SongPlayRequested?.Invoke(first);
        }
    }

    // === 歌单管理 ===

    /// <summary>
    /// 从歌单移除歌曲。
    /// </summary>
    [RelayCommand]
    public async Task RemoveSongAsync(Song? song)
    {
        if (song == null || _playlistId <= 0) return;
        await RemoveSongsFromPlaylistAsync(new[] { song.Id });
    }

    /// <summary>
    /// 批量移除歌曲：从歌单移除多首歌曲并同步集合。
    /// </summary>
    public async Task<int> RemoveSongsFromPlaylistAsync(IEnumerable<int> songIds)
    {
        if (_playlistId <= 0) return 0;
        var ids = songIds.ToHashSet();
        if (ids.Count == 0) return 0;

        await _musicLibrary.RemoveSongsFromPlaylistAsync(_playlistId, ids);

        // 从原始数据中移除，然后重新排序显示
        _allSongsRaw.RemoveAll(s => ids.Contains(s.Id));
        _allSongs.RemoveAll(s => ids.Contains(s.Id));
        await ApplyList();
        return ids.Count;
    }

    /// <summary>
    /// 切换收藏状态。
    /// </summary>
    public async Task ToggleFavoriteAsync(int songId, bool isFav)
    {
        await _db.SetFavoriteAsync(songId, isFav);
    }

    /// <summary>
    /// 按来源筛选：在原始歌曲集合上按 local / network / all 进行筛选。
    /// </summary>
    public void ApplySourceFilter(string filter)
    {
        _allSongs = filter switch
        {
            "local" => _allSongsRaw.Where(s => s.Source == SongSource.Local).ToList(),
            "network" => _allSongsRaw.Where(s => s.Source != SongSource.Local).ToList(),
            _ => _allSongsRaw.ToList()
        };
        _ = FilterAndSortAsync();
    }

    private async Task RecordPlayAsync(Song song)
    {
        try
        {
            await _db.RecordPlayAsync(song.Id);
        }
        catch { }
    }
}
