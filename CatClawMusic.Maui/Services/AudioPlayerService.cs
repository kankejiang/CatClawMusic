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
    private double _lastNotifiedPosition = -1;
    // 缓存定时器回调委托，避免每次 tick 创建新闭包
    private static readonly TimerCallback _positionCallback = PositionTimerCallback;
    // 缓存主线程调度委托，避免每次 tick 创建新 Action 闭包
    // 注意: 不能用 [ThreadStatic]，因为 Timer 线程写、主线程读需要看到同一个值
    private static volatile AudioPlayerService? _tickSvc;
    private static readonly Action _tickAction = TickOnMainThread;

    /// <summary>
    /// 平台可注入的 URL 转换器，用于将 smb:// 等 ExoPlayer 不支持的协议 URL 转换为可播放的 URL（如本地 HTTP 代理地址）。
    /// 输入为原始 URL，输出为转换后的 URL。若返回 null 则使用原始 URL。
    /// </summary>
    public static Func<string, string?>? UrlTransformer { get; set; }

    /// <summary>
    /// 异步 URL 解析器：用于需要异步操作的URL解析（如 OpenList raw_url 获取）。
    /// 输入为原始 URL，返回解析后的URL。返回null则继续使用 UrlTransformer 和原始URL。
    /// </summary>
    public static Func<string, Task<string?>>? AsyncUrlResolver { get; set; }

    /// <summary>播放状态变化事件（参数为是否正在播放）</summary>
    public event EventHandler<bool>? PlaybackStateChanged;
    /// <summary>播放位置变化事件（参数为当前播放位置）</summary>
    public event EventHandler<TimeSpan>? PositionChanged;
    /// <summary>媒体总时长变化事件（参数为当前媒体总时长，单位秒）</summary>
    public event EventHandler<double>? DurationChanged;
    /// <summary>播放完成事件</summary>
    public event EventHandler? PlaybackCompleted;
    /// <summary>请求播放下一首事件</summary>
    public event Func<Task>? PlayNextRequested;
    /// <summary>请求播放上一首事件</summary>
    public event Func<Task>? PlayPreviousRequested;
    /// <summary>收藏状态切换事件（参数为是否收藏）</summary>
    public event Action<bool>? FavoriteToggled;
    /// <summary>桌面歌词开关切换事件（参数为是否开启）</summary>
    public event Action<bool>? DesktopLyricToggled;

    /// <summary>获取当前是否正在播放</summary>
    public bool IsPlaying => GetPlatformIsPlaying();
    /// <summary>获取当前播放位置（秒）</summary>
    public double CurrentPosition => GetPlatformCurrentPositionSeconds();
    /// <summary>获取媒体总时长（秒）</summary>
    public double Duration => GetPlatformDurationSeconds();

    /// <summary>
    /// 位置定时器静态回调，避免每次 tick 创建 lambda 闭包。
    /// 整个回调在主线程执行，因为 ExoPlayer 要求同线程访问。
    /// </summary>
    private static void PositionTimerCallback(object? state)
    {
        if (state is not AudioPlayerService svc || svc._disposed) return;
        // ExoPlayer 要求在创建线程（主线程）访问，必须切到主线程
        _tickSvc = svc;
        MainThread.BeginInvokeOnMainThread(_tickAction);
    }

    private static void TickOnMainThread()
    {
        var svc = _tickSvc;
        if (svc == null || svc._disposed) return;
        try
        {
            var pos = svc.CurrentPosition;
            // 25fps 下每 tick 约 0.04s 变化，阈值 0.03 确保不跳过有效更新但过滤抖动
            if (Math.Abs(pos - svc._lastNotifiedPosition) < 0.03 && pos > 0)
                return;
            svc._lastNotifiedPosition = pos;
            svc.PositionChanged?.Invoke(svc, TimeSpan.FromSeconds(pos));
            svc.CheckPlatformCompletion();
        }
        catch (Exception ex)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[PositionTimer] Error: {ex.Message}");
#endif
        }
    }

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
    public async Task PlayAsync(string filePath)
    {
        try
        {
            _currentFilePath = filePath;

            // 检查本地缓存：如果音频已缓存到本地，直接使用本地文件
            string resolvedPath = AudioCacheService.Instance.GetCachedPath(filePath) ?? filePath;

            // 如果是本地缓存文件，跳过 URL 解析和代理转换
            if (resolvedPath == filePath)
            {
                // 先尝试异步URL解析器（如 OpenList raw_url 获取）
                if (AsyncUrlResolver != null)
                {
                    try
                    {
                        var asyncResolved = await AsyncUrlResolver(filePath);
                        if (!string.IsNullOrEmpty(asyncResolved))
                            resolvedPath = asyncResolved;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] AsyncUrlResolver error: {ex.Message}");
                    }
                }

                // 应用同步 URL 转换器（如 smb:// → http://127.0.0.1:xxxx/ 代理）
                var syncResolved = UrlTransformer?.Invoke(resolvedPath);
                if (!string.IsNullOrEmpty(syncResolved))
                    resolvedPath = syncResolved;
            }

            PlatformPlay(BuildSourceUri(resolvedPath));
            // 不在此处启动 PositionTimer — ExoPlayer 的 OnIsPlayingChanged 回调会负责启动/停止
            PlaybackStateChanged?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] Play error: {ex.Message}");
        }
    }

    /// <summary>异步暂停播放</summary>
    public Task PauseAsync()
    {
        PlatformPause();
        StopPositionTimer();
        PlaybackStateChanged?.Invoke(this, false);
        return Task.CompletedTask;
    }

    /// <summary>异步恢复播放</summary>
    public Task ResumeAsync()
    {
        PlatformResume();
        // 不在此处启动 PositionTimer — ExoPlayer 的 OnIsPlayingChanged 回调会负责启动/停止
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
        _lastNotifiedPosition = position.TotalSeconds;
        PositionChanged?.Invoke(this, position);
        return Task.CompletedTask;
    }

    #region 进度定时器

    /// <summary>启动进度定时器，每 50ms 触发一次位置更新（20fps），确保逐字填充动画流畅</summary>
    internal void StartPositionTimer()
    {
        StopPositionTimer();
        _positionTimer = new System.Threading.Timer(_positionCallback, this, 50, 50);
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
