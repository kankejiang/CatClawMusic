using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

public partial class PlaylistViewModel : ObservableObject
{
    private readonly IMusicLibraryService _musicLibrary;
    private readonly MusicDatabase _db;
    private bool _isDirty = true;

    private static readonly long _epoch = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    public ObservableCollection<Playlist> Playlists { get; } = new();

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _isLoading = false;

    public PlaylistViewModel(IMusicLibraryService musicLibrary, MusicDatabase db)
    {
        _musicLibrary = musicLibrary;
        _db = db;
    }

    public void MarkDirty() => _isDirty = true;

    /// <summary>
    /// 加载播放列表（含3个系统歌单 + 用户歌单）
    /// </summary>
    [RelayCommand]
    public async Task LoadPlaylistsAsync()
    {
        IsLoading = true;
        StatusText = "加载中...";
        try
        {
            Playlists.Clear();

            var allCountTask = _musicLibrary.GetMergedSongCountAsync();
            var favCountTask = _musicLibrary.GetFavoriteSongCountAsync();
            var recentCountTask = _musicLibrary.GetRecentSongCountAsync();
            var allFirstIdTask = _musicLibrary.GetFirstSongIdForAllAsync();
            var favFirstIdTask = _musicLibrary.GetFirstFavoriteSongIdAsync();
            var recentFirstIdTask = _musicLibrary.GetFirstRecentSongIdAsync();

            await Task.WhenAll(allCountTask, favCountTask, recentCountTask,
                allFirstIdTask, favFirstIdTask, recentFirstIdTask);

            Playlists.Add(new Playlist
            {
                Id = -1, Name = "全部歌曲", SongCount = allCountTask.Result,
                IsSystem = true, CoverSongId = allFirstIdTask.Result, CreatedAt = _epoch
            });
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
            foreach (var p in userPlaylists)
            {
                try
                {
                    var firstSong = await _db.GetFirstSongInPlaylistAsync(p.Id);
                    p.CoverSongId = firstSong?.Id ?? 0;
                }
                catch { p.CoverSongId = 0; }
                Playlists.Add(p);
            }

            _isDirty = false;
            StatusText = Playlists.Count == 0 ? "暂无播放列表" : "";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PlaylistVM] LoadPlaylists failed: {ex}");
            StatusText = "加载失败";
        }
        finally { IsLoading = false; }
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
        if (Playlists.Count < 3) return;
        try
        {
            var allCountTask = _musicLibrary.GetMergedSongCountAsync();
            var favCountTask = _musicLibrary.GetFavoriteSongCountAsync();
            var recentCountTask = _musicLibrary.GetRecentSongCountAsync();
            var allFirstIdTask = _musicLibrary.GetFirstSongIdForAllAsync();
            var favFirstIdTask = _musicLibrary.GetFirstFavoriteSongIdAsync();
            var recentFirstIdTask = _musicLibrary.GetFirstRecentSongIdAsync();

            await Task.WhenAll(allCountTask, favCountTask, recentCountTask,
                allFirstIdTask, favFirstIdTask, recentFirstIdTask);

            UpdateSystemPlaylist(-1, "全部歌曲", allCountTask.Result, allFirstIdTask.Result, _epoch);
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
        var idx = Playlists.IndexOf(existing);
        Playlists[idx] = new Playlist
        {
            Id = id, Name = name, SongCount = songCount,
            IsSystem = true, CoverSongId = coverSongId, CreatedAt = createdAt
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
