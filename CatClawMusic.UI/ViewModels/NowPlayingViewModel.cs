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
        set
        {
            _currentSong = value;
            OnPropertyChanged();
            _ = LoadLyricsAsync(value);
            _ = LoadCoverAsync(value);
            UpdateQueuePeek();
        }
    }

    // 封面
    private string _coverSource = "";
    public string CoverSource
    {
        get => _coverSource;
        set { _coverSource = value; OnPropertyChanged(); }
    }

    // 歌词
    public string CurrentLyricLine { get; set; } = "🐾 猫爪音乐";
    public string NextLyricLine { get; set; } = "选择一首歌曲开始播放吧~";

    public TimeSpan CurrentPosition { get; set; }
    public TimeSpan TotalDuration { get; set; }

    public double CurrentPositionSeconds
    {
        get => CurrentPosition.TotalSeconds;
        set
        {
            if (_isPositionUpdating) return;
            _ = _audioPlayer.SeekAsync(TimeSpan.FromSeconds(value));
        }
    }

    public double TotalDurationSeconds => TotalDuration.TotalSeconds;

    // 播放控制
    public string PlayPauseIcon { get; set; } = "▶";
    public string PlayModeIcon { get; set; } = "🔁";
    public string LikeIcon { get; set; } = "🤍";

    private bool _isLiked;
    public bool IsLiked
    {
        get => _isLiked;
        set { _isLiked = value; LikeIcon = value ? "❤️" : "🤍"; OnPropertyChanged(nameof(LikeIcon)); }
    }

    // 音量
    private int _volume = 80;
    public int Volume
    {
        get => _volume;
        set { _volume = value; OnPropertyChanged(); _audioPlayer.Volume = value; }
    }

    // 队列预览
    public ObservableCollection<Song> UpcomingSongs { get; } = new();
    private string _queueHint = "";
    public string QueueHint
    {
        get => _queueHint;
        set { _queueHint = value; OnPropertyChanged(); }
    }

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
        _volume = _audioPlayer.Volume;

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
                // 保存封面到缓存文件，然后用文件路径作为 Source
                var cacheDir = Path.Combine(FileSystem.CacheDirectory, "covers");
                Directory.CreateDirectory(cacheDir);
                var coverPath = Path.Combine(cacheDir, $"cover_{song.Id}_{song.Title?.GetHashCode() ?? 0}.jpg");
                using (var fs = File.Create(coverPath))
                    await stream.CopyToAsync(fs);
                MainThread.BeginInvokeOnMainThread(() => CoverSource = coverPath);
            }
        }
        catch { }
    }

    private void UpdateQueuePeek()
    {
        UpcomingSongs.Clear();
        // TODO: expose upcoming songs from PlayQueue
        QueueHint = UpcomingSongs.Count > 0 ? $"下一首: {UpcomingSongs.Count} 首待播" : "";
        OnPropertyChanged(nameof(QueueHint));
    }

    private void OnSwipe(SwipeDirection direction)
    {
        if (direction == SwipeDirection.Left) OnNext();
        else if (direction == SwipeDirection.Right) OnPrevious();
    }

    private void OnPlaybackStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        if (e.State == PlaybackState.Stopped) { MainThread.BeginInvokeOnMainThread(() => OnNext()); }
        else if (e.State == PlaybackState.Playing) { MainThread.BeginInvokeOnMainThread(() => { PlayPauseIcon = "⏸"; OnPropertyChanged(nameof(PlayPauseIcon)); }); }
        else if (e.State == PlaybackState.Paused || e.State == PlaybackState.Error) { MainThread.BeginInvokeOnMainThread(() => { PlayPauseIcon = "▶"; OnPropertyChanged(nameof(PlayPauseIcon)); }); }
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
        if (song == null) { CurrentLyricLine = "🐾 猫爪音乐"; NextLyricLine = "选择一首歌曲开始播放吧~"; _currentLyrics = null; return; }
        CurrentLyricLine = "正在加载歌词..."; NextLyricLine = "";
        OnPropertyChanged(nameof(CurrentLyricLine)); OnPropertyChanged(nameof(NextLyricLine));
        _currentLyrics = await _lyricsService.GetLyricsAsync(song);
        if (_currentLyrics == null) { CurrentLyricLine = "暂无歌词"; NextLyricLine = ""; }
        OnPropertyChanged(nameof(CurrentLyricLine)); OnPropertyChanged(nameof(NextLyricLine));
    }

    private void OnPlayPause()
    {
        if (_audioPlayer.IsPlaying) _audioPlayer.PauseAsync();
        else if (CurrentSong != null) _audioPlayer.PlayAsync(CurrentSong.FilePath);
    }

    private void OnNext()
    {
        var next = _playQueue.Next();
        if (next != null) { CurrentSong = next; _ = _audioPlayer.PlayAsync(next.FilePath); }
    }

    private void OnPrevious()
    {
        var prev = _playQueue.Previous();
        if (prev != null) { CurrentSong = prev; _ = _audioPlayer.PlayAsync(prev.FilePath); }
    }

    private void OnTogglePlayMode()
    {
        _playQueue.PlayMode = _playQueue.PlayMode switch
        {
            PlayMode.Sequential => PlayMode.Shuffle,
            PlayMode.Shuffle => PlayMode.SingleRepeat,
            PlayMode.SingleRepeat => PlayMode.ListRepeat,
            PlayMode.ListRepeat => PlayMode.Sequential,
            _ => PlayMode.Sequential
        };
        PlayModeIcon = _playQueue.PlayMode switch
        {
            PlayMode.Sequential => "➡️", PlayMode.Shuffle => "🔀",
            PlayMode.SingleRepeat => "🔂", PlayMode.ListRepeat => "🔁",
            _ => "🔁"
        };
        OnPropertyChanged(nameof(PlayModeIcon));
    }

    private void OnToggleShuffle()
    {
        _playQueue.PlayMode = _playQueue.PlayMode == PlayMode.Shuffle ? PlayMode.Sequential : PlayMode.Shuffle;
        PlayModeIcon = _playQueue.PlayMode switch
        {
            PlayMode.Sequential => "➡️", PlayMode.Shuffle => "🔀", _ => "🔁"
        };
        OnPropertyChanged(nameof(PlayModeIcon));
    }

    private void OnToggleLike() => IsLiked = !IsLiked;
}

public enum SwipeDirection { Left, Right, Up, Down }
