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

    public ObservableCollection<Song> Songs { get; } = new();

    [ObservableProperty] private string _playlistName = "";
    [ObservableProperty] private string _statusText = "";

    private int _playlistId;

    public PlaylistDetailViewModel(IMusicLibraryService musicLibrary, IAudioPlayerService? audioPlayer = null, PlayQueue? playQueue = null, INavigationService? navigationService = null)
    {
        _musicLibrary = musicLibrary;
        _audioPlayer = audioPlayer;
        _playQueue = playQueue;
        _navigationService = navigationService!;
    }

    public async Task LoadAsync(int playlistId, string name)
    {
        _playlistId = playlistId;
        PlaylistName = name;
        StatusText = "加载中...";
        try
        {
            // 全部歌曲（Id=3）：本地+网络合并去重
            List<Song> allSongs;
            if (playlistId == 3)
                allSongs = await _musicLibrary.GetMergedSongsAsync();
            else
                allSongs = await _musicLibrary.GetAllSongsAsync();

            Songs.Clear();
            foreach (var s in allSongs) Songs.Add(s);
            StatusText = Songs.Count > 0 ? $"共 {Songs.Count} 首" : "暂无歌曲";
        }
        catch { StatusText = "加载失败"; }
    }

    public async Task PlaySongAsync(Song song)
    {
        if (_audioPlayer == null || _playQueue == null) return;
        _playQueue.SetSongs(Songs);
        _playQueue.SelectSong(song.Id);
        if (!string.IsNullOrEmpty(song.FilePath))
            await _audioPlayer.PlayAsync(song.FilePath);
        _navigationService.PushFragment("NowPlaying");
    }
}
