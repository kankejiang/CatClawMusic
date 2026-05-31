using System.Collections.ObjectModel;
using System.Linq;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CatClawMusic.UI.ViewModels;

public partial class PlaylistViewModel : ObservableObject
{
    private readonly IMusicLibraryService _musicLibrary;
    private readonly INavigationService _navigationService;
    private readonly IServiceProvider? _serviceProvider;
    private Data.MusicDatabase? _db;
    private bool _isDirty = true;

    public ObservableCollection<Playlist> Playlists { get; } = new();

    [ObservableProperty]
    private string _statusText = "";

    private static readonly long _epoch = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    public PlaylistViewModel(IMusicLibraryService musicLibrary, INavigationService navigationService,
        IServiceProvider? serviceProvider = null)
    {
        _musicLibrary = musicLibrary;
        _navigationService = navigationService;
        _serviceProvider = serviceProvider;
    }

    private Data.MusicDatabase GetDb()
    {
        if (_db == null)
            _db = (_serviceProvider ?? MainApplication.Services).GetService(typeof(Data.MusicDatabase)) as Data.MusicDatabase
                ?? MainApplication.Services.GetService(typeof(Data.MusicDatabase)) as Data.MusicDatabase;
        return _db!;
    }

    public void MarkDirty() => _isDirty = true;

    public async Task RefreshIfChangedAsync()
    {
        if (!_isDirty && Playlists.Count > 0)
            return;
        _isDirty = false;
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

            var latest = new List<Playlist>
            {
                new Playlist
                {
                    Id = -1, Name = "全部歌曲", SongCount = allCountTask.Result,
                    IsSystem = true, CoverSongId = allFirstIdTask.Result, CreatedAt = _epoch
                },
                new Playlist
                {
                    Id = -2, Name = "收藏歌曲", SongCount = favCountTask.Result,
                    IsSystem = true, CoverSongId = favFirstIdTask.Result, CreatedAt = _epoch + 1
                },
                new Playlist
                {
                    Id = -3, Name = "最近播放", SongCount = recentCountTask.Result,
                    IsSystem = true, CoverSongId = recentFirstIdTask.Result, CreatedAt = _epoch + 2
                },
            };

            var userPlaylists = await _musicLibrary.GetAllPlaylistsAsync();
            var coverTasks = userPlaylists.Select(async p =>
            {
                try
                {
                    var firstSong = await GetDb().GetFirstSongInPlaylistAsync(p.Id);
                    p.CoverSongId = firstSong?.Id ?? 0;
                }
                catch { p.CoverSongId = 0; }
            }).ToArray();
            await Task.WhenAll(coverTasks);
            foreach (var p in userPlaylists)
                latest.Add(p);

            if (!HasPlaylistsChanged(latest))
                return;

            ApplyLatestPlaylists(latest);
            StatusText = "";
        }
        catch { }
    }

    private bool HasPlaylistsChanged(List<Playlist> latest)
    {
        if (Playlists.Count != latest.Count) return true;
        for (int i = 0; i < Playlists.Count; i++)
        {
            var c = Playlists[i];
            var l = latest[i];
            if (c.Id != l.Id || c.SongCount != l.SongCount
                || c.CoverSongId != l.CoverSongId || c.Name != l.Name)
                return true;
        }
        return false;
    }

    private void ApplyLatestPlaylists(List<Playlist> latest)
    {
        var latestIds = new HashSet<int>(latest.Select(p => p.Id));

        for (int i = Playlists.Count - 1; i >= 0; i--)
        {
            if (!latestIds.Contains(Playlists[i].Id))
                Playlists.RemoveAt(i);
        }

        var currentIds = new HashSet<int>(Playlists.Select(p => p.Id));
        for (int i = 0; i < latest.Count; i++)
        {
            var lat = latest[i];
            if (i < Playlists.Count && Playlists[i].Id == lat.Id)
            {
                if (Playlists[i].SongCount != lat.SongCount
                    || Playlists[i].CoverSongId != lat.CoverSongId
                    || Playlists[i].Name != lat.Name)
                {
                    Playlists[i] = lat;
                }
            }
            else if (currentIds.Contains(lat.Id))
            {
                var idx = Playlists.ToList().FindIndex(p => p.Id == lat.Id);
                if (idx >= 0)
                {
                    if (Playlists[idx].SongCount != lat.SongCount
                        || Playlists[idx].CoverSongId != lat.CoverSongId
                        || Playlists[idx].Name != lat.Name)
                    {
                        Playlists[idx] = lat;
                    }
                }
            }
            else
            {
                Playlists.Insert(i, lat);
                currentIds.Add(lat.Id);
            }
        }

        while (Playlists.Count > latest.Count)
            Playlists.RemoveAt(Playlists.Count - 1);
    }

    public async Task LoadPlaylistsAsync()
    {
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
                Id = -1,
                Name = "全部歌曲",
                SongCount = allCountTask.Result,
                IsSystem = true,
                CoverSongId = allFirstIdTask.Result,
                CreatedAt = _epoch
            });

            Playlists.Add(new Playlist
            {
                Id = -2,
                Name = "收藏歌曲",
                SongCount = favCountTask.Result,
                IsSystem = true,
                CoverSongId = favFirstIdTask.Result,
                CreatedAt = _epoch + 1
            });

            Playlists.Add(new Playlist
            {
                Id = -3,
                Name = "最近播放",
                SongCount = recentCountTask.Result,
                IsSystem = true,
                CoverSongId = recentFirstIdTask.Result,
                CreatedAt = _epoch + 2
            });

            var playlists = await _musicLibrary.GetAllPlaylistsAsync();
            foreach (var p in playlists)
            {
                var firstSong = await GetDb().GetFirstSongInPlaylistAsync(p.Id);
                p.CoverSongId = firstSong?.Id ?? 0;
                Playlists.Add(p);
            }

            StatusText = Playlists.Count == 0 ? "" : "";
        }
        catch { StatusText = "加载失败"; }
    }

    /// <summary>
    /// 创建新歌单并刷新列表
    /// </summary>
    public async Task<int> CreatePlaylistAsync(string name)
    {
        int id = await _musicLibrary.CreatePlaylistAsync(name);
        _isDirty = true;
        await RefreshIfChangedAsync();
        return id;
    }

    /// <summary>
    /// 删除指定歌单并刷新列表
    /// </summary>
    public async Task DeletePlaylistAsync(int playlistId)
    {
        await _musicLibrary.DeletePlaylistAsync(playlistId);
        _isDirty = true;
        await RefreshIfChangedAsync();
    }

    /// <summary>
    /// 将歌曲添加到指定歌单
    /// </summary>
    /// <param name="playlistId">目标歌单ID</param>
    /// <param name="songId">歌曲ID</param>
    public async Task AddSongToPlaylistAsync(int playlistId, int songId)
    {
        await _musicLibrary.AddSongToPlaylistAsync(playlistId, songId);
        _isDirty = true;
    }

    /// <summary>
    /// 检查歌曲是否已收藏
    /// </summary>
    /// <param name="songId">歌曲ID</param>
    /// <returns>是否已收藏</returns>
    public async Task<bool> IsFavoriteAsync(int songId)
    {
        try { return await GetDb().IsFavoriteAsync(songId); }
        catch { return false; }
    }

    /// <summary>
    /// 切换歌曲收藏状态
    /// </summary>
    /// <param name="songId">歌曲ID</param>
    /// <param name="isFav">是否收藏</param>
    public async Task ToggleFavoriteAsync(int songId, bool isFav)
    {
        await GetDb().SetFavoriteAsync(songId, isFav);
    }

    /// <summary>
    /// 导航到歌单详情页面
    /// </summary>
    public void NavigateToPlaylist(int playlistId, string name)
    {
        _navigationService.PushFragment("PlaylistDetail", new Dictionary<string, object>
        {
            ["playlistId"] = playlistId,
            ["playlistName"] = name
        });
    }
}
