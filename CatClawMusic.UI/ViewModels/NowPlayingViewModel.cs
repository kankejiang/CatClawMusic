using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;

namespace CatClawMusic.UI.ViewModels;

public class NowPlayingViewModel : BindableObject
{
    private Song? _currentSong;
    private IAudioPlayerService? _audioPlayer;
    private PlayQueue _playQueue;
    private System.Timers.Timer _positionTimer;

    public Song? CurrentSong
    {
        get => _currentSong;
        set
        {
            _currentSong = value;
            OnPropertyChanged();
        }
    }

    public string CurrentLyricLine { get; set; } = "正在加载歌词...";
    public string NextLyricLine { get; set; } = "";
    
    public TimeSpan CurrentPosition { get; set; }
    public TimeSpan TotalDuration { get; set; }
    
    public double CurrentPositionSeconds
    {
        get => CurrentPosition.TotalSeconds;
        set
        {
            // 拖动进度条
            _ = _audioPlayer?.SeekAsync(TimeSpan.FromSeconds(value));
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

    public NowPlayingViewModel()
    {
        _audioPlayer = null; // TODO: 从依赖注入获取
        _playQueue = new PlayQueue();
        
        PlayPauseCommand = new Command(OnPlayPause);
        NextCommand = new Command(OnNext);
        PreviousCommand = new Command(OnPrevious);
        TogglePlayModeCommand = new Command(OnTogglePlayMode);
        ToggleShuffleCommand = new Command(OnToggleShuffle);
        
        // 定时更新播放位置
        _positionTimer = new System.Timers.Timer(1000);
        _positionTimer.Elapsed += OnPositionTimerElapsed;
        _positionTimer.Start();
    }
    
    private void OnPlayPause()
    {
        if (_audioPlayer?.IsPlaying == true)
        {
            _audioPlayer.PauseAsync();
            PlayPauseIcon = "▶";
        }
        else
        {
            _audioPlayer?.PlayAsync(CurrentSong?.FilePath ?? "");
            PlayPauseIcon = "⏸";
        }
        OnPropertyChanged(nameof(PlayPauseIcon));
    }
    
    private void OnNext()
    {
        var nextSong = _playQueue.Next();
        if (nextSong != null)
        {
            CurrentSong = nextSong;
            _ = _audioPlayer?.PlayAsync(nextSong.FilePath);
        }
    }
    
    private void OnPrevious()
    {
        var prevSong = _playQueue.Previous();
        if (prevSong != null)
        {
            CurrentSong = prevSong;
            _ = _audioPlayer?.PlayAsync(prevSong.FilePath);
        }
    }
    
    private void OnTogglePlayMode()
    {
        // 切换播放模式：顺序 → 随机 → 单曲循环 → 列表循环
        // TODO: 实现
    }
    
    private void OnToggleShuffle()
    {
        _playQueue.PlayMode = _playQueue.PlayMode == PlayMode.Shuffle 
            ? PlayMode.Sequential 
            : PlayMode.Shuffle;
    }
    
    private void OnPositionTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_audioPlayer == null) return;
        
        CurrentPosition = _audioPlayer.Position;
        TotalDuration = _audioPlayer.Duration;
        
        // 更新歌词
        // TODO: 根据 CurrentPosition 更新 CurrentLyricLine
        
        OnPropertyChanged(nameof(CurrentPosition));
        OnPropertyChanged(nameof(CurrentPositionSeconds));
    }
}
