using Android.Content;
using Android.Media;
using Android.Net.Wifi;
using Android.OS;
using AndroidX.Media3.Common;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Maui.Platforms.Android;
using SimpleExoPlayer = AndroidX.Media3.ExoPlayer.SimpleExoPlayer;
namespace CatClawMusic.Maui.Services;

/// <summary>基于 Media3 ExoPlayer + FFmpeg 的 Android 音频播放服务，提供音频播放、暂停、跳转、音量控制及前台通知、音频焦点、唤醒锁等能力</summary>
public partial class AudioPlayerService
{
    /// <summary>ExoPlayer 播放器实例</summary>
    private SimpleExoPlayer? _player;
    /// <summary>当前音量（0.0 ~ 1.0）</summary>
    private float _volume = 1.0f;
    /// <summary>ExoPlayer 是否已进入 STATE_READY/STATE_ENDED 状态（即 Prepare 完成）</summary>
    private volatile bool _isPrepared;
    /// <summary>由 ExoPlayerListener 维护的真实播放状态，避免依赖 .NET 绑定的 IsPlaying 属性</summary>
    private volatile bool _isActuallyPlaying;
    /// <summary>Android 上下文，由 MainActivity 注入，用于启动前台服务及获取系统服务</summary>
    private global::Android.Content.Context? _androidContext;
    /// <summary>播放期间持有的 PARTIAL_WAKE_LOCK，防止 CPU 进入休眠</summary>
    private PowerManager.WakeLock? _wakeLock;
    /// <summary>播放期间持有的 WIFI_MODE_FULL Lock，防止 Wi-Fi 休眠导致断流</summary>
    private WifiManager.WifiLock? _wifiLock;
    /// <summary>Android 音频管理器，用于请求/释放音频焦点</summary>
    private AudioManager? _audioManager;
    /// <summary>是否因短暂失去音频焦点而自动暂停，焦点恢复后用于决定是否自动继续播放</summary>
    private bool _pausedByFocusLoss;
    /// <summary>最近一次缓存的播放位置（毫秒），避免播放器释放后无法获取进度</summary>
    private long _cachedPositionMs;
    /// <summary>当前播放文件的 URI 字符串</summary>
    private string? _currentPath;
    /// <summary>FFmpeg 转码服务实例，用于处理 ExoPlayer 原生不支持的音频格式</summary>
    private FFmpegService? _ffmpeg;
    /// <summary>绑定到主线程 Looper 的 Handler，用于在主线程上回调 UI 相关事件</summary>
    private readonly Android.OS.Handler _mainHandler = new(Looper.MainLooper!);
    /// <summary>最近一次 SeekTo 调用的时间戳（Ticks），用于防抖避免频繁跳转</summary>
    private long _lastSeekTicks;
    /// <summary>Seek 防抖窗口时间（毫秒），在该时间内的重复 seek 将被忽略</summary>
    private const int SeekGuardMs = 800;
    /// <summary>ExoPlayer 状态监听器实例</summary>
    private ExoPlayerListener? _playerListener;

    // Audio focus listener
    /// <summary>音频焦点变化监听器实例</summary>
    private AudioFocusListener? _focusListener;

    /// <summary>平台特定的初始化逻辑：注册音频管理器、音频焦点监听器以及前台服务通知回调</summary>
    partial void InitializePlatform()
    {
        var ctx = global::Android.App.Application.Context;
        _audioManager = (AudioManager?)ctx.GetSystemService(Context.AudioService);
        _focusListener = new AudioFocusListener(this);

        ForegroundPlayerService.OnPlayPauseRequested += OnNotifPlayPauseRequested;
        ForegroundPlayerService.OnNextRequested += OnNotifNextRequested;
        ForegroundPlayerService.OnPreviousRequested += OnNotifPreviousRequested;
        ForegroundPlayerService.OnLyricsRequested += OnNotifLyricsRequested;
        ForegroundPlayerService.OnFavoriteToggled += OnNotifFavoriteToggled;

        PositionChanged += OnPositionChangedForNotification;
        PlaybackStateChanged += OnPlaybackStateChangedForNotification;
    }

    /// <summary>上次通知栏进度更新时间，用于限流（每秒最多更新一次）</summary>
    private long _lastNotifProgressMs;

    /// <summary>播放位置变化时更新通知栏进度条</summary>
    private void OnPositionChangedForNotification(object? sender, TimeSpan e)
    {
        if (!IsPlaying) return;
        // 限流：每秒最多更新一次通知栏进度，避免频繁创建 PlaybackState.Builder 等对象
        var nowMs = System.Environment.TickCount64;
        if (nowMs - _lastNotifProgressMs < 1000) return;
        _lastNotifProgressMs = nowMs;
        try { UpdateNotificationProgress(); } catch { }
    }

    /// <summary>播放状态变化时刷新前台通知</summary>
    private void OnPlaybackStateChangedForNotification(object? sender, bool isPlaying)
    {
        try { UpdateForegroundNotification(); } catch { }
    }

    /// <summary>注入 Android 上下文，用于启动前台服务及获取系统服务</summary>
    /// <param name="context">Android 上下文，通常由 MainActivity 提供</param>
    public void SetAndroidContext(global::Android.Content.Context context)
    {
        _androidContext = context;
    }

    /// <summary>注入 FFmpeg 服务实例，用于处理 ExoPlayer 原生不支持的音频格式</summary>
    /// <param name="ffmpeg">FFmpeg 服务实例</param>
    public void SetFFmpegService(FFmpegService ffmpeg)
    {
        _ffmpeg = ffmpeg;
    }

    // ═══════════════════════════════════════
    // ExoPlayer 构建
    // ═══════════════════════════════════════

    /// <summary>确保 ExoPlayer 实例已创建，若尚未创建则按当前音量与监听器配置构建一个新的实例</summary>
    /// <returns>已创建或已存在的 SimpleExoPlayer 实例</returns>
    private SimpleExoPlayer EnsurePlayer()
    {
        if (_player != null) return _player;

        var ctx = _androidContext ?? global::Android.App.Application.Context;
        _player = new SimpleExoPlayer.Builder(ctx).Build();
        _player.Volume = _volume;
        _player.RepeatMode = 0; // REPEAT_MODE_OFF
        _player.PlayWhenReady = false;

        // 设置 AudioAttributes，让 ExoPlayer 自动管理音频焦点与音频路由
        // 在 MIUI/HyperOS 等设备上，未设置 AudioAttributes 会导致音频无法正确路由到扬声器/蓝牙，表现为静音
        var audioAttributes = new AndroidX.Media3.Common.AudioAttributes.Builder()
            .SetUsage(C.UsageMedia)
            .SetContentType(C.AudioContentTypeMusic)
            .Build();
        _player.SetAudioAttributes(audioAttributes, true);

        // 注册 Listener 准确跟踪播放状态
        _playerListener = new ExoPlayerListener(this);
        _player.AddListener(_playerListener);

        return _player;
    }

    // ═══════════════════════════════════════
    // 播放核心
    // ═══════════════════════════════════════

    /// <summary>平台特定的播放入口，由跨平台 AudioPlayerService 调用</summary>
    /// <param name="source">音频源 URI</param>
    partial void PlatformPlay(Uri source)
    {
        _ = PlayInternalAsync(source, autoPlay: true);
    }

    /// <summary>执行实际的播放流程：重置状态、必要时通过 FFmpeg 转码、设置媒体项、Prepare 并启动前台服务</summary>
    /// <param name="source">音频源 URI</param>
    /// <param name="autoPlay">是否在 Prepare 完成后立即开始播放</param>
    private async Task PlayInternalAsync(Uri source, bool autoPlay)
    {
        // 重置状态：在切换歌曲期间，IsPlaying 应返回 false，
        // 防止 _positionTimer 在 Prepare 未完成时反复拉到 0 位置
        _isPrepared = false;
        _isActuallyPlaying = false;
        _cachedPositionMs = 0;
        _lastSeekTicks = 0;
        _currentPath = source.ToString();

        try
        {
            var player = EnsurePlayer();
            player.Stop();
            player.ClearMediaItems();

            var playUri = source;
            var localPath = source.IsFile ? source.LocalPath :
                source.Scheme == "file" ? source.AbsolutePath : null;

            var ffmpegEnabled = Preferences.Get("ffmpeg_enabled", true);
            // 对 m4a/mp4 等 ExoPlayer 原生解码不完整的格式强制走 FFmpeg 软解
            // 原因：MIUI/HyperOS 上 ExoPlayer 对 m4a (AAC-LC/ALAC) 可能 prepare 成功但实际无声
            if (ffmpegEnabled && localPath != null && NeedsTranscoding(localPath))
            {
                // 确保 FFmpeg 已初始化（首次播放时 Task.Run 注入可能尚未完成）
                if (_ffmpeg == null || !_ffmpeg.IsAvailable)
                {
                    await EnsureFFmpegReadyAsync();
                }

                if (_ffmpeg != null && _ffmpeg.IsAvailable)
                {
                    System.Diagnostics.Debug.WriteLine($"[ExoPlayer] FFmpeg 转码: {Path.GetFileName(localPath)}");
                    var wavPath = await _ffmpeg.TranscodeToWavAsync(localPath);
                    if (wavPath != null)
                    {
                        playUri = new Uri("file://" + wavPath);
                        System.Diagnostics.Debug.WriteLine("[ExoPlayer] FFmpeg 转码完成，使用 WAV 播放");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[ExoPlayer] FFmpeg 转码失败，回退原生播放");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[ExoPlayer] FFmpeg 不可用，回退原生播放 m4a");
                }
            }

            var mediaItem = MediaItem.FromUri(global::Android.Net.Uri.Parse(playUri.ToString()));
            player.SetMediaItem(mediaItem);
            player.Prepare();
            player.PlayWhenReady = autoPlay;
            if (autoPlay) player.Play();

            // 不在这里设置 _isPrepared，由 ExoPlayerListener.OnPlaybackStateChanged(STATE_READY) 触发
            AcquireWakeLock();
            // 音频焦点由 ExoPlayer.SetAudioAttributes(handleAudioFocus=true) 自动管理，无需手动请求
            StartForegroundService();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExoPlayer] Play error: {ex.Message}");
            // FFmpeg 兜底：如果 ExoPlayer 直接播放失败，尝试转码
            await TryFFmpegFallbackAsync(source);
        }
    }

    /// <summary>确保 FFmpegService 已初始化并注入（解决启动时 Task.Run 注入时序问题）</summary>
    private async Task EnsureFFmpegReadyAsync()
    {
        if (_ffmpeg != null && _ffmpeg.IsAvailable) return;
        try
        {
            _ffmpeg ??= new FFmpegService();
            await _ffmpeg.InitializeAsync();
            System.Diagnostics.Debug.WriteLine($"[ExoPlayer] FFmpeg 就绪: {_ffmpeg.IsAvailable}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExoPlayer] FFmpeg 初始化异常: {ex.Message}");
        }
    }

    /// <summary>ExoPlayer 播放失败后的 FFmpeg 兜底：将原文件转码为 WAV 后再次尝试播放</summary>
    /// <param name="source">原始音频源 URI</param>
    private async Task TryFFmpegFallbackAsync(Uri source)
    {
        var localPath = source.IsFile ? source.LocalPath :
            source.Scheme == "file" ? source.AbsolutePath : null;
        var ffmpegEnabled = Preferences.Get("ffmpeg_enabled", true);
        if (!ffmpegEnabled || localPath == null || _ffmpeg == null || !_ffmpeg.IsAvailable) return;

        try
        {
            System.Diagnostics.Debug.WriteLine("[ExoPlayer] 尝试 FFmpeg 兜底转码...");
            var wavPath = await _ffmpeg.TranscodeToWavAsync(localPath);
            if (wavPath == null) return;

            var player = EnsurePlayer();
            player.Stop();
            player.ClearMediaItems();

            var mediaItem = MediaItem.FromUri(global::Android.Net.Uri.Parse("file://" + wavPath));
            player.SetMediaItem(mediaItem);
            player.Prepare();
            player.Play();
            AcquireWakeLock();
            // 音频焦点由 ExoPlayer 自动管理
            StartForegroundService();
            System.Diagnostics.Debug.WriteLine("[ExoPlayer] FFmpeg 兜底成功");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExoPlayer] FFmpeg 兜底失败: {ex.Message}");
        }
    }

    /// <summary>判断指定文件是否需要 FFmpeg 转码（ExoPlayer 原生不支持的格式）</summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>需要转码返回 true，否则返回 false</returns>
    private static bool NeedsTranscoding(string filePath)
    {
        var ext = Path.GetExtension(filePath)?.ToLowerInvariant();
        return ext is ".m4a" or ".m4b" or ".mp4" or ".mov" or ".wma"
            or ".ogg" or ".opus" or ".ape" or ".wv" or ".aiff" or ".aif" or ".alac";
    }

    // ═══════════════════════════════════════
    // 播放控制
    // ═══════════════════════════════════════

    /// <summary>平台特定的暂停逻辑：暂停 ExoPlayer、释放唤醒锁（音频焦点由 ExoPlayer 自动管理）</summary>
    partial void PlatformPause()
    {
        try { _player?.Pause(); ReleaseWakeLock(); } catch { }
    }

    /// <summary>平台特定的恢复播放逻辑：恢复 ExoPlayer 播放、重新获取唤醒锁与 Wi-Fi 锁、确保前台服务运行（音频焦点由 ExoPlayer 自动管理）</summary>
    partial void PlatformResume()
    {
        try
        {
            if (_player != null)
            {
                _player.PlayWhenReady = true;
                _player.Play();
                AcquireWakeLock();
                StartForegroundService();
            }
        }
        catch { }
    }

    /// <summary>平台特定的停止逻辑：停止播放器、清空媒体项并重置状态、释放唤醒锁（音频焦点由 ExoPlayer 自动管理）</summary>
    partial void PlatformStop()
    {
        try
        {
            _player?.Stop();
            _player?.ClearMediaItems();
            _isPrepared = false;
            _isActuallyPlaying = false;
            _cachedPositionMs = 0;
            ReleaseWakeLock();
        }
        catch { }
    }

    /// <summary>平台特定的跳转逻辑：跳转到指定位置并更新缓存</summary>
    /// <param name="position">目标位置</param>
    partial void PlatformSeek(TimeSpan position)
    {
        try
        {
            _lastSeekTicks = DateTime.UtcNow.Ticks;
            _player?.SeekTo((long)position.TotalMilliseconds);
            _cachedPositionMs = (long)position.TotalMilliseconds;
        }
        catch { }
    }

    /// <summary>获取平台真实的播放状态（由 ExoPlayerListener 维护）</summary>
    /// <returns>正在播放返回 true，否则返回 false</returns>
    private partial bool GetPlatformIsPlaying()
    {
        // 使用 listener 维护的真实状态，避免 ExoPlayer.IsPlaying 绑定差异
        return _isActuallyPlaying;
    }

    /// <summary>定时器回调：已由 ExoPlayerListener 处理 STATE_ENDED，此处仅做安全网</summary>
    partial void CheckPlatformCompletion()
    {
        // ExoPlayerListener.OnPlaybackStateChanged 已即时处理 STATE_ENDED
        // 不再通过定时器检测，避免延迟和重复触发
    }

    /// <summary>获取当前播放位置（秒），优先取 ExoPlayer 实时位置，失败时回退到缓存值</summary>
    /// <returns>当前播放位置（秒）</returns>
    private partial double GetPlatformCurrentPositionSeconds()
    {
        try
        {
            if (_player != null)
            {
                _cachedPositionMs = _player.CurrentPosition;
                return _cachedPositionMs / 1000.0;
            }
        }
        catch { }
        return _cachedPositionMs / 1000.0;
    }

    /// <summary>获取音频总时长（秒），仅当 ExoPlayer 报告的时长大于 0 时返回</summary>
    /// <returns>音频总时长（秒），无法获取时返回 0</returns>
    private partial double GetPlatformDurationSeconds()
    {
        try
        {
            if (_player != null)
            {
                var dur = _player.Duration;
                if (dur > 0)
                    return dur / 1000.0;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExoPlayer] GetDuration error: {ex.Message}");
        }
        return 0;
    }

    /// <summary>获取当前音量（0.0 ~ 1.0）</summary>
    /// <returns>当前音量</returns>
    private partial double GetPlatformVolume() => _volume;

    /// <summary>设置当前音量，并同步到 ExoPlayer</summary>
    /// <param name="volume">音量值（0.0 ~ 1.0）</param>
    partial void SetPlatformVolume(double volume)
    {
        _volume = (float)Math.Clamp(volume, 0.0, 1.0);
        try { if (_player != null) _player.Volume = _volume; } catch { }
    }

    /// <summary>释放平台相关资源：停止前台服务、释放唤醒锁与音频焦点、释放 ExoPlayer 及监听器</summary>
    partial void DisposePlatform()
    {
        StopForegroundService();
        ReleaseWakeLock();
        AbandonAudioFocus();
        if (_notificationBitmap != null)
        {
            try { _notificationBitmap.Recycle(); } catch { }
            _notificationBitmap = null;
        }
        if (_player != null)
        {
            try
            {
                if (_playerListener != null)
                {
                    _player.RemoveListener(_playerListener);
                    _playerListener.Dispose();
                    _playerListener = null;
                }
                _player.Stop();
                _player.Release();
                _player.Dispose();
            }
            catch { }
            _player = null;
        }
    }

    // ═══════════════════════════════════════
    // ExoPlayer Listener — 准确跟踪播放状态
    // ═══════════════════════════════════════

    /// <summary>
    /// 通过 IPlayerListener 接收 ExoPlayer 状态变化，
    /// 维护 _isPrepared / _isActuallyPlaying，避免依赖 .NET 绑定的 IsPlaying 属性。
    /// Xamarin.AndroidX.Media3.Common 1.10.1 把 Player.IListener 绑定为 IPlayerListener。
    /// </summary>
    private sealed class ExoPlayerListener : Java.Lang.Object, IPlayerListener
    {
        /// <summary>拥有该监听器的 AudioPlayerService 实例</summary>
        private readonly AudioPlayerService _owner;

        /// <summary>构造监听器并关联播放服务实例</summary>
        /// <param name="owner">拥有该监听器的 AudioPlayerService 实例</param>
        public ExoPlayerListener(AudioPlayerService owner) => _owner = owner;

        /// <summary>播放状态变化回调：在 STATE_READY 时推送时长与位置，在 STATE_ENDED 时触发完成事件并停止服务</summary>
        /// <param name="playbackState">播放状态值，STATE_IDLE=1, STATE_BUFFERING=2, STATE_READY=3, STATE_ENDED=4</param>
        public void OnPlaybackStateChanged(int playbackState)
        {
            // STATE_IDLE=1, STATE_BUFFERING=2, STATE_READY=3, STATE_ENDED=4
            _owner._isPrepared = playbackState == 3 || playbackState == 4;
            if (playbackState == 4)
            {
                _owner._isActuallyPlaying = false;
                // 立即触发 PlaybackCompleted，不等待定时器
                _owner._mainHandler.Post(() =>
                {
                    _owner.PlaybackCompleted?.Invoke(_owner, EventArgs.Empty);
                    _owner.PlaybackStateChanged?.Invoke(_owner, false);
                    _owner.StopPositionTimer();
                    _owner.StopForegroundService();
                    _owner.ReleaseWakeLock();
                    _owner.AbandonAudioFocus();
                });
            }
            else if (playbackState == 3)
            {
                // STATE_READY: 主动推送 Duration 和初始 Position，避免依赖 timer 轮询
                _owner._mainHandler.Post(() =>
                {
                    try
                    {
                        var dur = _owner._player?.Duration ?? 0;
                        var pos = _owner._player?.CurrentPosition ?? 0;
                        System.Diagnostics.Debug.WriteLine($"[ExoPlayer] STATE_READY: Duration={dur}ms, Position={pos}ms");
                        if (dur > 0)
                        {
                            _owner.DurationChanged?.Invoke(_owner, dur / 1000.0);
                            _owner.PositionChanged?.Invoke(_owner, TimeSpan.FromSeconds(pos / 1000.0));
                        }
                    }
                    catch { }
                });
            }
            System.Diagnostics.Debug.WriteLine($"[ExoPlayer] State={playbackState} prepared={_owner._isPrepared}");
        }

        /// <summary>IsPlaying 状态变化回调：同步更新真实播放状态，并通知上层 PlaybackStateChanged</summary>
        /// <param name="isPlaying">是否正在播放</param>
        public void OnIsPlayingChanged(bool isPlaying)
        {
            _owner._isActuallyPlaying = isPlaying;
            System.Diagnostics.Debug.WriteLine($"[ExoPlayer] IsPlaying={isPlaying}");
            // 同步通知上层 PlaybackStateChanged
            try { _owner.PlaybackStateChanged?.Invoke(_owner, isPlaying); }
            catch { }
            if (isPlaying)
            {
                _owner._mainHandler.Post(() =>
                {
                    _owner.StartPositionTimer();
                    _owner.AcquireWakeLock();
                    _owner.StartForegroundService();
                });
            }
            else
            {
                _owner._mainHandler.Post(() => _owner.StopPositionTimer());
            }
        }

        /// <summary>PlayWhenReady 变化回调：当 PlayWhenReady=false 时立即标记为未播放，避免在 buffering 期间误判</summary>
        /// <param name="playWhenReady">是否准备好播放</param>
        /// <param name="reason">变化原因</param>
        public void OnPlayWhenReadyChanged(bool playWhenReady, int reason)
        {
            // 如果 playWhenReady=false 但仍在 buffering，IsPlaying 也会变 false
            if (!playWhenReady)
            {
                _owner._isActuallyPlaying = false;
            }
        }

        // 其余 IPlayerListener 方法使用接口默认实现（Java default methods）
        // .NET 绑定会将未实现的方法自动路由到默认实现
    }

    // ═══════════════════════════════════════
    // Wake Lock
    // ═══════════════════════════════════════

    /// <summary>获取 PARTIAL_WAKE_LOCK 和 Wi-Fi Lock，防止播放期间 CPU/Wi-Fi 进入休眠。若已存在但未持有则重新 Acquire</summary>
    private void AcquireWakeLock()
    {
        if (_wakeLock == null)
        {
            var ctx = _androidContext ?? global::Android.App.Application.Context;
            var pm = (PowerManager?)ctx.GetSystemService(Context.PowerService);
            _wakeLock = pm?.NewWakeLock(WakeLockFlags.Partial, "CatClaw:Playback");
            if (_wakeLock != null)
                _wakeLock.SetReferenceCounted(false);
        }
        if (_wakeLock?.IsHeld == false)
        {
            try { _wakeLock.Acquire(); } catch { }
        }

        if (_wifiLock == null)
        {
            var ctx = _androidContext ?? global::Android.App.Application.Context;
            var wm = (WifiManager?)ctx.GetSystemService(Context.WifiService);
            _wifiLock = wm?.CreateWifiLock("CatClaw:WifiPlayback");
            if (_wifiLock != null)
                _wifiLock.SetReferenceCounted(false);
        }
        if (_wifiLock?.IsHeld == false)
        {
            try { _wifiLock.Acquire(); } catch { }
        }
    }

    /// <summary>释放持有的唤醒锁和 Wi-Fi 锁（若已持有）</summary>
    private void ReleaseWakeLock()
    {
        if (_wakeLock?.IsHeld == true)
        {
            try { _wakeLock.Release(); } catch { }
        }
        if (_wifiLock?.IsHeld == true)
        {
            try { _wifiLock.Release(); } catch { }
        }
    }

    // ═══════════════════════════════════════
    // Audio Focus
    // ═══════════════════════════════════════

    /// <summary>请求音频焦点（Gain 模式），用于在播放期间独占音频输出</summary>
    private void RequestAudioFocus()
    {
        if (_audioManager == null || _focusListener == null) return;
        try
        {
            _audioManager.RequestAudioFocus(
                _focusListener, global::Android.Media.Stream.Music, AudioFocus.Gain);
        }
        catch { }
    }

    /// <summary>放弃音频焦点并重置因焦点失去而暂停的标记</summary>
    private void AbandonAudioFocus()
    {
        if (_audioManager == null || _focusListener == null) return;
        try
        {
            _audioManager.AbandonAudioFocus(_focusListener);
            _pausedByFocusLoss = false;
        }
        catch { }
    }

    /// <summary>处理音频焦点变化：根据焦点类型决定继续播放、暂停、降低音量或停止</summary>
    /// <param name="focusChange">音频焦点变化类型</param>
    internal void HandleAudioFocusChange(AudioFocus focusChange)
    {
        switch (focusChange)
        {
            case AudioFocus.Gain:
                if (_pausedByFocusLoss)
                {
                    _pausedByFocusLoss = false;
                    _ = ResumeAsync();
                }
                // 恢复因 Duck 降低的音量（_volume 字段保存的是用户设置的原始音量）
                if (_player != null && _player.Volume < _volume)
                {
                    try { _player.Volume = _volume; } catch { }
                }
                break;
            case AudioFocus.Loss:
                _pausedByFocusLoss = false;
                _ = PauseAsync();
                break;
            case AudioFocus.LossTransient:
                if (IsPlaying) { _pausedByFocusLoss = true; _ = PauseAsync(); }
                break;
            case AudioFocus.LossTransientCanDuck:
                // Duck：降低音量但不暂停，_volume 字段保持原值以便恢复
                if (_player != null)
                {
                    try { _player.Volume = _volume * 0.3f; } catch { }
                }
                break;
        }
    }
    // ═══════════════════════════════════════
    // Audio Focus Listener
    // ═══════════════════════════════════════

    /// <summary>音频焦点变化监听器：将系统回调转发到主线程，由 AudioPlayerService 处理</summary>
    private class AudioFocusListener : Java.Lang.Object, AudioManager.IOnAudioFocusChangeListener
    {
        /// <summary>拥有该监听器的 AudioPlayerService 实例</summary>
        private readonly AudioPlayerService _s;
        /// <summary>构造监听器并关联播放服务实例</summary>
        /// <param name="s">拥有该监听器的 AudioPlayerService 实例</param>
        public AudioFocusListener(AudioPlayerService s) => _s = s;
        /// <summary>系统音频焦点变化回调，转发到主线程处理</summary>
        /// <param name="focusChange">音频焦点变化类型</param>
        public void OnAudioFocusChange(AudioFocus focusChange)
        {
            _s._mainHandler.Post(() => _s.HandleAudioFocusChange(focusChange));
        }
    }

    // ═══════════════════════════════════════
    // 前台服务
    // ═══════════════════════════════════════

    /// <summary>当前歌曲标题（用于前台通知）</summary>
    private string _currentTitle = "";
    /// <summary>当前歌曲艺术家（用于前台通知）</summary>
    private string _currentArtist = "";
    /// <summary>当前歌曲是否已收藏（用于前台通知）</summary>
    private bool _currentIsFavorite;
    /// <summary>当前歌曲封面本地路径（用于前台通知）</summary>
    private string? _currentCoverPath;
    /// <summary>缓存的通知栏封面Bitmap，避免重复解码造成内存泄漏</summary>
    private Android.Graphics.Bitmap? _notificationBitmap;
    /// <summary>上次用于通知栏的封面路径，用于判断是否需要重新解码</summary>
    private string? _lastNotifCoverPath;

    /// <summary>更新当前歌曲信息并刷新前台通知显示</summary>
    /// <param name="title">歌曲标题</param>
    /// <param name="artist">歌曲艺术家</param>
    public void UpdateSongInfo(string title, string artist)
    {
        _currentTitle = title;
        _currentArtist = artist;
        UpdateForegroundNotification();
    }

    /// <summary>更新当前歌曲收藏状态并刷新前台通知</summary>
    /// <param name="isFavorite">是否已收藏</param>
    public void UpdateFavoriteState(bool isFavorite)
    {
        _currentIsFavorite = isFavorite;
        UpdateForegroundNotification();
    }

    /// <summary>更新当前歌曲封面路径并刷新前台通知</summary>
    /// <param name="coverPath">封面本地文件路径，为 null 表示无封面</param>
    public void UpdateCoverPath(string? coverPath)
    {
        _currentCoverPath = coverPath;
        UpdateForegroundNotification();
    }

    /// <summary>启动前台播放服务</summary>
    private void StartForegroundService()
    {
        var ctx = _androidContext ?? global::Android.App.Application.Context;

        // Android 13+ 必须在运行时授予 POST_NOTIFICATIONS，否则 StartForeground 会抛 SecurityException，
        // 导致整个播放通知（含媒体控件）都不显示。在启动前台服务前尽量申请该权限（失败不影响播放流程）。
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            _ = Microsoft.Maui.ApplicationModel.MainThread.InvokeOnMainThreadAsync(RequestNotificationPermissionAsync);
        }

        try { ForegroundPlayerService.Start(ctx, _currentTitle, _currentArtist); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AudioPlayer] FG start: {ex.Message}"); }
    }

    /// <summary>Android 13+ 申请通知权限（POST_NOTIFICATIONS），已授权则跳过；异常不影响播放</summary>
    private async Task RequestNotificationPermissionAsync()
    {
        try
        {
            var status = await Microsoft.Maui.ApplicationModel.Permissions
                .CheckStatusAsync<Microsoft.Maui.ApplicationModel.Permissions.PostNotifications>();
            if (status != Microsoft.Maui.ApplicationModel.PermissionStatus.Granted)
            {
                await Microsoft.Maui.ApplicationModel.Permissions
                    .RequestAsync<Microsoft.Maui.ApplicationModel.Permissions.PostNotifications>();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioPlayer] 通知权限申请异常: {ex.Message}");
        }
    }

    /// <summary>停止前台播放服务</summary>
    private void StopForegroundService()
    {
        var ctx = _androidContext ?? global::Android.App.Application.Context;
        try { ForegroundPlayerService.Stop(ctx); } catch { }
    }

    /// <summary>更新前台通知的播放状态与歌曲信息</summary>
    private void UpdateForegroundNotification()
    {
        try
        {
            Android.Graphics.Bitmap? albumArt = null;
            if (!string.IsNullOrEmpty(_currentCoverPath))
            {
                if (_currentCoverPath != _lastNotifCoverPath)
                {
                    try
                    {
                        _notificationBitmap?.Recycle();
                        _notificationBitmap = null;
                        _notificationBitmap = DecodeBitmapDownsampled(
                            global::Android.Graphics.BitmapFactory.DecodeFile(_currentCoverPath), 512);
                    }
                    catch { }
                    _lastNotifCoverPath = _currentCoverPath;
                }
                albumArt = _notificationBitmap;
            }
            else
            {
                if (_notificationBitmap != null)
                {
                    _notificationBitmap.Recycle();
                    _notificationBitmap = null;
                    _lastNotifCoverPath = null;
                }
            }
            long positionMs = 0;
            long durationMs = 0;
            try
            {
                positionMs = (long)(CurrentPosition * 1000);
                durationMs = (long)(Duration * 1000);
            }
            catch { }
            ForegroundPlayerService.UpdatePlayState(_currentTitle, _currentArtist, IsPlaying, _currentIsFavorite, albumArt, positionMs, durationMs);
        }
        catch { }
    }

    /// <summary>仅更新通知栏进度条位置，避免频繁重建通知</summary>
    private void UpdateNotificationProgress()
    {
        try
        {
            long positionMs = (long)(CurrentPosition * 1000);
            ForegroundPlayerService.UpdatePlayPosition(positionMs);
        }
        catch { }
    }

    /// <summary>前台通知"播放/暂停"按钮回调：在主线程切换播放状态</summary>
    /// <param name="shouldPlay">true 表示请求播放，false 表示请求暂停</param>
    private void OnNotifPlayPauseRequested(bool shouldPlay)
    {
        _mainHandler.Post(async () =>
        {
            try
            {
                if (shouldPlay)
                    await ResumeAsync();
                else
                    await PauseAsync();
            }
            catch { }
        });
    }

    /// <summary>前台通知"下一首"按钮回调：在主线程触发 PlayNextRequested 事件</summary>
    private void OnNotifNextRequested()
    {
        _mainHandler.Post(async () =>
        {
            try
            {
                if (PlayNextRequested != null)
                    await PlayNextRequested.Invoke();
            }
            catch { }
        });
    }

    /// <summary>前台通知"上一首"按钮回调：在主线程触发 PlayPreviousRequested 事件</summary>
    private void OnNotifPreviousRequested()
    {
        _mainHandler.Post(async () =>
        {
            try
            {
                if (PlayPreviousRequested != null)
                    await PlayPreviousRequested.Invoke();
            }
            catch { }
        });
    }

    /// <summary>前台通知"歌词"按钮回调：切换桌面歌词开关</summary>
    /// <param name="isEnabled">桌面歌词目标状态</param>
    private void OnNotifLyricsRequested(bool isEnabled)
    {
        _mainHandler.Post(() =>
        {
            try
            {
                DesktopLyricToggled?.Invoke(isEnabled);
            }
            catch (Exception ex)
            {
                Android.Util.Log.Error("AudioPlayerSvc", $"OnNotifLyricsRequested failed: {ex.GetType().Name}: {ex.Message}");
            }
        });
    }

    /// <summary>前台通知"收藏"按钮回调：同步内部收藏状态并在主线程触发 FavoriteToggled 事件</summary>
    /// <param name="isFavorite">收藏按钮最新状态</param>
    private void OnNotifFavoriteToggled(bool isFavorite)
    {
        // 同步内部状态，避免后续 UpdateForegroundNotification 用旧值覆盖通知栏收藏状态
        _currentIsFavorite = isFavorite;
        _mainHandler.Post(async () =>
        {
            try
            {
                FavoriteToggled?.Invoke(isFavorite);
            }
            catch { }
        });
    }

    /// <summary>将Bitmap降采样到指定最大尺寸，减少通知栏封面内存占用；源Bitmap会被回收</summary>
    /// <param name="source">原始Bitmap</param>
    /// <param name="maxSize">目标最大边长（像素）</param>
    /// <returns>降采样后的新Bitmap</returns>
    private static Android.Graphics.Bitmap? DecodeBitmapDownsampled(Android.Graphics.Bitmap? source, int maxSize)
    {
        if (source == null) return null;
        try
        {
            int width = source.Width;
            int height = source.Height;
            if (width <= 0 || height <= 0) { source.Recycle(); return null; }
            float scale = Math.Min((float)maxSize / width, (float)maxSize / height);
            if (scale >= 1.0f) return source;
            var result = Android.Graphics.Bitmap.CreateScaledBitmap(source, (int)(width * scale), (int)(height * scale), true);
            if (!ReferenceEquals(result, source)) source.Recycle();
            return result;
        }
        catch
        {
            source.Recycle();
            return null;
        }
    }
}
