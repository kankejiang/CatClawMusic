using System;
using System.Threading.Tasks;

namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 音频播放服务接口（平台抽象）
/// </summary>
public interface IAudioPlayerService
{
    /// <summary>
    /// 当前是否正在播放
    /// </summary>
    bool IsPlaying { get; }
    
    /// <summary>
    /// 当前播放位置（秒）
    /// </summary>
    double CurrentPosition { get; }
    
    /// <summary>
    /// 当前歌曲总时长（秒）
    /// </summary>
    double Duration { get; }
    
    /// <summary>
    /// 音量（0.0 - 1.0）
    /// </summary>
    double Volume { get; set; }
    
    /// <summary>
    /// 当前播放的歌曲文件路径
    /// </summary>
    string? CurrentSongFilePath { get; }
    
    /// <summary>
    /// 播放状态变化事件
    /// </summary>
    event EventHandler<bool>? PlaybackStateChanged;
    
    /// <summary>
    /// 播放进度变化事件
    /// </summary>
    event EventHandler<TimeSpan>? PositionChanged;
    
    /// <summary>
    /// 播放完成事件
    /// </summary>
    event EventHandler? PlaybackCompleted;
    
    /// <summary>
    /// 播放指定歌曲
    /// </summary>
    Task PlayAsync(string filePath);
    
    /// <summary>
    /// 暂停播放
    /// </summary>
    Task PauseAsync();
    
    /// <summary>
    /// 恢复播放
    /// </summary>
    Task ResumeAsync();
    
    /// <summary>
    /// 停止播放
    /// </summary>
    Task StopAsync();
    
    /// <summary>
    /// 跳转到指定位置
    /// </summary>
    Task SeekAsync(TimeSpan position);
    
    /// <summary>
    /// 初始化播放器
    /// </summary>
    Task InitializeAsync();
}
