using System.Collections.ObjectModel;
using System.Linq;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CatClawMusic.UI.ViewModels;

/// <summary>
/// 歌单列表ViewModel，管理系统歌单和用户歌单的加载、创建和删除
/// </summary>
public partial class PlaylistViewModel : ObservableObject
{
    private readonly IMusicLibraryService _musicLibrary;
    private readonly INavigationService _navigationService;
    private readonly IServiceProvider? _serviceProvider;
    private Data.MusicDatabase? _db;

    /// <summary>
    /// 歌单列表
    /// </summary>
    public ObservableCollection<Playlist> Playlists { get; } = new();

    /// <summary>
    /// 状态文本
    /// </summary>
    [ObservableProperty]
    private string _statusText = "";

    private static readonly long _epoch = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    /// <summary>
    /// 初始化歌单列表ViewModel
    /// </summary>
    public PlaylistViewModel(IMusicLibraryService musicLibrary, INavigationService navigationService,
        IServiceProvider? serviceProvider = null)
    {
        _musicLibrary = musicLibrary;
        _navigationService = navigationService;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// 获取数据库实例（延迟初始化）
    /// </summary>
    private Data.MusicDatabase GetDb()
    {
        if (_db == null)
            _db = (_serviceProvider ?? MainApplication.Services).GetService(typeof(Data.MusicDatabase)) as Data.MusicDatabase
                ?? MainApplication.Services.GetService(typeof(Data.MusicDatabase)) as Data.MusicDatabase;
        return _db!;
    }

    /// <summary>
    /// 异步加载所有歌单（系统歌单 + 用户歌单）
    /// </summary>
    public async Task LoadPlaylistsAsync()
    {
        StatusText = "加载中...";
        try
        {
            Playlists.Clear();

            var allSongs = await _musicLibrary.GetMergedSongsAsync();
            Playlists.Add(new Playlist
            {
                Id = -1,
                Name = "全部歌曲",
                SongCount = allSongs.Count,
                IsSystem = true,
                CoverSongId = allSongs.FirstOrDefault()?.Id ?? 0,
                CreatedAt = _epoch
            });

            var favSongs = await _musicLibrary.GetFavoriteSongsAsync();
            Playlists.Add(new Playlist
            {
                Id = -2,
                Name = "收藏歌曲",
                SongCount = favSongs.Count,
                IsSystem = true,
                CoverSongId = favSongs.FirstOrDefault()?.Id ?? 0,
                CreatedAt = _epoch + 1
            });

            var recentSongs = await _musicLibrary.GetRecentSongsAsync();
            Playlists.Add(new Playlist
            {
                Id = -3,
                Name = "最近播放",
                SongCount = recentSongs.Count,
                IsSystem = true,
                CoverSongId = recentSongs.FirstOrDefault()?.Id ?? 0,
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
        await LoadPlaylistsAsync();
        return id;
    }

    /// <summary>
    /// 删除指定歌单并刷新列表
    /// </summary>
    public async Task DeletePlaylistAsync(int playlistId)
    {
        await _musicLibrary.DeletePlaylistAsync(playlistId);
        await LoadPlaylistsAsync();
    }

    /// <summary>
    /// 将歌曲添加到指定歌单
    /// </summary>
    /// <param name="playlistId">目标歌单ID</param>
    /// <param name="songId">歌曲ID</param>
    public async Task AddSongToPlaylistAsync(int playlistId, int songId)
    {
        await _musicLibrary.AddSongToPlaylistAsync(playlistId, songId);
        await LoadPlaylistsAsync();
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
