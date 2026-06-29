using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Maui.ViewModels;

public partial class NowPlayingViewModel : ObservableObject
{
    private readonly PlayQueue _queue;
    private readonly ILyricsService _lyrics;
    private readonly MusicDatabase _db;
    private readonly IAudioPlayerService _audioService;

    // === Observable Properties (must be fields, not properties) ===
    
    [ObservableProperty]
    private string _title = "";
    
    [ObservableProperty]
    private string _artist = "";
    
    [ObservableProperty]
    private string _album = "";
    
    [ObservableProperty]
    private string _albumArtUrl = "";
    
    [ObservableProperty]
    private string _currentLyric = "";
    
    [ObservableProperty]
    private bool _isPlaying;
    
    [ObservableProperty]
    private double _progress;
    
    [ObservableProperty]
    private double _duration;
    
    [ObservableProperty]
    private double _volume = 1.0;
    
    // === New UI properties ===
    
    [ObservableProperty]
    private bool _hasLyrics;
    
    [ObservableProperty]
    private bool _hasAlbum;
    
    [ObservableProperty]
    private string _currentTimeDisplay = "0:00";
    
    [ObservableProperty]
    private string _totalTimeDisplay = "0:00";
    
    [ObservableProperty]
    private string _playPauseIcon = "▶";
    
    [ObservableProperty]
    private Color _shuffleColor = Colors.White;
    
    [ObservableProperty]
    private Color _repeatColor = Colors.White;
    
    [ObservableProperty]
    private bool _isLiked;
    
    [ObservableProperty]
    private int _shuffleMode; // 0=off, 1=on
    
    [ObservableProperty]
    private int _repeatMode; // 0=off, 1=repeat-all, 2=repeat-one

    public Song? CurrentSong => _queue.CurrentSong;

    public NowPlayingViewModel(PlayQueue queue, ILyricsService lyrics, MusicDatabase db, IAudioPlayerService audioService)
    {
        _queue = queue;
        _lyrics = lyrics;
        _db = db;
        _audioService = audioService;
        
        // Subscribe to audio service events
        _audioService.PlaybackStateChanged += (s, isPlaying) => IsPlaying = isPlaying;
        _audioService.PlaybackCompleted += (s, e) => { _queue.Next(); UpdateCurrentSong(); };
        
        // Initialize commands
        TogglePlayPauseCommand = new AsyncRelayCommand(TogglePlayPauseAsync);
        PlayNextCommand = new AsyncRelayCommand(PlayNextAsync);
        PlayPreviousCommand = new AsyncRelayCommand(PlayPreviousAsync);
        ToggleShuffleCommand = new RelayCommand(ToggleShuffle);
        ToggleRepeatCommand = new RelayCommand(ToggleRepeat);
        ToggleLikeCommand = new RelayCommand(ToggleLike);
    }
    
    // === Commands ===
    
    public IRelayCommand TogglePlayPauseCommand { get; }
    public IRelayCommand PlayNextCommand { get; }
    public IRelayCommand PlayPreviousCommand { get; }
    public IRelayCommand ToggleShuffleCommand { get; }
    public IRelayCommand ToggleRepeatCommand { get; }
    public IRelayCommand ToggleLikeCommand { get; }
    
    private async Task TogglePlayPauseAsync()
    {
        if (IsPlaying)
        {
            await _audioService.PauseAsync();
        }
        else
        {
            if (_queue.CurrentSong != null)
            {
                await _audioService.PlayAsync(_queue.CurrentSong.FilePath);
            }
        }
        PlayPauseIcon = IsPlaying ? "⏸" : "▶";
        await Task.CompletedTask;
    }
    
    private async Task PlayNextAsync()
    {
        _queue.Next();
        UpdateCurrentSong();
        await Task.CompletedTask;
    }
    
    private async Task PlayPreviousAsync()
    {
        _queue.Previous();
        UpdateCurrentSong();
        await Task.CompletedTask;
    }
    
    private void ToggleShuffle()
    {
        if (_queue.PlayMode == PlayMode.Shuffle)
        {
            _queue.PlayMode = PlayMode.ListRepeat;
            ShuffleMode = 0;
        }
        else
        {
            _queue.EnableShuffle();
            ShuffleMode = 1;
        }
        ShuffleColor = ShuffleMode == 1 ? Colors.Green : Colors.White;
    }
    
    private void ToggleRepeat()
    {
        RepeatMode = (RepeatMode + 1) % 3;
        _queue.PlayMode = RepeatMode switch
        {
            0 => PlayMode.ListRepeat,
            1 => PlayMode.ListRepeat,
            2 => PlayMode.SingleRepeat,
            _ => PlayMode.ListRepeat
        };
        RepeatColor = RepeatMode > 0 ? Colors.Green : Colors.White;
    }
    
    private void ToggleLike()
    {
        IsLiked = !IsLiked;
        // TODO: Save to database
    }
    
    // === Public Methods ===
    
    public void UpdateCurrentSong()
    {
        if (_queue.CurrentSong is Song song)
        {
            Title = song.Title;
            Artist = song.Artist;
            Album = song.Album;
            HasAlbum = !string.IsNullOrEmpty(song.Album);
            Duration = song.Duration;
            TotalTimeDisplay = FormatTime(song.Duration);
            // TODO: Load album art, lyrics, etc.
        }
    }
    
    public void UpdateProgress(double currentTime)
    {
        Progress = currentTime;
        CurrentTimeDisplay = FormatTime(currentTime);
    }
    
    private static string FormatTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
    }
}

// LibraryViewModel has been moved to LibraryViewModel.cs
