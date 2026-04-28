using Android.Media;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.UI.Platforms.Android;

/// <summary>
/// Android 音频播放服务（封装 ExoPlayer）
/// </summary>
public class AudioPlayerService : IAudioPlayerService
{
    private MediaPlayer? _mediaPlayer;
    private bool _isPrepared = false;
    
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
            _mediaPlayer.Prepared += OnPrepared;
            _mediaPlayer.Completion += OnCompletion;
            _mediaPlayer.Error += OnError;
        }
        
        _mediaPlayer.Reset();
        _isPrepared = false;
        
        // 设置数据源
        if (filePathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            await _mediaPlayer.SetDataSourceAsync(filePathOrUrl);
        }
        else
        {
            await _mediaPlayer.SetDataSourceAsync(filePathOrUrl);
        }
        
        _mediaPlayer.Prepare();
        
        // 等待准备完成
        while (!_isPrepared)
        {
            await Task.Delay(50);
        }
        
        _mediaPlayer.Start();
        StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs 
        { 
            State = PlaybackState.Playing 
        });
        
        // 启动位置更新定时器
        StartPositionTimer();
    }
    
    public Task PauseAsync()
    {
        _mediaPlayer?.Pause();
        StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs 
        { 
            State = PlaybackState.Paused 
        });
        return Task.CompletedTask;
    }
    
    public Task StopAsync()
    {
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
    
    private void OnPrepared(object? sender, EventArgs e)
    {
        _isPrepared = true;
    }
    
    private void OnCompletion(object? sender, EventArgs e)
    {
        StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs 
        { 
            State = PlaybackState.Stopped 
        });
    }
    
    private void OnError(object? sender, MediaPlayer.ErrorEventArgs e)
    {
        StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs 
        { 
            State = PlaybackState.Error 
        });
    }
    
    private void StartPositionTimer()
    {
        // TODO: 实现定时更新播放位置
        // 使用 Timer 每秒触发 PositionChanged 事件
    }
}
