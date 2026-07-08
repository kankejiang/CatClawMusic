using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 专辑详情页 ViewModel：加载指定专辑的本地与网络缓存歌曲列表，
/// 提供整专辑播放与单曲播放/暂停等交互能力。
/// </summary>
public partial class AlbumDetailViewModel : ObservableObject
{
    private readonly MusicDatabase _db;
    private readonly IAudioPlayerService? _audioPlayer;
    private readonly PlayQueue? _playQueue;

    /// <summary>当前专辑信息</summary>
    [ObservableProperty]
    private Album? _album;

    /// <summary>专辑封面路径（独立可观察属性，用于在 Album 模型属性变更后刷新绑定）</summary>
    [ObservableProperty]
    private string? _albumCoverPath;

    /// <summary>专辑下的歌曲集合（已合并本地与网络缓存，并按曲目序号排序）</summary>
    [ObservableProperty]
    private ObservableCollection<Song> _songs = new();

    /// <summary>是否正在加载专辑数据</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// 初始化 <see cref="AlbumDetailViewModel"/> 实例。
    /// </summary>
    /// <param name="db">音乐数据库访问对象</param>
    /// <param name="audioPlayer">音频播放服务，可为空（设计时支持）</param>
    /// <param name="playQueue">播放队列，可为空（设计时支持）</param>
    public AlbumDetailViewModel(MusicDatabase db,
        IAudioPlayerService? audioPlayer = null,
        PlayQueue? playQueue = null)
    {
        _db = db;
        _audioPlayer = audioPlayer;
        _playQueue = playQueue;
    }

    /// <summary>
    /// 异步加载指定专辑的详情与歌曲列表。
    /// 合并本地歌曲与缓存网络歌曲，并批量解析封面。
    /// </summary>
    /// <param name="albumTitle">专辑标题</param>
    public async Task LoadAsync(string albumTitle)
    {
        try
        {
            IsLoading = true;

            if (string.IsNullOrWhiteSpace(albumTitle))
                return;

            Album = new Album { Title = albumTitle };

            var allAlbums = await _db.GetAllAlbumsAsync();
            var existingAlbum = allAlbums.FirstOrDefault(a =>
                string.Equals(a.Title, albumTitle, StringComparison.OrdinalIgnoreCase));
            if (existingAlbum != null)
            {
                Album = existingAlbum;
            }

            var localSongs = await _db.GetSongsByAlbumAsync(albumTitle);
            var networkSongs = await _db.GetCachedNetworkSongsAsync();
            var albumNetworkSongs = networkSongs.Where(s =>
                string.Equals(s.Album, albumTitle, StringComparison.OrdinalIgnoreCase));

            var allSongs = localSongs.Concat(albumNetworkSongs)
                .OrderBy(s => s.TrackNumber > 0 ? s.TrackNumber : int.MaxValue)
                .ThenBy(s => s.Title)
                .ToList();

            await Task.Run(() => Services.CoverHelper.BatchResolveCovers(allSongs));

            // 用第一首已解析封面的歌曲回填专辑封面（Album.CoverArtPath 在 DB 中通常为空）
            if (string.IsNullOrEmpty(Album.CoverArtPath))
            {
                var firstWithCover = allSongs.FirstOrDefault(s => !string.IsNullOrEmpty(s.CoverArtPath));
                if (firstWithCover != null)
                    Album.CoverArtPath = firstWithCover.CoverArtPath;
            }
            // 同步到可观察属性以触发 UI 刷新（Album 模型未实现 INPC）
            AlbumCoverPath = Album.CoverArtPath;

            Songs = new ObservableCollection<Song>(allSongs);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AlbumDetailVM] LoadAsync({albumTitle}) failed: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>播放整张专辑：将全部歌曲加入播放队列并从首曲开始播放</summary>
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
    }

    /// <summary>播放指定歌曲：若为当前曲则切换播放/暂停，否则将其设为播放队列当前曲并播放</summary>
    /// <param name="song">要播放的歌曲，为空则忽略</param>
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
        }
    }

    private async Task RecordPlayAsync(Song song)
    {
        try { await _db.RecordPlayAsync(song.Id); }
        catch { }
    }
}
