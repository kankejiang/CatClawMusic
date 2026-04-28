using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;

namespace CatClawMusic.UI.ViewModels;

public class NowPlayingViewModel : BindableObject
{
    private readonly IAudioPlayerService _audioPlayer;
    private readonly ILyricsService _lyricsService;
    private readonly PlayQueue _playQueue;
    private LrcLyrics? _currentLyrics;
    
    private Song? _currentSong;

    public Song? CurrentSong
    {
        get => _currentSong;
        set
        {
            _currentSong = value;
            OnPropertyChanged();
            _ = LoadLyricsAsync(value);
        }
    }

    public string CurrentLyricLine { get; set; } = "暂无歌曲";
    public string NextLyricLine { get; set; } = "";
    
    public TimeSpan CurrentPosition { get; set; }
    public TimeSpan TotalDuration { get; set; }
    
    public double CurrentPositionSeconds
    {
        get => CurrentPosition.TotalSeconds;
        set
        {
            _ = _audioPlayer.SeekAsync(TimeSpan.FromSeconds(value));
        }
    }
    
    public double TotalDurationSeconds => TotalDuration.TotalSeconds;
    
    public string PlayPauseIcon { get; set; } = "▶";
    public string PlayModeIcon { get; set; } = "🔁";
    
    public Command PlayPauseCommand { get; }
    public Command NextCommand { get; }
    public Command PreviousCommand { get; }
    public Command TogglePlayModeCommand { get; }
    public Command ToggleShuffleCommand { get; }

    public NowPlayingViewModel(IAudioPlayerService audioPlayer, ILyricsService lyricsService, PlayQueue playQueue)
    {
        _audioPlayer = audioPlayer;
        _lyricsService = lyricsService;
        _playQueue = playQueue;
        
        PlayPauseCommand = new Command(OnPlayPause);
        NextCommand = new Command(OnNext);
        PreviousCommand = new Command(OnPrevious);
        TogglePlayModeCommand = new Command(OnTogglePlayMode);
        ToggleShuffleCommand = new Command(OnToggleShuffle);
        
        _audioPlayer.StateChanged += OnPlaybackStateChanged;
        _audioPlayer.PositionChanged += OnPositionChanged;
    }
    
    private void OnPlaybackStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        if (e.State == PlaybackState.Stopped)
        {
            MainThread.BeginInvokeOnMainThread(() => OnNext());
        }
        else if (e.State == PlaybackState.Playing)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                PlayPauseIcon = "⏸";
                OnPropertyChanged(nameof(PlayPauseIcon));
            });
        }
        else if (e.State == PlaybackState.Paused || e.State == PlaybackState.Error)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                PlayPauseIcon = "▶";
                OnPropertyChanged(nameof(PlayPauseIcon));
            });
        }
    }
    
    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            CurrentPosition = position;
            TotalDuration = _audioPlayer.Duration;
            
            // 更新歌词
            if (_currentLyrics != null && _currentLyrics.Lines.Count > 0)
            {
                var index = _lyricsService.GetCurrentLyricIndex(_currentLyrics, position);
                if (index >= 0 && index < _currentLyrics.Lines.Count)
                {
                    CurrentLyricLine = _currentLyrics.Lines[index].Text;
                    NextLyricLine = index + 1 < _currentLyrics.Lines.Count 
                        ? _currentLyrics.Lines[index + 1].Text 
                        : "";
                }
            }
            
            OnPropertyChanged(nameof(CurrentPosition));
            OnPropertyChanged(nameof(CurrentPositionSeconds));
            OnPropertyChanged(nameof(TotalDuration));
            OnPropertyChanged(nameof(TotalDurationSeconds));
            OnPropertyChanged(nameof(CurrentLyricLine));
            OnPropertyChanged(nameof(NextLyricLine));
        });
    }
    
    private async Task LoadLyricsAsync(Song? song)
    {
        if (song == null)
        {
            CurrentLyricLine = "暂无歌曲";
            NextLyricLine = "";
            _currentLyrics = null;
            OnPropertyChanged(nameof(CurrentLyricLine));
            OnPropertyChanged(nameof(NextLyricLine));
            return;
        }
        
        CurrentLyricLine = "正在加载歌词...";
        NextLyricLine = "";
        OnPropertyChanged(nameof(CurrentLyricLine));
        OnPropertyChanged(nameof(NextLyricLine));
        
        _currentLyrics = await _lyricsService.GetLyricsAsync(song);
        if (_currentLyrics == null)
        {
            CurrentLyricLine = "暂无歌词";
            NextLyricLine = "";
            OnPropertyChanged(nameof(CurrentLyricLine));
            OnPropertyChanged(nameof(NextLyricLine));
        }
    }
    
    private void OnPlayPause()
    {
        if (_audioPlayer.IsPlaying)
        {
            _audioPlayer.PauseAsync();
        }
        else
        {
            if (CurrentSong != null)
            {
                _audioPlayer.PlayAsync(CurrentSong.FilePath);
            }
        }
    }
    
    private void OnNext()
    {
        var nextSong = _playQueue.Next();
        if (nextSong != null)
        {
            CurrentSong = nextSong;
            _ = _audioPlayer.PlayAsync(nextSong.FilePath);
        }
    }
    
    private void OnPrevious()
    {
        var prevSong = _playQueue.Previous();
        if (prevSong != null)
        {
            CurrentSong = prevSong;
            _ = _audioPlayer.PlayAsync(prevSong.FilePath);
        }
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
        UpdatePlayModeIcon();
    }
    
    private void OnToggleShuffle()
    {
        _playQueue.PlayMode = _playQueue.PlayMode == PlayMode.Shuffle 
            ? PlayMode.Sequential 
            : PlayMode.Shuffle;
        UpdatePlayModeIcon();
    }
    
    private void UpdatePlayModeIcon()
    {
        PlayModeIcon = _playQueue.PlayMode switch
        {
            PlayMode.Sequential => "➡️",
            PlayMode.Shuffle => "🔀",
            PlayMode.SingleRepeat => "🔂",
            PlayMode.ListRepeat => "🔁",
            _ => "🔁"
        };
        OnPropertyChanged(nameof(PlayModeIcon));
    }
}
