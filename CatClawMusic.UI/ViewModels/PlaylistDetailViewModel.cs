using System.Collections.ObjectModel;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services;
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

    public ObservableCollection<Song> Songs { get; } = new();

    [ObservableProperty] private string _playlistName = "";
    [ObservableProperty] private string _statusText = "";

    private int _playlistId;

    public PlaylistDetailViewModel(IMusicLibraryService musicLibrary, IAudioPlayerService? audioPlayer = null,
        PlayQueue? playQueue = null, INavigationService? navigationService = null, IServiceProvider? serviceProvider = null)
    {
        _musicLibrary = musicLibrary;
        _audioPlayer = audioPlayer;
        _playQueue = playQueue;
        _navigationService = navigationService!;
        _serviceProvider = serviceProvider;
    }

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

            Songs.Clear();
            foreach (var s in songs) Songs.Add(s);
            StatusText = Songs.Count > 0 ? $"共 {Songs.Count} 首" : "暂无歌曲";
        }
        catch { StatusText = "加载失败"; }
    }

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

    public async Task<bool> IsFavoriteAsync(int songId)
    {
        try { return await GetDb().IsFavoriteAsync(songId); }
        catch { return false; }
    }

    public async Task ToggleFavoriteAsync(int songId, bool isFav)
    {
        await GetDb().SetFavoriteAsync(songId, isFav);
    }

    public async Task AddSongToPlaylistAsync(int targetPlaylistId, int songId)
    {
        await _musicLibrary.AddSongToPlaylistAsync(targetPlaylistId, songId);
    }
}
