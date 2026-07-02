using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

public partial class PlaylistDetailViewModel : ObservableObject
{
    private readonly IMusicLibraryService _musicLibrary;
    private readonly IAudioPlayerService? _audioPlayer;
    private readonly PlayQueue? _playQueue;
    private readonly MusicDatabase _db;

    public ObservableCollection<Song> Songs { get; } = new();

    private List<Song> _allSongsRaw = new();
    private int _playlistId;

    [ObservableProperty]
    private Playlist _playlist = new();

    [ObservableProperty]
    private string _playlistName = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _isLoading = false;

    public event Action<Song>? SongPlayRequested;

    public PlaylistDetailViewModel(IMusicLibraryService musicLibrary,
        MusicDatabase db,
        IAudioPlayerService? audioPlayer = null,
        PlayQueue? playQueue = null)
    {
        _musicLibrary = musicLibrary;
        _db = db;
        _audioPlayer = audioPlayer;
        _playQueue = playQueue;
    }

    /// <summary>
    /// 设置歌单参数并加载
    /// </summary>
    public async Task LoadPlaylistAsync(int playlistId, string name)
    {
        _playlistId = playlistId;
        PlaylistName = name;
        IsLoading = true;
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

            var enabledProtocols = await _db.GetEnabledProtocolsAsync();
            _allSongsRaw = _db.FilterByEnabledProtocols(songs, enabledProtocols);

            Songs.Clear();
            foreach (var s in _allSongsRaw)
                Songs.Add(s);

            // 同步更新 Playlist 对象，让顶部"共 X 首歌曲"显示正确
            Playlist = new Playlist
            {
                Id = playlistId,
                Name = name,
                SongCount = Songs.Count
            };

            StatusText = Songs.Count > 0 ? $"共 {Songs.Count} 首" : "暂无歌曲";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PlaylistDetailVM] LoadAsync({playlistId}) failed: {ex}");
            StatusText = "加载失败";
        }
        finally { IsLoading = false; }
    }

    /// <summary>
    /// 播放歌曲
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
            _playQueue.SetSongs(Songs);
            _playQueue.SelectSong(song.Id);
            if (!string.IsNullOrEmpty(song.FilePath))
                await _audioPlayer.PlayAsync(song.FilePath);
            _ = RecordPlayAsync(song);
            SongPlayRequested?.Invoke(song);
        }
    }

    /// <summary>
    /// 播放全部
    /// </summary>
    [RelayCommand]
    public async Task PlayAllAsync()
    {
        if (_audioPlayer == null || _playQueue == null || Songs.Count == 0) return;
        _playQueue.SetSongs(Songs);
        var first = Songs[0];
        _playQueue.SelectSong(first.Id);
        if (!string.IsNullOrEmpty(first.FilePath))
            await _audioPlayer.PlayAsync(first.FilePath);
        _ = RecordPlayAsync(first);
        SongPlayRequested?.Invoke(first);
    }

    /// <summary>
    /// 从歌单移除歌曲
    /// </summary>
    [RelayCommand]
    public async Task RemoveSongAsync(Song? song)
    {
        if (song == null || _playlistId <= 0) return;
        await RemoveSongsFromPlaylistAsync(new[] { song.Id });
    }

    /// <summary>
    /// 批量移除歌曲
    /// </summary>
    public async Task<int> RemoveSongsFromPlaylistAsync(IEnumerable<int> songIds)
    {
        if (_playlistId <= 0) return 0;
        var ids = songIds.ToHashSet();
        if (ids.Count == 0) return 0;

        await _musicLibrary.RemoveSongsFromPlaylistAsync(_playlistId, ids);

        var toRemove = Songs.Where(s => ids.Contains(s.Id)).ToList();
        foreach (var s in toRemove) Songs.Remove(s);
        _allSongsRaw = Songs.ToList();
        StatusText = Songs.Count > 0 ? $"共 {Songs.Count} 首" : "暂无歌曲";
        return ids.Count;
    }

    /// <summary>
    /// 切换收藏
    /// </summary>
    public async Task ToggleFavoriteAsync(int songId, bool isFav)
    {
        await _db.SetFavoriteAsync(songId, isFav);
    }

    /// <summary>
    /// 按来源筛选
    /// </summary>
    public void ApplySourceFilter(string filter)
    {
        var filtered = filter switch
        {
            "local" => _allSongsRaw.Where(s => s.Source == SongSource.Local).ToList(),
            "network" => _allSongsRaw.Where(s => s.Source != SongSource.Local).ToList(),
            _ => _allSongsRaw.ToList()
        };
        Songs.Clear();
        foreach (var s in filtered) Songs.Add(s);
        StatusText = Songs.Count > 0 ? $"共 {Songs.Count} 首" : "暂无歌曲";
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
