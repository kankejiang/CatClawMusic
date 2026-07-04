using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 跨平台音频播放服务，使用平台原生 MediaPlayer API。
/// Android: Android.Media.MediaPlayer
/// Windows: Windows.Media.Playback.MediaPlayer
/// </summary>
public partial class AudioPlayerService : IAudioPlayerService, IDisposable
{
    private string? _currentFilePath;
    private bool _disposed;
    private System.Threading.Timer? _positionTimer;

    /// <summary>
    /// 平台可注入的 URL 转换器，用于将 smb:// 等 ExoPlayer 不支持的协议 URL 转换为可播放的 URL（如本地 HTTP 代理地址）。
    /// 输入为原始 URL，输出为转换后的 URL。若返回 null 则使用原始 URL。
    /// </summary>
    public static Func<string, string?>? UrlTransformer { get; set; }

    /// <summary>播放状态变化事件（参数为是否正在播放）</summary>
    public event EventHandler<bool>? PlaybackStateChanged;
    /// <summary>播放位置变化事件（参数为当前播放位置）</summary>
    public event EventHandler<TimeSpan>? PositionChanged;
    /// <summary>播放完成事件</summary>
    public event EventHandler? PlaybackCompleted;
    /// <summary>请求播放下一首事件</summary>
    public event Func<Task>? PlayNextRequested;
    /// <summary>请求播放上一首事件</summary>
    public event Func<Task>? PlayPreviousRequested;
    /// <summary>收藏状态切换事件（参数为是否收藏）</summary>
    public event Action<bool>? FavoriteToggled;

    /// <summary>获取当前是否正在播放</summary>
    public bool IsPlaying => GetPlatformIsPlaying();
    /// <summary>获取当前播放位置（秒）</summary>
    public double CurrentPosition => GetPlatformCurrentPositionSeconds();
    /// <summary>获取媒体总时长（秒）</summary>
    public double Duration => GetPlatformDurationSeconds();

    /// <summary>
    /// 获取或设置音量（0.0 ~ 1.0），超出范围会被自动钳制。
    /// </summary>
    public double Volume
    {
        get => GetPlatformVolume();
        set => SetPlatformVolume(Math.Clamp(value, 0.0, 1.0));
    }

    /// <summary>获取当前播放歌曲的文件路径</summary>
    public string? CurrentSongFilePath => _currentFilePath;

    /// <summary>构造函数，初始化平台原生播放器</summary>
    public AudioPlayerService()
    {
        InitializePlatform();
    }

    /// <summary>异步初始化服务（占位实现，平台可在 partial 中扩展）</summary>
    public Task InitializeAsync()
    {
        System.Diagnostics.Debug.WriteLine("[AudioPlayerService] Initialized");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 异步播放指定文件或网络地址。
    /// 支持 http/https/rtsp/content 协议及本地文件路径。
    /// </summary>
    /// <param name="filePath">音频文件路径或网络地址</param>
    public Task PlayAsync(string filePath)
    {
        try
        {
            _currentFilePath = filePath;
            // 应用平台 URL 转换器（如 smb:// → http://127.0.0.1:xxxx/ 代理）
            var resolvedPath = UrlTransformer?.Invoke(filePath) ?? filePath;
            PlatformPlay(BuildSourceUri(resolvedPath));
            StartPositionTimer();
            PlaybackStateChanged?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] Play error: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    /// <summary>异步暂停播放</summary>
    public Task PauseAsync()
    {
        PlatformPause();
        PlaybackStateChanged?.Invoke(this, false);
        return Task.CompletedTask;
    }

    /// <summary>异步恢复播放</summary>
    public Task ResumeAsync()
    {
        PlatformResume();
        StartPositionTimer();
        PlaybackStateChanged?.Invoke(this, true);
        return Task.CompletedTask;
    }

    /// <summary>异步停止播放并停止进度定时器</summary>
    public Task StopAsync()
    {
        PlatformStop();
        StopPositionTimer();
        PlaybackStateChanged?.Invoke(this, false);
        return Task.CompletedTask;
    }

    /// <summary>异步跳转到指定播放位置</summary>
    /// <param name="position">目标播放位置</param>
    public Task SeekAsync(TimeSpan position)
    {
        PlatformSeek(position);
        PositionChanged?.Invoke(this, position);
        return Task.CompletedTask;
    }

    #region 进度定时器

    /// <summary>启动进度定时器，每 500ms 触发一次位置更新</summary>
    internal void StartPositionTimer()
    {
        StopPositionTimer();
        _positionTimer = new System.Threading.Timer(_ =>
        {
            // ExoPlayer 必须在主线程访问，MAUI 11 严格检查线程
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    var pos = CurrentPosition;
                    var dur = Duration;
                    System.Diagnostics.Debug.WriteLine($"[PositionTimer] Tick: pos={pos:F1}s, dur={dur:F1}s");
                    PositionChanged?.Invoke(this, TimeSpan.FromSeconds(pos));
                    CheckPlatformCompletion();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PositionTimer] Error: {ex.Message}");
                }
            });
        }, null, 500, 500);
        System.Diagnostics.Debug.WriteLine($"[PositionTimer] Started, hasSubscribers={PositionChanged != null}");
    }

    /// <summary>停止进度定时器并释放资源</summary>
    internal void StopPositionTimer()
    {
        if (_positionTimer != null)
        {
            _positionTimer.Dispose();
            _positionTimer = null;
            System.Diagnostics.Debug.WriteLine("[PositionTimer] Stopped");
        }
    }

    #endregion

    private static Uri BuildSourceUri(string filePathOrUrl)
    {
        if (filePathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            filePathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            filePathOrUrl.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase) ||
            filePathOrUrl.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(filePathOrUrl);
        }
        var fullPath = Path.GetFullPath(filePathOrUrl);
        return new Uri($"file://{fullPath}");
    }

    /// <summary>释放平台原生播放器及定时器资源</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopPositionTimer();
        DisposePlatform();
    }

    // 平台实现由 partial class 文件提供
    partial void InitializePlatform();
    partial void PlatformPlay(Uri source);
    partial void PlatformPause();
    partial void PlatformResume();
    partial void PlatformStop();
    partial void PlatformSeek(TimeSpan position);
    partial void CheckPlatformCompletion();
    private partial bool GetPlatformIsPlaying();
    private partial double GetPlatformCurrentPositionSeconds();
    private partial double GetPlatformDurationSeconds();
    private partial double GetPlatformVolume();
    partial void SetPlatformVolume(double volume);
    partial void DisposePlatform();
}
