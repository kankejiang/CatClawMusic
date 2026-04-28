using System.Collections.ObjectModel;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services;

namespace CatClawMusic.UI.ViewModels;

public class PlaylistDetailViewModel : BindableObject
{
    private readonly IMusicLibraryService _musicLibrary;
    private readonly IAudioPlayerService? _audioPlayer;
    private readonly PlayQueue? _playQueue;

    public ObservableCollection<Song> Songs { get; } = new();
    public string PlaylistName { get; set; } = "";
    public string StatusText { get; set; } = "";

    private int _playlistId;

    public PlaylistDetailViewModel(IMusicLibraryService musicLibrary, IAudioPlayerService? audioPlayer = null, PlayQueue? playQueue = null)
    {
        _musicLibrary = musicLibrary;
        _audioPlayer = audioPlayer;
        _playQueue = playQueue;
    }

    public async Task LoadAsync(int playlistId, string name)
    {
        _playlistId = playlistId;
        PlaylistName = name;
        OnPropertyChanged(nameof(PlaylistName));

        StatusText = "加载中...";
        OnPropertyChanged(nameof(StatusText));

        try
        {
            var allSongs = await _musicLibrary.GetAllSongsAsync();
            Songs.Clear();
            // TODO: 从 PlaylistSong 表加载关联歌曲
            foreach (var s in allSongs.Take(50))
                Songs.Add(s);

            StatusText = Songs.Count > 0 ? $"共 {Songs.Count} 首" : "暂无歌曲";
        }
        catch
        {
            StatusText = "加载失败";
        }
        OnPropertyChanged(nameof(StatusText));
    }

    public async Task PlaySongAsync(Song song)
    {
        if (_audioPlayer == null || _playQueue == null) return;
        _playQueue.SetSongs(Songs);
        _playQueue.SelectSong(song.Id);
        if (!string.IsNullOrEmpty(song.FilePath))
            await _audioPlayer.PlayAsync(song.FilePath);
        await Shell.Current.GoToAsync("NowPlayingPage");
    }
}
