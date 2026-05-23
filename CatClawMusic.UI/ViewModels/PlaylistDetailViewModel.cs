using CatClawMusic.Core.Models;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services;
using CatClawMusic.UI.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CatClawMusic.UI.ViewModels;

public partial class PlaylistDetailViewModel : ObservableObject
{
    private readonly IMusicLibraryService _musicLibrary;
    private readonly IAudioPlayerService? _audioPlayer;
    private readonly PlayQueue? _playQueue;
    private readonly INavigationService _navigationService;
    private readonly IServiceProvider? _serviceProvider;
    private Data.MusicDatabase? _db;

    public BatchObservableCollection<Song> Songs { get; } = new();

    /// <summary>
    /// 歌单名称
    /// </summary>
    [ObservableProperty] private string _playlistName = "";
    /// <summary>
    /// 状态文本
    /// </summary>
    [ObservableProperty] private string _statusText = "";

    private int _playlistId;

    /// <summary>
    /// 初始化歌单详情ViewModel
    /// </summary>
    public PlaylistDetailViewModel(IMusicLibraryService musicLibrary, IAudioPlayerService? audioPlayer = null,
        PlayQueue? playQueue = null, INavigationService? navigationService = null, IServiceProvider? serviceProvider = null)
    {
        _musicLibrary = musicLibrary;
        _audioPlayer = audioPlayer;
        _playQueue = playQueue;
        _navigationService = navigationService!;
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

    public async Task LoadAsync(int playlistId, string name)
    {
        _playlistId = playlistId;
        PlaylistName = name;
        StatusText = "加载中...";
        try
        {
            List<Song> songs;

            switch (playlistId)
            {
                case -1:
                    songs = await _musicLibrary.GetMergedSongsAsync();
                    break;
                case -2:
                    songs = await _musicLibrary.GetFavoriteSongsAsync();
                    break;
                case -3:
                    songs = await _musicLibrary.GetRecentSongsAsync();
                    break;
                default:
                    songs = await _musicLibrary.GetPlaylistSongsAsync(playlistId);
                    break;
            }

            if (playlistId != -1)
            {
                var enabledProtocols = await GetDb().GetEnabledProtocolsAsync();
                songs = GetDb().FilterByEnabledProtocols(songs, enabledProtocols);
            }

            Songs.ReplaceAll(songs);
            StatusText = Songs.Count > 0 ? $"共 {Songs.Count} 首" : "暂无歌曲";
        }
        catch { StatusText = "加载失败"; }
    }

    /// <summary>
    /// 播放指定歌曲，如果已在播放中则切换暂停/恢复
    /// </summary>
    public async Task PlaySongAsync(Song song)
    {
        if (_audioPlayer == null || _playQueue == null) return;

        var currentSongInQueue = _playQueue.CurrentSong;
        if (currentSongInQueue != null && currentSongInQueue.Id == song.Id)
        {
            if (_audioPlayer.IsPlaying)
            {
                await _audioPlayer.PauseAsync();
            }
            else
            {
                await _audioPlayer.ResumeAsync();
            }
        }
        else
        {
            _playQueue.SetSongs(Songs);
            _playQueue.SelectSong(song.Id);
            if (!string.IsNullOrEmpty(song.FilePath))
                await _audioPlayer.PlayAsync(song.FilePath);
            _navigationService.PushFragment("NowPlaying");
        }
    }

    /// <summary>
    /// 检查歌曲是否已收藏
    /// </summary>
    public async Task<bool> IsFavoriteAsync(int songId)
    {
        try { return await GetDb().IsFavoriteAsync(songId); }
        catch { return false; }
    }

    /// <summary>
    /// 切换歌曲收藏状态
    /// </summary>
    public async Task ToggleFavoriteAsync(int songId, bool isFav)
    {
        await GetDb().SetFavoriteAsync(songId, isFav);
    }

    /// <summary>
    /// 将歌曲添加到指定歌单
    /// </summary>
    public async Task AddSongToPlaylistAsync(int targetPlaylistId, int songId)
    {
        await _musicLibrary.AddSongToPlaylistAsync(targetPlaylistId, songId);
    }

    /// <summary>
    /// 从当前歌单中移除歌曲
    /// </summary>
    /// <param name="songId">歌曲ID</param>
    public async Task RemoveSongFromPlaylistAsync(int songId)
    {
        if (_playlistId > 0)
            await _musicLibrary.RemoveSongFromPlaylistAsync(_playlistId, songId);
    }
}
