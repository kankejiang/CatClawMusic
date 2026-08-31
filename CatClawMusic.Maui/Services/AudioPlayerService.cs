using CatClawMusic.Core.Interfaces;
using CatClawMusic.Maui.Services.Equalizer;
using Microsoft.Maui.ApplicationModel;

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

    // ─── 淡入淡出（crossfade）状态机 ───
    // 单播放器架构下用音量淡变模拟交叉淡变：当前曲尾段淡出 + 下一首开头淡入，避免硬切。
    private const int XfadeIdle = 0;
    private const int XfadeOut = 1; // 当前曲尾段正在淡出
    private const int XfadeIn = 2;  // 下一首正在淡入
    private int _xfadeState = XfadeIdle;
    private double _xfadeCur = 1.0;
    private long _xfadeInStartTicks;
    private double _lastNotifiedPosition = -1;
    // 缓存定时器回调委托，避免每次 tick 创建新闭包
    private static readonly TimerCallback _positionCallback = PositionTimerCallback;

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
        // 把 tick 派发回主线程读取播放位置。
        // 早期实现用构造期捕获的 SynchronizationContext（_mainContext）做 Post，
        // 但 MAUI 在部分启动路径下构造时主线程同步上下文尚未就绪 → _mainContext 为 null →
        // 每次 tick 的 Post 被静默丢弃 → 进度条冻结而音频照常播放（“进度条不会跑”）。
        // MainThread.BeginInvokeOnMainThread 由 MAUI 在启动早期安装，跨平台可靠，
        // 不依赖捕获时机，是修复进度条冻结的关键。
        MainThread.BeginInvokeOnMainThread(() => TickOnMainThread(svc));
    }

    private static int _tickLogCount;

    private static void TickOnMainThread(object? state)
    {
        if (state is not AudioPlayerService svc || svc._disposed) return;
#if DEBUG
        if (_tickLogCount < 3)
        {
            _tickLogCount++;
            Log.Debug("AudioPlayerService", $"[PositionTimer] tick #{_tickLogCount} pos={svc.CurrentPosition:F2}s");
        }
#endif
        try
        {
            var pos = svc.CurrentPosition;
            // 16ms tick（约 60fps）下每 tick 位置变化约 0.016s，
            // 阈值 0.01 保证 60fps 全量通知但过滤播放器位置抖动/重复值
            if (Math.Abs(pos - svc._lastNotifiedPosition) < 0.01 && pos > 0)
                return;
            svc._lastNotifiedPosition = pos;
            svc.PositionChanged?.Invoke(svc, TimeSpan.FromSeconds(pos));
            svc.UpdateCrossfade(pos, svc.Duration);
            svc.CheckPlatformCompletion();
        }
        catch (Exception ex)
        {
#if DEBUG
            Log.Debug("AudioPlayerService", $"[PositionTimer] Error: {ex.Message}");
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
        // 歌词表面可见性变化时重启位置定时器（60fps ↔ 5Hz 自适应），
        // 仅 Android 需要：Windows 端定时器不随页面可见性降频。
#if ANDROID
        PlayerSurfaceTracker.VisibilityChanged += OnPlayerSurfaceVisibilityChanged;
#endif
        InitializePlatform();
    }

#if ANDROID
    /// <summary>歌词表面可见性变化：播放中则按新频率重启位置定时器</summary>
    private void OnPlayerSurfaceVisibilityChanged()
    {
        if (_positionTimer != null)
            StartPositionTimer();
    }
#endif

    /// <summary>异步初始化服务（占位实现，平台可在 partial 中扩展）</summary>
    public Task InitializeAsync()
    {
        Log.Debug("AudioPlayerService", "[AudioPlayerService] Initialized");
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
                        Log.Debug("AudioPlayerService", $"[AudioPlayerService] AsyncUrlResolver error: {ex.Message}");
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
            Log.Debug("AudioPlayerService", $"[AudioPlayerService] Play error: {ex.Message}");
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

    /// <summary>歌词表面可见时的位置更新间隔（33ms ≈ 30fps）。
    /// 逐字歌词着色在 30fps 下视觉已足够平滑（1000/30≈33ms 每字步进人眼不可辨），
    /// 相比 60fps 把主线程 UI 负载减半——60fps tick 与切页动画/滚动争抢主线程是播放页卡顿主因之一。</summary>
    private const int PositionIntervalVisibleMs = 33;

    /// <summary>歌词表面不可见时的位置更新间隔（200ms = 5Hz）：
    /// 迷你播放器进度条/时间显示足够平滑，同时避免浏览音乐库等页面时
    /// 60Hz 位置事件风暴（跨线程回投 + 绑定求值 + 进度条重绘）拖慢整个 App。</summary>
    private const int PositionIntervalHiddenMs = 200;

    /// <summary>按当前歌词表面可见性计算位置更新间隔（仅 Android 自适应，其他平台恒定 60fps）。</summary>
    private static int GetPositionIntervalMs()
    {
#if ANDROID
        return PlayerSurfaceTracker.IsVisible ? PositionIntervalVisibleMs : PositionIntervalHiddenMs;
#else
        return PositionIntervalVisibleMs;
#endif
    }

    /// <summary>启动进度定时器：歌词表面可见时 16ms 触发一次（约 60fps），
    /// 提升逐字歌词（Karaoke）着色帧率，使 FillProgress 平滑过渡而非 20fps 跳变。
    /// 不可见时自动降频到 200ms（5Hz），把 CPU/主线程让给当前正在浏览的页面。</summary>
    internal void StartPositionTimer()
    {
        StopPositionTimer();
        var interval = GetPositionIntervalMs();
#if DEBUG
        Log.Debug("AudioPlayerService", $"[PositionTimer] Started ({interval}ms)");
#endif
        _positionTimer = new System.Threading.Timer(_positionCallback, this, interval, interval);
    }

    /// <summary>停止进度定时器并释放资源</summary>
    internal void StopPositionTimer()
    {
        if (_positionTimer != null)
        {
            _positionTimer.Dispose();
            _positionTimer = null;
            Log.Debug("AudioPlayerService", "[PositionTimer] Stopped");
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
#if ANDROID
        PlayerSurfaceTracker.VisibilityChanged -= OnPlayerSurfaceVisibilityChanged;
#endif
        StopPositionTimer();
        DisposePlatform();
    }

    // ─── 均衡器 ───

    /// <summary>将当前均衡器设置应用到平台音频引擎（Android 原生音效 / Windows AudioGraph DSP）</summary>
    public void ApplyEqualizer() => ApplyEqualizerPlatform();

    /// <summary>
    /// 均衡器设置变更后，对当前正在播放的歌曲即时重新应用。
    /// 主要用于 FFmpeg 烘焙式（10 段）模式：改变 EQ 需重新烘焙当前曲音频并就地重载（进度不丢失）。
    /// 也用于 FFmpeg 模式开关切换时把当前曲在「原生解码 ↔ FFmpeg 烘焙」两种路径间重载。
    /// 原生 5 段实时模式由 ApplyEqualizer() 即时处理，无需重载。
    /// Android 端实现防抖 + 重新转码重载；Windows 端为空实现（Windows 走 RestartPlaybackForEqSwitchAsync）。
    /// </summary>
    public void ReapplyEqualizerLive() => ReapplyEqualizerLivePlatform();

    /// <summary>重新应用当前音量（用于左右平衡等设置变更后即时生效）</summary>
    public void RefreshVolume() => SetPlatformVolume(Volume);

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
    partial void ApplyEqualizerPlatform();
    partial void ApplyCrossfadeVolume(double factor);
    partial void ReapplyEqualizerLivePlatform();

    // ─── 淡入淡出（crossfade） ───

    /// <summary>触发播放完成：若开启淡入淡出，先把音量降到 0 并标记下一首淡入，
    /// 再抛出 PlaybackCompleted 事件交由上层加载下一首。自然结束/出错路径统一走这里。</summary>
    internal void NotifyPlaybackCompleted()
    {
        if (EqualizerSettings.CrossfadeEnabled && EqualizerSettings.CrossfadeDuration > 0)
        {
            _xfadeState = XfadeIn;
            _xfadeInStartTicks = DateTime.UtcNow.Ticks;
            _xfadeCur = 0.0;
            ApplyCrossfadeVolume(0.0);
        }
        else
        {
            _xfadeState = XfadeIdle;
            _xfadeCur = 1.0;
        }
        PlaybackCompleted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>根据当前进度驱动淡入淡出音量（由进度定时器在主线程调用）。
    /// 尾段淡出：remaining ≤ N 时按 remaining/N 降低音量；下一首淡入：从淡入起点按 wall-clock 线性升到 1。</summary>
    private void UpdateCrossfade(double posSec, double durSec)
    {
        if (!EqualizerSettings.CrossfadeEnabled || durSec <= 0 || EqualizerSettings.CrossfadeDuration <= 0)
        {
            if (_xfadeCur != 1.0 || _xfadeState != XfadeIdle)
            {
                _xfadeState = XfadeIdle;
                _xfadeCur = 1.0;
                ApplyCrossfadeVolume(1.0);
            }
            return;
        }

        var n = EqualizerSettings.CrossfadeDuration; // 淡入淡出时长（秒）

        if (_xfadeState == XfadeIn)
        {
            var elapsed = (DateTime.UtcNow.Ticks - _xfadeInStartTicks) / 1e7;
            var f = Math.Min(1.0, elapsed / n);
            _xfadeCur = f;
            ApplyCrossfadeVolume(f);
            if (f >= 1.0) { _xfadeState = XfadeIdle; _xfadeCur = 1.0; }
            return;
        }

        var remaining = durSec - posSec;
        if (remaining <= n && remaining > 0)
        {
            var f = Math.Max(0.0, remaining / n);
            _xfadeCur = f;
            _xfadeState = XfadeOut;
            ApplyCrossfadeVolume(f);
        }
        else if (_xfadeState == XfadeOut && remaining > n)
        {
            // 用户回退进度 / 恢复播放 → 取消淡出
            _xfadeState = XfadeIdle;
            _xfadeCur = 1.0;
            ApplyCrossfadeVolume(1.0);
        }
    }
}
