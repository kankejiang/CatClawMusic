namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 音频播放服务接口
/// </summary>
public interface IAudioPlayerService
{
    /// <summary>
    /// 播放指定歌曲
    /// </summary>
    Task PlayAsync(string filePathOrUrl);
    
    /// <summary>
    /// 暂停播放
    /// </summary>
    Task PauseAsync();
    
    /// <summary>
    /// 停止播放
    /// </summary>
    Task StopAsync();
    
    /// <summary>
    /// 跳转到指定位置
    /// </summary>
    Task SeekAsync(TimeSpan position);
    
    /// <summary>
    /// 是否正在播放
    /// </summary>
    bool IsPlaying { get; }
    
    /// <summary>
    /// 当前播放位置
    /// </summary>
    TimeSpan Position { get; }
    
    /// <summary>
    /// 歌曲总时长
    /// </summary>
    TimeSpan Duration { get; }
    
    /// <summary>
    /// 音量（0-100）
    /// </summary>
    int Volume { get; set; }
    
    /// <summary>
    /// 播放状态改变事件
    /// </summary>
    event EventHandler<PlaybackStateChangedEventArgs> StateChanged;
    
    /// <summary>
    /// 播放位置改变事件
    /// </summary>
    event EventHandler<TimeSpan> PositionChanged;
}

/// <summary>
/// 播放状态改变事件参数
/// </summary>
public class PlaybackStateChangedEventArgs : EventArgs
{
    public PlaybackState State { get; set; }
    public string? SongId { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 播放状态
/// </summary>
public enum PlaybackState
{
    Stopped,
    Playing,
    Paused,
    Buffering,
    Error
}
