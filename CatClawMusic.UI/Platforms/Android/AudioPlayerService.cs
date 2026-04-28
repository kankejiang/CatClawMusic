using Android.Media;
using CatClawMusic.Core.Interfaces;
using System.Timers;

namespace CatClawMusic.UI.Platforms.Android;

/// <summary>
/// Android 音频播放服务（封装 MediaPlayer）
/// </summary>
public class AudioPlayerService : IAudioPlayerService
{
    private MediaPlayer? _mediaPlayer;
    private bool _isPrepared = false;
    private System.Timers.Timer? _positionTimer;
    
    public bool IsPlaying => _mediaPlayer?.IsPlaying ?? false;
    public TimeSpan Position => TimeSpan.FromMilliseconds(_mediaPlayer?.CurrentPosition ?? 0);
    public TimeSpan Duration => TimeSpan.FromMilliseconds(_mediaPlayer?.Duration ?? 0);
    public int Volume { get; set; } = 100;
    
    public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;
    public event EventHandler<TimeSpan>? PositionChanged;
    
    public async Task PlayAsync(string filePathOrUrl)
    {
        if (_mediaPlayer == null)
        {
            _mediaPlayer = new MediaPlayer();
            _mediaPlayer.Completion += OnCompletion;
            _mediaPlayer.Error += OnError;
        }
        
        _mediaPlayer.Reset();
        _isPrepared = false;
        
        // 设置数据源
        await _mediaPlayer.SetDataSourceAsync(filePathOrUrl);
        
        // 异步准备，避免阻塞主线程
        var tcs = new TaskCompletionSource<bool>();
        EventHandler? preparedHandler = null;
        preparedHandler = (s, e) =>
        {
            _mediaPlayer.Prepared -= preparedHandler;
            _isPrepared = true;
            tcs.TrySetResult(true);
        };
        _mediaPlayer.Prepared += preparedHandler;
        _mediaPlayer.PrepareAsync();
        
        await tcs.Task;
        
        _mediaPlayer.Start();
        StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs 
        { 
            State = PlaybackState.Playing 
        });
        
        StartPositionTimer();
    }
    
    public Task PauseAsync()
    {
        _mediaPlayer?.Pause();
        StopPositionTimer();
        StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs 
        { 
            State = PlaybackState.Paused 
        });
        return Task.CompletedTask;
    }
    
    public Task StopAsync()
    {
        StopPositionTimer();
        _mediaPlayer?.Stop();
        _mediaPlayer?.Reset();
        StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs 
        { 
            State = PlaybackState.Stopped 
        });
        return Task.CompletedTask;
    }
    
    public Task SeekAsync(TimeSpan position)
    {
        if (_mediaPlayer != null && _isPrepared)
        {
            _mediaPlayer.SeekTo((int)position.TotalMilliseconds);
        }
        return Task.CompletedTask;
    }
    
    private void OnCompletion(object? sender, EventArgs e)
    {
        StopPositionTimer();
        StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs 
        { 
            State = PlaybackState.Stopped 
        });
    }
    
    private void OnError(object? sender, MediaPlayer.ErrorEventArgs e)
    {
        StopPositionTimer();
        StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs 
        { 
            State = PlaybackState.Error 
        });
    }
    
    private void StartPositionTimer()
    {
        StopPositionTimer();
        _positionTimer = new System.Timers.Timer(500);
        _positionTimer.Elapsed += OnPositionTimerElapsed;
        _positionTimer.AutoReset = true;
        _positionTimer.Start();
    }
    
    private void StopPositionTimer()
    {
        if (_positionTimer != null)
        {
            _positionTimer.Elapsed -= OnPositionTimerElapsed;
            _positionTimer.Stop();
            _positionTimer.Dispose();
            _positionTimer = null;
        }
    }
    
    private void OnPositionTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (_mediaPlayer != null && _isPrepared)
        {
            PositionChanged?.Invoke(this, Position);
        }
    }
}
