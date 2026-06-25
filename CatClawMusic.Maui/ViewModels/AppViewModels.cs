using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CatClawMusic.Maui.ViewModels;

public partial class NowPlayingViewModel : ObservableObject
{
    private readonly PlayQueue _queue;
    private readonly ILyricsService _lyrics;
    private readonly MusicDatabase _db;

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _artist = "";
    [ObservableProperty] private string _album = "";
    [ObservableProperty] private string _currentLyric = "";
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private double _duration;
    [ObservableProperty] private double _volume = 1.0;

    public Song? CurrentSong => _queue.CurrentSong;

    public NowPlayingViewModel(PlayQueue queue, ILyricsService lyrics, MusicDatabase db)
    {
        _queue = queue;
        _lyrics = lyrics;
        _db = db;
    }
}

public partial class LibraryViewModel : ObservableObject
{
    private readonly MusicDatabase _db;

    [ObservableProperty] private List<Song> _songs = new();
    [ObservableProperty] private bool _isLoading;

    public LibraryViewModel(MusicDatabase db) { _db = db; }
    public async Task LoadAsync() { Songs = await _db.GetSongsWithDetailsAsync(); }
}
