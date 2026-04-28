using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;

namespace CatClawMusic.UI.ViewModels;

public class NowPlayingViewModel : BindableObject
{
    private readonly IAudioPlayerService _audioPlayer;
    private readonly ILyricsService _lyricsService;
    private readonly IMusicLibraryService _musicLibrary;
    private readonly PlayQueue _playQueue;
    private LrcLyrics? _currentLyrics;
    private bool _isPositionUpdating;

    private Song? _currentSong;
    public Song? CurrentSong
    {
        get => _currentSong;
        set { _currentSong = value; OnPropertyChanged(); _ = LoadLyricsAsync(value); _ = LoadCoverAsync(value); UpdateQueuePeek(); }
    }

    public string CoverSource { get; set; } = "";
    public string CurrentLyricLine { get; set; } = "🐾 猫爪音乐";
    public string NextLyricLine { get; set; } = "选择一首歌曲开始播放吧~";
    public TimeSpan CurrentPosition { get; set; }
    public TimeSpan TotalDuration { get; set; }

    public double CurrentPositionSeconds
    {
        get => CurrentPosition.TotalSeconds;
        set { if (!_isPositionUpdating) _ = _audioPlayer.SeekAsync(TimeSpan.FromSeconds(value)); }
    }
    public double TotalDurationSeconds => TotalDuration.TotalSeconds;

    public string PlayPauseIcon { get; set; } = "▶";
    public string PlayModeIcon { get; set; } = "🔁";
    public string LikeIcon { get; set; } = "🤍";

    private bool _isLiked;
    public bool IsLiked { get => _isLiked; set { _isLiked = value; LikeIcon = value ? "❤️" : "🤍"; OnPropertyChanged(nameof(LikeIcon)); } }

    public int Volume { get; set; } = 80;

    public ObservableCollection<Song> UpcomingSongs { get; } = new();
    public string QueueHint { get; set; } = "";

    public Command PlayPauseCommand { get; }
    public Command NextCommand { get; }
    public Command PreviousCommand { get; }
    public Command TogglePlayModeCommand { get; }
    public Command ToggleShuffleCommand { get; }
    public Command ToggleLikeCommand { get; }
    public Command<SwipeDirection> SwipeCommand { get; }

    public NowPlayingViewModel(IAudioPlayerService audioPlayer, ILyricsService lyricsService,
        IMusicLibraryService musicLibrary, PlayQueue playQueue)
    {
        _audioPlayer = audioPlayer;
        _lyricsService = lyricsService;
        _musicLibrary = musicLibrary;
        _playQueue = playQueue;
        Volume = _audioPlayer.Volume;

        PlayPauseCommand = new Command(OnPlayPause);
        NextCommand = new Command(OnNext);
        PreviousCommand = new Command(OnPrevious);
        TogglePlayModeCommand = new Command(OnTogglePlayMode);
        ToggleShuffleCommand = new Command(OnToggleShuffle);
        ToggleLikeCommand = new Command(OnToggleLike);
        SwipeCommand = new Command<SwipeDirection>(OnSwipe);

        _audioPlayer.StateChanged += OnPlaybackStateChanged;
        _audioPlayer.PositionChanged += OnPositionChanged;
    }

    private async Task LoadCoverAsync(Song? song)
    {
        CoverSource = "";
        if (song == null) return;
        try
        {
            var stream = await _musicLibrary.GetAlbumCoverAsync(song);
            if (stream != null)
            {
                var cacheDir = Path.Combine(FileSystem.CacheDirectory, "covers");
                Directory.CreateDirectory(cacheDir);
                var coverPath = Path.Combine(cacheDir, $"cover_{song.Id}.jpg");
                using (var fs = File.Create(coverPath)) await stream.CopyToAsync(fs);
                MainThread.BeginInvokeOnMainThread(() => { CoverSource = coverPath; OnPropertyChanged(nameof(CoverSource)); });
            }
        }
        catch { }
    }

    private void UpdateQueuePeek()
    {
        UpcomingSongs.Clear();
        var upcoming = _playQueue.GetUpcomingSongs(3);
        foreach (var s in upcoming) UpcomingSongs.Add(s);
        QueueHint = UpcomingSongs.Count > 0 ? $"下一首: {UpcomingSongs[0].Title}" : "";
        OnPropertyChanged(nameof(QueueHint));
        OnPropertyChanged(nameof(UpcomingSongs));
    }

    private void OnSwipe(SwipeDirection direction)
    {
        if (direction == SwipeDirection.Left) OnNext();
        else if (direction == SwipeDirection.Right) OnPrevious();
    }

    private void OnPlaybackStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        if (e.State == PlaybackState.Stopped) MainThread.BeginInvokeOnMainThread(() => OnNext());
        else if (e.State == PlaybackState.Playing) MainThread.BeginInvokeOnMainThread(() => { PlayPauseIcon = "⏸"; OnPropertyChanged(nameof(PlayPauseIcon)); });
        else if (e.State is PlaybackState.Paused or PlaybackState.Error) MainThread.BeginInvokeOnMainThread(() => { PlayPauseIcon = "▶"; OnPropertyChanged(nameof(PlayPauseIcon)); });
    }

    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _isPositionUpdating = true;
            CurrentPosition = position;
            TotalDuration = _audioPlayer.Duration;

            if (_currentLyrics?.Lines is { Count: > 0 })
            {
                var idx = _lyricsService.GetCurrentLyricIndex(_currentLyrics, position);
                if (idx >= 0 && idx < _currentLyrics.Lines.Count)
                {
                    CurrentLyricLine = _currentLyrics.Lines[idx].Text;
                    NextLyricLine = idx + 1 < _currentLyrics.Lines.Count ? _currentLyrics.Lines[idx + 1].Text : "";
                }
            }

            OnPropertyChanged(nameof(CurrentPosition));
            OnPropertyChanged(nameof(CurrentPositionSeconds));
            OnPropertyChanged(nameof(TotalDuration));
            OnPropertyChanged(nameof(TotalDurationSeconds));
            OnPropertyChanged(nameof(CurrentLyricLine));
            OnPropertyChanged(nameof(NextLyricLine));
            _isPositionUpdating = false;
        });
    }

    private async Task LoadLyricsAsync(Song? song)
    {
        if (song == null) { CurrentLyricLine = "🐾 猫爪音乐"; NextLyricLine = "选择一首歌曲开始播放吧~"; _currentLyrics = null; OnPropertyChanged(nameof(CurrentLyricLine)); OnPropertyChanged(nameof(NextLyricLine)); return; }
        CurrentLyricLine = "正在加载歌词..."; NextLyricLine = ""; OnPropertyChanged(nameof(CurrentLyricLine));
        _currentLyrics = await _lyricsService.GetLyricsAsync(song);
        if (_currentLyrics == null) { CurrentLyricLine = "暂无歌词"; NextLyricLine = ""; }
        OnPropertyChanged(nameof(CurrentLyricLine)); OnPropertyChanged(nameof(NextLyricLine));
    }

    private void OnPlayPause()
    {
        if (_audioPlayer.IsPlaying) _audioPlayer.PauseAsync();
        else if (CurrentSong != null) _audioPlayer.PlayAsync(CurrentSong.FilePath);
    }

    private void OnNext() { var s = _playQueue.Next(); if (s != null) { CurrentSong = s; _ = _audioPlayer.PlayAsync(s.FilePath); } }
    private void OnPrevious() { var s = _playQueue.Previous(); if (s != null) { CurrentSong = s; _ = _audioPlayer.PlayAsync(s.FilePath); } }

    private void OnTogglePlayMode()
    {
        _playQueue.PlayMode = _playQueue.PlayMode switch { PlayMode.Sequential => PlayMode.Shuffle, PlayMode.Shuffle => PlayMode.SingleRepeat, PlayMode.SingleRepeat => PlayMode.ListRepeat, PlayMode.ListRepeat => PlayMode.Sequential, _ => PlayMode.Sequential };
        PlayModeIcon = _playQueue.PlayMode switch { PlayMode.Sequential => "➡️", PlayMode.Shuffle => "🔀", PlayMode.SingleRepeat => "🔂", PlayMode.ListRepeat => "🔁", _ => "🔁" };
        OnPropertyChanged(nameof(PlayModeIcon));
    }

    private void OnToggleShuffle() { _playQueue.PlayMode = _playQueue.PlayMode == PlayMode.Shuffle ? PlayMode.Sequential : PlayMode.Shuffle; PlayModeIcon = _playQueue.PlayMode == PlayMode.Shuffle ? "🔀" : "➡️"; OnPropertyChanged(nameof(PlayModeIcon)); }
    private void OnToggleLike() => IsLiked = !IsLiked;
}
