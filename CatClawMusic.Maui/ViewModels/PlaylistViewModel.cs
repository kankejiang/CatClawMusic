using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 歌单列表页 ViewModel：管理 2 个系统歌单（收藏/最近播放）与用户自定义歌单的加载、
/// 创建、删除、收藏切换与计数刷新等交互。
/// </summary>
public partial class PlaylistViewModel : ObservableObject
{
    private readonly IMusicLibraryService _musicLibrary;
    private readonly MusicDatabase _db;
    private bool _isDirty = true;

    // 防止多个调用方并发加载导致 Playlists 被重复添加
    private readonly System.Threading.SemaphoreSlim _loadLock = new(1, 1);

    private static readonly long _epoch = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    /// <summary>歌单集合（含系统歌单与用户歌单）</summary>
    public ObservableCollection<Playlist> Playlists { get; } = new();

    /// <summary>状态文本（用于向用户展示加载进度或结果）</summary>
    [ObservableProperty]
    private string _statusText = "";

    /// <summary>是否正在加载歌单数据</summary>
    [ObservableProperty]
    private bool _isLoading = false;

    /// <summary>歌单总数（含系统歌单，用于顶部卡片展示）</summary>
    [ObservableProperty]
    private int _playlistCount;

    /// <summary>
    /// 初始化 <see cref="PlaylistViewModel"/> 实例。
    /// </summary>
    /// <param name="musicLibrary">音乐库服务，用于读写歌单数据</param>
    /// <param name="db">音乐数据库访问对象</param>
    public PlaylistViewModel(IMusicLibraryService musicLibrary, MusicDatabase db)
    {
        _musicLibrary = musicLibrary;
        _db = db;

        // 订阅音乐库歌单变更事件：AI Agent 或其他模块修改歌单后自动标记 dirty，
        // 避免移动端 PlaylistPage.OnAppearing 因未检查 _isDirty 而不刷新。
        _musicLibrary.PlaylistsChanged += OnPlaylistsChanged;
    }

    /// <summary>音乐库歌单变更回调：标记 dirty 以便页面下次出现时刷新</summary>
    private void OnPlaylistsChanged()
    {
        MarkDirty();
    }

    /// <summary>标记数据已变更，下次刷新时强制重新加载</summary>
    public void MarkDirty() => _isDirty = true;

    /// <summary>数据是否已变更（供页面 OnAppearing 检查是否需要刷新）</summary>
    public bool IsDirty => _isDirty;

    /// <summary>
    /// 加载播放列表（含2个系统歌单 + 用户歌单）
    /// </summary>
    [RelayCommand]
    public async Task LoadPlaylistsAsync()
    {
        await _loadLock.WaitAsync();
        IsLoading = true;
        StatusText = "加载中...";
        try
        {
            Playlists.Clear();

            var favCountTask = _musicLibrary.GetFavoriteSongCountAsync();
            var recentCountTask = _musicLibrary.GetRecentSongCountAsync();
            var favFirstIdTask = _musicLibrary.GetFirstFavoriteSongIdAsync();
            var recentFirstIdTask = _musicLibrary.GetFirstRecentSongIdAsync();

            await Task.WhenAll(favCountTask, recentCountTask,
                favFirstIdTask, recentFirstIdTask);

            Playlists.Add(new Playlist
            {
                Id = -2, Name = "收藏歌曲", SongCount = favCountTask.Result,
                IsSystem = true, CoverSongId = favFirstIdTask.Result, CreatedAt = _epoch + 1
            });
            Playlists.Add(new Playlist
            {
                Id = -3, Name = "最近播放", SongCount = recentCountTask.Result,
                IsSystem = true, CoverSongId = recentFirstIdTask.Result, CreatedAt = _epoch + 2
            });

            var userPlaylists = await _musicLibrary.GetAllPlaylistsAsync();

            // 并行查询每个歌单的第一首歌（避免串行 N 次 DB 往返）
            if (userPlaylists.Count > 0)
            {
                var coverTasks = userPlaylists.Select(async p =>
                {
                    try
                    {
                        var firstSong = await _db.GetFirstSongInPlaylistAsync(p.Id);
                        return (Playlist: p, CoverSongId: firstSong?.Id ?? 0);
                    }
                    catch
                    {
                        return (Playlist: p, CoverSongId: 0);
                    }
                }).ToList();
                var coverResults = await Task.WhenAll(coverTasks);
                foreach (var (p, coverId) in coverResults)
                {
                    p.CoverSongId = coverId;
                    Playlists.Add(p);
                }
            }

            // 批量解析每个歌单的封面（用 CoverSongId 查磁盘缓存或从音频文件提取）
            await Task.Run(() =>
            {
                foreach (var pl in Playlists)
                {
                    if (pl.CoverSongId <= 0) continue;
                    pl.CoverPath = Services.CoverHelper.GetCachedPath(pl.CoverSongId);
                    if (!File.Exists(pl.CoverPath))
                    {
                        // 缓存未命中，需要从数据库取歌曲文件路径来提取封面
                        try
                        {
                            var song = _db.GetSongByIdAsync(pl.CoverSongId).GetAwaiter().GetResult();
                            if (song != null)
                            {
                                var resolved = Services.CoverHelper.ResolveSingleCover(song);
                                pl.CoverPath = resolved;
                            }
                        }
                        catch { }
                    }
                }
            });

            _isDirty = false;
            PlaylistCount = Playlists.Count;
            StatusText = Playlists.Count == 0 ? "暂无播放列表" : "";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PlaylistVM] LoadPlaylists failed: {ex}");
            StatusText = "加载失败";
        }
        finally
        {
            IsLoading = false;
            _loadLock.Release();
        }
    }

    /// <summary>
    /// 增量刷新（仅在 dirty 时重新加载）
    /// </summary>
    public async Task RefreshIfChangedAsync()
    {
        if (!_isDirty && Playlists.Count > 0) return;
        await LoadPlaylistsAsync();
    }

    /// <summary>
    /// 刷新系统歌单计数（收藏/最近播放数量变化时调用）
    /// </summary>
    public async Task RefreshSystemPlaylistCountsAsync()
    {
        if (Playlists.Count < 2) return;
        try
        {
            var favCountTask = _musicLibrary.GetFavoriteSongCountAsync();
            var recentCountTask = _musicLibrary.GetRecentSongCountAsync();
            var favFirstIdTask = _musicLibrary.GetFirstFavoriteSongIdAsync();
            var recentFirstIdTask = _musicLibrary.GetFirstRecentSongIdAsync();

            await Task.WhenAll(favCountTask, recentCountTask,
                favFirstIdTask, recentFirstIdTask);

            UpdateSystemPlaylist(-2, "收藏歌曲", favCountTask.Result, favFirstIdTask.Result, _epoch + 1);
            UpdateSystemPlaylist(-3, "最近播放", recentCountTask.Result, recentFirstIdTask.Result, _epoch + 2);
        }
        catch { }
    }

    private void UpdateSystemPlaylist(int id, string name, int songCount, int coverSongId, long createdAt)
    {
        var existing = Playlists.FirstOrDefault(p => p.Id == id);
        if (existing == null) return;
        if (existing.SongCount == songCount && existing.CoverSongId == coverSongId) return;

        // 解析新封面歌曲的封面路径
        string? coverPath = null;
        if (coverSongId > 0)
        {
            coverPath = Services.CoverHelper.GetCachedPath(coverSongId);
            if (!File.Exists(coverPath))
            {
                try
                {
                    var song = _db.GetSongByIdAsync(coverSongId).GetAwaiter().GetResult();
                    if (song != null)
                        coverPath = Services.CoverHelper.ResolveSingleCover(song);
                }
                catch { }
            }
        }

        var idx = Playlists.IndexOf(existing);
        Playlists[idx] = new Playlist
        {
            Id = id, Name = name, SongCount = songCount,
            IsSystem = true, CoverSongId = coverSongId, CreatedAt = createdAt,
            CoverPath = coverPath
        };
    }

    /// <summary>
    /// 创建新歌单
    /// </summary>
    public async Task<int> CreatePlaylistAsync(string name)
    {
        int id = await _musicLibrary.CreatePlaylistAsync(name);
        MarkDirty();
        return id;
    }

    /// <summary>
    /// 删除歌单
    /// </summary>
    public async Task DeletePlaylistAsync(int playlistId)
    {
        await _musicLibrary.DeletePlaylistAsync(playlistId);
        MarkDirty();
    }

    /// <summary>
    /// 重命名歌单
    /// </summary>
    public async Task RenamePlaylistAsync(int playlistId, string newName)
    {
        var pl = await _musicLibrary.GetPlaylistByIdAsync(playlistId);
        if (pl == null) return;
        pl.Name = newName;
        await _musicLibrary.UpdatePlaylistAsync(pl);
        MarkDirty();
    }

    /// <summary>
    /// 添加歌曲到歌单
    /// </summary>
    public async Task AddSongToPlaylistAsync(int playlistId, int songId)
    {
        await _musicLibrary.AddSongToPlaylistAsync(playlistId, songId);
        MarkDirty();
    }

    /// <summary>
    /// 切换收藏
    /// </summary>
    public async Task ToggleFavoriteAsync(int songId, bool isFav)
    {
        await _db.SetFavoriteAsync(songId, isFav);
        MarkDirty();
        _ = RefreshSystemPlaylistCountsAsync();
    }

    /// <summary>
    /// 检查是否已收藏
    /// </summary>
    public async Task<bool> IsFavoriteAsync(int songId)
    {
        try { return await _db.IsFavoriteAsync(songId); }
        catch { return false; }
    }
}
