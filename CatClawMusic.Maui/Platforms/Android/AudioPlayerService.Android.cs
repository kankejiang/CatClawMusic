using Android.Content;
using Android.Media;
using Android.Net.Wifi;
using Android.OS;
using AndroidX.Media3.Common;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Maui.Platforms.Android;
using CatClawMusic.Maui.Services.Equalizer;
using ALog = Android.Util.Log;
using SimpleExoPlayer = AndroidX.Media3.ExoPlayer.SimpleExoPlayer;
namespace CatClawMusic.Maui.Services;

/// <summary>基于 Media3 ExoPlayer + FFmpeg 的 Android 音频播放服务，提供音频播放、暂停、跳转、音量控制及前台通知、音频焦点、唤醒锁等能力</summary>
public partial class AudioPlayerService
{
    /// <summary>ExoPlayer 播放器实例</summary>
    private SimpleExoPlayer? _player;
    /// <summary>当前音量（0.0 ~ 1.0）</summary>
    private float _volume = 1.0f;
    /// <summary>淡入淡出当前系数（0.0 ~ 1.0），与 _volume 相乘得到实际播放音量</summary>
    private float _xfadeFactor = 1.0f;
    /// <summary>标记全局 Authenticator 是否已注册（仅注册一次）</summary>
    private static bool _authenticatorRegistered;
    /// <summary>当前播放歌曲的 Basic Auth 用户名（从 URL userinfo 提取）</summary>
    private static string? _currentAuthUser;
    /// <summary>当前播放歌曲的 Basic Auth 密码（从 URL userinfo 提取）</summary>
    private static string? _currentAuthPass;
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
    /// <summary>当前曲目的原始播放源 URI（file:// 或网络地址），用于 FFmpeg 模式重新烘焙时就地重载</summary>
    private Uri? _currentSourceUri;
    /// <summary>FFmpeg 实时均衡器防抖定时器：连续拖动滑块时合并为一次重新转码重载</summary>
    private System.Threading.Timer? _eqReapplyTimer;
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
    /// <summary>播放操作串行化锁：连续切歌时旧 PlayInternalAsync 持锁，新任务等待；
    /// 配合 _playCts 让旧任务在 await 点主动退出，避免并发操作 ExoPlayer 触发 native 崩溃。</summary>
    private readonly SemaphoreSlim _playLock = new(1, 1);
    /// <summary>当前 PlayInternalAsync 的取消令牌源；切歌时 Cancel 让旧任务在 await 点抛 OperationCanceledException 退出。</summary>
    private CancellationTokenSource? _playCts;
    /// <summary>最近一次 OnPlayerError 时间戳（Ticks），用于退避避免错误后立即切歌形成崩溃循环。</summary>
    private long _lastPlayerErrorTicks;
    /// <summary>通知栏 Bitmap 操作锁，防止 Recycle 与通知系统使用竞态导致 native SIGSEGV。</summary>
    private readonly object _notifBitmapLock = new();

    // Audio focus listener
    /// <summary>音频焦点变化监听器实例</summary>
    private AudioFocusListener? _focusListener;

    /// <summary>Android 原生均衡器/低音增强/响度增强服务</summary>
    private AndroidEqualizerService? _eqService;

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
        // MediaSession.SetPlaybackState 是到 system_server 的 binder IPC，MIUI 上系统繁忙时
        // 可阻塞主线程数百毫秒（logcat 里每秒一次的 PlatformDispatcher wall=300~500ms），
        // 是播放页左右滑动掉帧的主因之一。MediaSession 本身线程安全，移到后台线程推送；
        // 位置直接用事件参数，避免后台线程访问 ExoPlayer。
        var posMs = (long)e.TotalMilliseconds;
        Task.Run(() => { try { ForegroundPlayerService.UpdatePlayPosition(posMs); } catch { } });
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

        // 注册全局 Authenticator，让 HttpURLConnection 在收到 401 时从 URL 的 userinfo 提取认证信息
        // ExoPlayer 的 DefaultHttpDataSource 内部使用 HttpURLConnection，不解析 URL 中的 user:pass@
        if (!_authenticatorRegistered)
        {
            Java.Net.Authenticator.SetDefault(new WebDavAuthenticator());
            _authenticatorRegistered = true;
        }

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

        // 挂载原生均衡器到 ExoPlayer 音频会话（实时处理所有解码后音频）
        // FFmpeg 模式（10 段）下不挂原生 EQ，改由 FFmpeg 把均衡器烘焙进音频，避免双重 EQ
        try
        {
            if (!EqualizerSettings.UseFFmpegEq)
            {
                _eqService ??= new AndroidEqualizerService();
                var sessionId = ((AndroidX.Media3.ExoPlayer.IExoPlayer)_player).AudioSessionId;
                _eqService.AttachToSession(sessionId);
            }
        }
        catch (Exception ex)
        {
            ALog.Warn("AudioPlayerService.Android", $"[ExoPlayer] EQ 挂载失败: {ex.Message}");
        }

        return _player;
    }

    // ═══════════════════════════════════════
    // Media3 流式磁盘缓存（仅网络音频）
    // ═══════════════════════════════════════

    /// <summary>Media3 流式缓存实例（懒加载、线程安全）。仅缓存网络音频的播放流，本地文件不走此缓存。</summary>
    private static AndroidX.Media3.DataSource.Cache.SimpleCache? _mediaCache;
    private static readonly object _mediaCacheLock = new();

    /// <summary>
    /// 懒加载 Media3 SimpleCache（LRU 淘汰）。须在后台线程首次触发以避免主线程 I/O；
    /// 这里在播放线程（PlayInternalAsync 已 Task.Run 化）调用，SimpleCache 构造本身很快。
    /// </summary>
    private static AndroidX.Media3.DataSource.Cache.SimpleCache EnsureMediaCache()
    {
        if (_mediaCache != null) return _mediaCache;
        lock (_mediaCacheLock)
        {
            if (_mediaCache != null) return _mediaCache;
            var ctx = global::Android.App.Application.Context;
            var cacheDir = new Java.IO.File(ctx.CacheDir, "media3_stream");
            // 复用 AudioCacheService 的用户可配置上限，避免额外设置项
            long maxBytes;
            try { maxBytes = AudioCacheService.Instance.CacheSizeLimitBytes; }
            catch { maxBytes = (long)AudioCacheService.DefaultCacheSizeMB * 1024 * 1024; }
            var evictor = new AndroidX.Media3.DataSource.Cache.LeastRecentlyUsedCacheEvictor(maxBytes);
            var dbProvider = new AndroidX.Media3.Database.StandaloneDatabaseProvider(ctx);
            _mediaCache = new AndroidX.Media3.DataSource.Cache.SimpleCache(cacheDir, evictor, dbProvider);
            return _mediaCache;
        }
    }

    /// <summary>
    /// 为网络音频（http/https，含 SMB 本地代理与 WebDAV/Alist）创建带渐进式磁盘缓存的 ProgressiveMediaSource。
    /// 上游用 DefaultHttpDataSource（其内部 HttpURLConnection 仍受全局 WebDavAuthenticator 处理 Basic Auth）；
    /// FLAG_IGNORE_CACHE_ON_ERROR 保证缓存读写异常时自动回退网络，不阻断播放。失败返回 null（调用方回退默认源）。
    /// </summary>
    private static AndroidX.Media3.ExoPlayer.Source.IMediaSource? CreateNetworkMediaSource(global::Android.Net.Uri uri)
    {
        try
        {
            var cache = EnsureMediaCache();
            var upstream = new AndroidX.Media3.DataSource.DefaultHttpDataSource.Factory();
            var cacheFactory = new AndroidX.Media3.DataSource.Cache.CacheDataSource.Factory()
                .SetCache(cache)
                .SetUpstreamDataSourceFactory(upstream)
                .SetFlags(AndroidX.Media3.DataSource.Cache.CacheDataSource.FlagIgnoreCacheOnError);
            // 自定义缓存键：去掉查询串（Alist 的 sign/token 会过期），使同一首歌的不同签名 URL 共享缓存条目
            var mediaItem = new MediaItem.Builder()
                .SetUri(uri)
                .SetCustomCacheKey(BuildCacheKey(uri.ToString() ?? ""))
                .Build();
            return new AndroidX.Media3.ExoPlayer.Source.ProgressiveMediaSource.Factory(cacheFactory)
                .CreateMediaSource(mediaItem);
        }
        catch (Exception ex)
        {
            ALog.Warn("AudioPlayerService.Android", $"[ExoPlayer] 缓存源创建失败，回退默认: {ex.Message}");
            return null;
        }
    }

    /// <summary>从 URL 派生稳定缓存键：剥离查询串（sign/token 等易变参数），保留 scheme+host+path（含 userinfo）。</summary>
    private static string BuildCacheKey(string url)
    {
        var q = url.IndexOf('?');
        return q >= 0 ? url.Substring(0, q) : url;
    }

    // ═══════════════════════════════════════
    // 播放核心
    // ═══════════════════════════════════════

    /// <summary>平台特定的播放入口，由跨平台 AudioPlayerService 调用</summary>
    /// <param name="source">音频源 URI</param>
    partial void PlatformPlay(Uri source)
    {
        // 标记未准备，使 GetPlatformCurrentPositionSeconds/Duration 在切歌间隙返回 0，
        // 避免把上一首的实时进度/时长透传到通知栏与 MediaSession（锁屏旧时间）。
        _isPrepared = false;
        _cachedPositionMs = 0;
        // 取消上一次 PlayInternalAsync（若仍在 await FFmpeg/网络），并启动新任务；
        // _playLock 保证新旧任务不会并发操作 ExoPlayer。
        _playCts?.Cancel();
        _playCts?.Dispose();
        _playCts = new CancellationTokenSource();
        var ct = _playCts.Token;
        _ = PlayInternalAsync(source, autoPlay: true, ct: ct);
    }

    /// <summary>执行实际的播放流程：重置状态、必要时通过 FFmpeg 转码、设置媒体项、Prepare 并启动前台服务</summary>
    /// <param name="source">音频源 URI</param>
    /// <param name="autoPlay">是否在 Prepare 完成后立即开始播放</param>
    /// <param name="startPositionSec">起始播放位置（秒），用于 FFmpeg 模式重新烘焙时从原进度无缝重载</param>
    /// <param name="ct">取消令牌：连续切歌时旧任务会被取消，在 await 点抛 OperationCanceledException 退出</param>
    private async Task PlayInternalAsync(Uri source, bool autoPlay, double startPositionSec = 0, CancellationToken ct = default)
    {
        // 串行化：旧任务持锁时新任务在此等待；旧任务最终释放锁后新任务进入，
        // 但旧任务此时已在 await 点被 ct 取消，不会继续操作 ExoPlayer。
        await _playLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();

        // 重置状态：在切换歌曲期间，IsPlaying 应返回 false，
        // 防止 _positionTimer 在 Prepare 未完成时反复拉到 0 位置
        _isPrepared = false;
        _isActuallyPlaying = false;
        _cachedPositionMs = 0;
        _lastSeekTicks = 0;
        _currentPath = source.ToString();
        _currentSourceUri = source; // 记录原始源，供重新烘焙/重载使用
        // 重新烘焙重载（startPositionSec>0）时，用续播位置填充缓存，
        // 避免转码期间进度条回弹到 0（普通切歌则保持 0，不显示上一首旧进度）
        if (startPositionSec > 0)
            _cachedPositionMs = (long)(startPositionSec * 1000);

        // 从 URL userinfo (user:pass@host) 提取 Basic Auth 凭证，供全局 Authenticator 使用
        // ExoPlayer 的 DefaultHttpDataSource 不解析 URL userinfo，需通过 Authenticator 在 401 时提供
        try
        {
            var userInfo = source.UserInfo;
            if (!string.IsNullOrEmpty(userInfo))
            {
                var parts = userInfo.Split(':');
                if (parts.Length >= 2)
                {
                    _currentAuthUser = Uri.UnescapeDataString(parts[0]);
                    _currentAuthPass = Uri.UnescapeDataString(parts[1]);
                }
                else
                {
                    _currentAuthUser = null;
                    _currentAuthPass = null;
                }
            }
            else
            {
                _currentAuthUser = null;
                _currentAuthPass = null;
            }
        }
        catch
        {
            _currentAuthUser = null;
            _currentAuthPass = null;
        }

        try
        {
            var player = EnsurePlayer();
            // ExoPlayer 要求所有方法在创建它的线程（主线程）调用。
            // WebDAV/SMB 路径中 await AsyncUrlResolver 后 continuation 可能在线程池线程，
            // 因此所有 player 操作必须 Post 到主线程执行，否则触发 native 数据竞争。
            // ct 在 Post 内部检查，确保等待主线程期间被取消的任务不会继续操作 player。
            await PostToMainThreadAsync(() =>
            {
                ct.ThrowIfCancellationRequested();
                try { player.Stop(); } catch (Exception ex) { Log.Debug("AudioPlayerService.Android", $"[ExoPlayer] player.Stop error: {ex.Message}"); }
                try { player.ClearMediaItems(); } catch (Exception ex) { Log.Debug("AudioPlayerService.Android", $"[ExoPlayer] ClearMediaItems error: {ex.Message}"); }
            }, ct).ConfigureAwait(false);

            var playUri = source;
            var localPath = source.IsFile ? source.LocalPath :
                source.Scheme == "file" ? source.AbsolutePath : null;

            // FFmpeg 模式（10 段）：均衡器由 FFmpeg 烘焙进音频，需强制转码；此时不挂原生 EQ
            var ffmpegEqMode = EqualizerSettings.UseFFmpegEq && EqualizerSettings.Enabled;
            // FFmpeg 工作模式（均衡器页统一配置）：
            //  - 自动：仅 m4a/mp4 等 ExoPlayer 原生解码不完整的格式走 FFmpeg 软解
            //    （MIUI/HyperOS 上 m4a 可能 prepare 成功但实际无声）
            //  - 开启：ffmpegEqMode 为 true，所有本地音频强制转码并烘焙 10 段 EQ
            if (localPath != null && (NeedsTranscoding(localPath) || ffmpegEqMode))
            {
                // 确保 FFmpeg 已初始化（首次播放时 Task.Run 注入可能尚未完成）
                if (_ffmpeg == null || !_ffmpeg.IsAvailable)
                {
                    await EnsureFFmpegReadyAsync(ct).ConfigureAwait(false);
                }

                if (_ffmpeg != null && _ffmpeg.IsAvailable)
                {
                    Log.Debug("AudioPlayerService.Android", $"[ExoPlayer] FFmpeg 转码: {Path.GetFileName(localPath)}");
                    // FFmpeg 模式：始终把均衡器滤镜（10 段）烘焙进转码；否则仅原生 EQ 不可用时兜底烘焙
                    var eqFilter = ffmpegEqMode || _eqService?.IsAttached != true
                        ? EqualizerSettings.BuildFFmpegFilterChain()
                        : "";
                    var wavPath = await _ffmpeg.TranscodeToWavAsync(localPath, eqFilter).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    if (wavPath != null)
                    {
                        playUri = new Uri("file://" + wavPath);
                        Log.Debug("AudioPlayerService.Android", "[ExoPlayer] FFmpeg 转码完成，使用 WAV 播放");
                    }
                    else
                    {
                        Log.Debug("AudioPlayerService.Android", "[ExoPlayer] FFmpeg 转码失败，回退原生播放");
                    }
                }
                else
                {
                    Log.Debug("AudioPlayerService.Android", "[ExoPlayer] FFmpeg 不可用，回退原生播放");
                }
            }

            var androidUri = global::Android.Net.Uri.Parse(playUri.ToString());
            // 网络音频（http/https，含 SMB 本地代理与 WebDAV/Alist）走 Media3 渐进式磁盘缓存，
            // 实现边下边播 + seek 命中缓存；本地文件（file:///content://）保持默认直读，不写入流缓存。
            AndroidX.Media3.ExoPlayer.Source.IMediaSource? cachedSource = null;
            if (playUri.Scheme == "http" || playUri.Scheme == "https")
                cachedSource = CreateNetworkMediaSource(androidUri);

            // ExoPlayer 状态变更必须在主线程执行
            await PostToMainThreadAsync(() =>
            {
                ct.ThrowIfCancellationRequested();
                if (cachedSource != null)
                    player.SetMediaSource(cachedSource);
                else
                    player.SetMediaItem(MediaItem.FromUri(androidUri));
                player.Prepare();
                // FFmpeg 模式重新烘焙时，从原进度无缝续播（startPositionSec>0 才跳转，0 表示从头播放）
                if (startPositionSec > 0.3)
                {
                    try { player.SeekTo((long)(startPositionSec * 1000)); } catch (Exception ex) { Log.Debug("AudioPlayerService.Android", $"[ExoPlayer] SeekTo error: {ex.Message}"); }
                }
                player.PlayWhenReady = autoPlay;
                if (autoPlay) player.Play();
            }, ct).ConfigureAwait(false);

            // 原生（非 FFmpeg）模式：确保原生均衡器已挂载并对当前设置生效。
            // 首次播放由 EnsurePlayer 挂载；此处覆盖「FFmpeg→原生」切换重载、以及重载后重新应用，
            // 保证重新烘焙/重载后原生 EQ 立即生效。
            if (!EqualizerSettings.UseFFmpegEq)
            {
                try
                {
                    if (_eqService == null && _player != null)
                    {
                        _eqService = new AndroidEqualizerService();
                        var sessionId = ((AndroidX.Media3.ExoPlayer.IExoPlayer)_player).AudioSessionId;
                        _eqService.AttachToSession(sessionId);
                    }
                    _eqService?.ApplySettings();
                }
                catch (Exception ex)
                {
                    ALog.Warn("AudioPlayerService.Android", $"[ExoPlayer] 重载后应用原生 EQ 失败: {ex.Message}");
                }
            }

            // 不在这里设置 _isPrepared，由 ExoPlayerListener.OnPlaybackStateChanged(STATE_READY) 触发
            AcquireWakeLock();
            // 音频焦点由 ExoPlayer.SetAudioAttributes(handleAudioFocus=true) 自动管理，无需手动请求
            StartForegroundService();
        }
        catch (System.OperationCanceledException)
        {
            Log.Debug("AudioPlayerService.Android", "[ExoPlayer] PlayInternalAsync 已被取消（连续切歌）");
            throw;
        }
        catch (Exception ex)
        {
            Log.Debug("AudioPlayerService.Android", $"[ExoPlayer] Play error: {ex.Message}");
            // FFmpeg 兜底：如果 ExoPlayer 直接播放失败，尝试转码
            await TryFFmpegFallbackAsync(source);
        }
        }
        finally
        {
            _playLock.Release();
        }
    }

    /// <summary>将操作投递到主线程 Looper 执行；若已在主线程则同步执行。支持取消令牌。</summary>
    /// <param name="action">要在主线程执行的操作</param>
    /// <param name="ct">取消令牌</param>
    private Task PostToMainThreadAsync(Action action, CancellationToken ct = default)
    {
        // 已在主线程：直接执行（仍尊重 ct）
        if (Looper.MyLooper() == Looper.MainLooper)
        {
            ct.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }
        // 否则 Post 到主线程并 await 完成
        var tcs = new TaskCompletionSource<bool>();
        _mainHandler.Post(() =>
        {
            try
            {
                if (!ct.IsCancellationRequested)
                    action();
                tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        return tcs.Task;
    }

    /// <summary>确保 FFmpegService 已初始化并注入（解决启动时 Task.Run 注入时序问题）</summary>
    /// <param name="ct">取消令牌</param>
    private async Task EnsureFFmpegReadyAsync(CancellationToken ct = default)
    {
        if (_ffmpeg != null && _ffmpeg.IsAvailable) return;
        try
        {
            _ffmpeg ??= new FFmpegService();
            await _ffmpeg.InitializeAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            Log.Debug("AudioPlayerService.Android", $"[ExoPlayer] FFmpeg 就绪: {_ffmpeg.IsAvailable}");
        }
        catch (System.OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Debug("AudioPlayerService.Android", $"[ExoPlayer] FFmpeg 初始化异常: {ex.Message}");
        }
    }

    /// <summary>ExoPlayer 播放失败后的 FFmpeg 兜底：将原文件转码为 WAV 后再次尝试播放</summary>
    /// <param name="source">原始音频源 URI</param>
    private async Task TryFFmpegFallbackAsync(Uri source)
    {
        var localPath = source.IsFile ? source.LocalPath :
            source.Scheme == "file" ? source.AbsolutePath : null;
        // FFmpeg 兜底不再受旧"软解码"开关限制：ExoPlayer 解码失败时总是尝试转码（自动模式的兜底语义）
        if (localPath == null || _ffmpeg == null || !_ffmpeg.IsAvailable) return;

        try
        {
            Log.Debug("AudioPlayerService.Android", "[ExoPlayer] 尝试 FFmpeg 兜底转码...");
            var wavPath = await _ffmpeg.TranscodeToWavAsync(localPath).ConfigureAwait(false);
            if (wavPath == null) return;

            var player = EnsurePlayer();
            var mediaItem = MediaItem.FromUri(global::Android.Net.Uri.Parse("file://" + wavPath));
            // ExoPlayer 操作必须在主线程
            await PostToMainThreadAsync(() =>
            {
                try { player.Stop(); } catch { }
                try { player.ClearMediaItems(); } catch { }
                player.SetMediaItem(mediaItem);
                player.Prepare();
                player.Play();
            }).ConfigureAwait(false);
            AcquireWakeLock();
            // 音频焦点由 ExoPlayer 自动管理
            StartForegroundService();
            Log.Debug("AudioPlayerService.Android", "[ExoPlayer] FFmpeg 兜底成功");
        }
        catch (Exception ex)
        {
            Log.Debug("AudioPlayerService.Android", $"[ExoPlayer] FFmpeg 兜底失败: {ex.Message}");
        }
    }

    /// <summary>判断指定文件是否需要 FFmpeg 转码（ExoPlayer 原生不支持的格式）</summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>需要转码返回 true，否则返回 false</returns>
    private static bool NeedsTranscoding(string filePath)
    {
        var ext = Path.GetExtension(filePath)?.ToLowerInvariant();
        return ext is ".m4a" or ".m4b" or ".mp4" or ".mov" or ".wma"
            or ".ogg" or ".opus" or ".ape" or ".wv" or ".aiff" or ".aif" or ".alac"
            or ".flac";
    }

    // ═══════════════════════════════════════
    // 播放控制
    // ═══════════════════════════════════════

    /// <summary>平台特定的暂停逻辑：暂停 ExoPlayer、释放唤醒锁（音频焦点由 ExoPlayer 自动管理）</summary>
    partial void PlatformPause()
    {
        try { _player?.Pause(); ReleaseWakeLock(); }
        catch (Exception ex) { Log.Debug("AudioPlayerService.Android", $"[ExoPlayer] PlatformPause error: {ex.Message}"); }
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
        catch (Exception ex) { Log.Debug("AudioPlayerService.Android", $"[ExoPlayer] PlatformResume error: {ex.Message}"); }
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
        catch (Exception ex) { Log.Debug("AudioPlayerService.Android", $"[ExoPlayer] PlatformStop error: {ex.Message}"); }
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
        catch (Exception ex) { Log.Debug("AudioPlayerService.Android", $"[ExoPlayer] PlatformSeek error: {ex.Message}"); }
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
        // 未准备完成时（切歌间隙），返回缓存值（已被 PlayInternalAsync / PlatformPlay 重置为 0），
        // 避免把上一首的实时位置透传到通知栏与 MediaSession，导致锁屏显示旧进度。
        if (!_isPrepared)
            return _cachedPositionMs / 1000.0;
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
        // 未准备完成时返回 0，避免切歌间隙把上一首时长泄露到通知栏与 MediaSession。
        if (!_isPrepared)
            return 0;
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
            Log.Debug("AudioPlayerService.Android", $"[ExoPlayer] GetDuration error: {ex.Message}");
        }
        return 0;
    }

    /// <summary>获取当前音量（0.0 ~ 1.0）</summary>
    /// <returns>当前音量</returns>
    private partial double GetPlatformVolume() => _volume;

    /// <summary>设置当前音量，并同步到 ExoPlayer（叠加淡入淡出系数）</summary>
    /// <param name="volume">音量值（0.0 ~ 1.0）</param>
    partial void SetPlatformVolume(double volume)
    {
        _volume = (float)Math.Clamp(volume, 0.0, 1.0);
        try { if (_player != null) _player.Volume = _volume * _xfadeFactor; } catch { }
    }

    /// <summary>将淡入淡出系数应用到 ExoPlayer 实际音量（_volume × factor）</summary>
    /// <param name="factor">淡入淡出系数 0.0 ~ 1.0</param>
    partial void ApplyCrossfadeVolume(double factor)
    {
        _xfadeFactor = (float)factor;
        try { if (_player != null) _player.Volume = _volume * _xfadeFactor; } catch { }
    }

    /// <summary>将当前均衡器设置应用到原生音效引擎（Equalizer/BassBoost/LoudnessEnhancer）</summary>
    partial void ApplyEqualizerPlatform()
    {
        // FFmpeg 模式（10 段）下均衡器由 FFmpeg 烘焙进音频，不挂原生 EQ
        if (EqualizerSettings.UseFFmpegEq) return;
        try
        {
            if (_eqService == null && _player != null)
            {
                _eqService = new AndroidEqualizerService();
                var sessionId = ((AndroidX.Media3.ExoPlayer.IExoPlayer)_player).AudioSessionId;
                _eqService.AttachToSession(sessionId);
            }
            _eqService?.ApplySettings();
        }
        catch (Exception ex)
        {
            ALog.Warn("AudioPlayerService.Android", $"[ExoPlayer] ApplyEqualizer 失败: {ex.Message}");
        }
    }

    /// <summary>
    /// FFmpeg 模式均衡器实时生效：防抖后重新烘焙当前曲（新滤镜）并从原进度重载。
    /// 防抖避免拖动滑块时每像素都触发一次重转码（重转码耗时数秒）。
    /// 也用于 FFmpeg 开关切换时把当前曲在「原生 ↔ FFmpeg 烘焙」路径间重载。
    /// </summary>
    partial void ReapplyEqualizerLivePlatform()
    {
        _eqReapplyTimer?.Dispose();
        _eqReapplyTimer = new System.Threading.Timer(_ =>
        {
            _eqReapplyTimer = null;
            // 重新烘焙/重载涉及 ExoPlayer 操作，统一切到主线程执行
            _mainHandler.Post(() => _ = ReapplyEqLiveAsync());
        }, null, 500, System.Threading.Timeout.Infinite);
    }

    /// <summary>
    /// 实际重载当前歌曲：捕获当前进度，重新计算 FFmpeg 滤镜并转码，
    /// 用新音频从原进度续播。原生模式则直接重载原始源（原生 EQ 由 PlayInternalAsync 重新挂载应用）。
    /// </summary>
    private bool _isReapplyingEq;
    private async Task ReapplyEqLiveAsync()
    {
        if (_isReapplyingEq) return; // 防止转码进行中重复触发导致状态错乱
        _isReapplyingEq = true;
        try
        {
            var src = _currentSourceUri;
            if (src == null) return;
            var wasPlaying = IsPlaying;
            var posSec = CurrentPosition;
            // 手动 EQ 调整属歌曲中途，重置淡入淡出状态，避免音量卡在低值
            _xfadeState = XfadeIdle;
            _xfadeCur = 1.0;
            ApplyCrossfadeVolume(1.0);

            Log.Debug("AudioPlayerService.Android",
                $"[ExoPlayer] 重新应用均衡器: wasPlaying={wasPlaying}, pos={posSec:F1}s, ffmpeg={EqualizerSettings.UseFFmpegEq}");
            await PlayInternalAsync(src, autoPlay: wasPlaying, startPositionSec: posSec);
        }
        catch (Exception ex)
        {
            Log.Debug("AudioPlayerService.Android", $"[ExoPlayer] 重新应用均衡器失败: {ex.Message}");
        }
        finally
        {
            _isReapplyingEq = false;
        }
    }

    /// <summary>释放平台相关资源：停止前台服务、释放唤醒锁与音频焦点、释放 ExoPlayer 及监听器</summary>
    partial void DisposePlatform()
    {
        // 取消任何进行中的 PlayInternalAsync，避免 Dispose 后仍访问 _player
        try { _playCts?.Cancel(); } catch { }
        StopForegroundService();
        ReleaseWakeLock();
        AbandonAudioFocus();
        _eqService?.Dispose();
        _eqService = null;
        lock (_notifBitmapLock)
        {
            if (_notificationBitmap != null)
            {
                try { _notificationBitmap.Recycle(); } catch { }
                _notificationBitmap = null;
            }
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
            catch (Exception ex) { Log.Debug("AudioPlayerService.Android", $"[ExoPlayer] Dispose player error: {ex.Message}"); }
            _player = null;
        }
        try { _playCts?.Dispose(); } catch { }
        _playCts = null;
        try { _playLock.Dispose(); } catch { }
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
                    _owner.NotifyPlaybackCompleted();
                    _owner.PlaybackStateChanged?.Invoke(_owner, false);
                    _owner.StopPositionTimer();
                    // 注意：【不】在此停止前台服务 / 释放 MediaSession。
                    // 自然播放下一曲时若立即停止前台服务，会销毁 MediaSession 并随后重建，
                    // 导致锁屏媒体控件"先暂停再出现"的闪烁，且锁屏会短暂回显上一首的
                    // 旧标题与旧进度（"时间特别旧"）。下一首 PlayInternalAsync 会复用同一
                    // MediaSession 并刷新元数据/进度，实现无缝切歌、加快切歌速度。
                    // 仅当确实无下一首可播时，才在 LoadCurrentSongAsync 的 song==null 分支调用
                    // StopAndHideNotification() 真正停止前台服务。
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
                        Log.Debug("AudioPlayerService.Android", $"[ExoPlayer] STATE_READY: Duration={dur}ms, Position={pos}ms");
                        if (dur > 0)
                        {
                            _owner.DurationChanged?.Invoke(_owner, dur / 1000.0);
                            _owner.PositionChanged?.Invoke(_owner, TimeSpan.FromSeconds(pos / 1000.0));
                        }
                        // 立即向通知栏/MediaSession 推送完整状态（含准确时长）。
                        // 切歌时 UpdateSongInfo 先于 Prepare 完成执行，彼时 Duration=0，
                        // 若等 OnIsPlayingChanged(true) 才刷新，主线程拥塞期间通知栏会显示
                        // 00:00 数秒。STATE_READY 早于 IsPlaying 回调，在此推送可立即修正。
                        _owner.UpdateForegroundNotification();
                    }
                    catch { }
                });
            }
            Log.Debug("AudioPlayerService.Android", $"[ExoPlayer] State={playbackState} prepared={_owner._isPrepared}");
        }

        /// <summary>IsPlaying 状态变化回调：同步更新真实播放状态，并通知上层 PlaybackStateChanged</summary>
        /// <param name="isPlaying">是否正在播放</param>
        public void OnIsPlayingChanged(bool isPlaying)
        {
            _owner._isActuallyPlaying = isPlaying;
            Log.Debug("AudioPlayerService.Android", $"[ExoPlayer] IsPlaying={isPlaying}");
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
        // 但是 OnPlayerError 必须显式重写：当 ExoPlayer 内部 MediaCodec 解码器失败时
        // （如 logcat 中 flac 解码器 59/60 帧被丢弃），系统不会自动回退，导致：
        //   - 解码器反复重试 → BufferPool 耗尽（1857/1862）
        //   - 堆内存暴涨（7MB → 99MB）→ GC 暂停飙升至 165ms
        //   - 最终触发 Android LMK 杀进程
        public void OnPlayerError(PlaybackException error)
        {
            Log.Debug("AudioPlayerService.Android",
                $"[ExoPlayer] OnPlayerError: {error.ErrorCodeName} — {error.Message}");
            try
            {
                // 退避：5 秒内连续 OnPlayerError 不再自动切下一首，避免竞态引起的崩溃循环。
                // 竞态导致的错误会在切歌后立即触发；连续切歌会持续触发错误形成循环。
                var nowTicks = System.Environment.TickCount64;
                var lastTicks = Interlocked.Read(ref _owner._lastPlayerErrorTicks);
                if (nowTicks - lastTicks < 5000)
                {
                    Log.Debug("AudioPlayerService.Android",
                        "[ExoPlayer] OnPlayerError 退避：5 秒内已发生过错误，停止自动切歌避免崩溃循环");
                    _owner._isActuallyPlaying = false;
                    _owner._mainHandler.Post(() =>
                    {
                        try
                        {
                            _owner.PlaybackStateChanged?.Invoke(_owner, false);
                        }
                        catch { }
                    });
                    // 注意：退避分支【不能】StopPositionTimer()。
                    // 若 ExoPlayer 自动从可恢复的解码错误中恢复（OnIsPlayingChanged 不会再次翻转为 true），
                    // 停掉位置定时器后无人重启，进度条会永久冻结而音频仍在播（“进度条不会跑了”）。
                    // 定时器始终按真实 CurrentPosition 轮询：播放恢复则进度继续推进，
                    // 若播放确已停止，ExoPlayer 会自行触发 OnIsPlayingChanged(false) 来正确停表。
                    return;
                }
                Interlocked.Exchange(ref _owner._lastPlayerErrorTicks, nowTicks);

                // 通知上层播放失败，让 PlayAsync 中的 catch 块回退到 FFmpeg 转码路径
                _owner._player?.Stop();
                _owner._player?.ClearMediaItems();
                _owner._isActuallyPlaying = false;
                _owner._mainHandler.Post(() =>
                {
                    try
                    {
                        _owner.NotifyPlaybackCompleted();
                        _owner.PlaybackStateChanged?.Invoke(_owner, false);
                        _owner.StopPositionTimer();
                    }
                    catch (Exception ex) { Log.Debug("AudioPlayerService.Android", $"[ExoPlayer] OnPlayerError post error: {ex.Message}"); }
                });
            }
            catch (Exception ex) { Log.Debug("AudioPlayerService.Android", $"[ExoPlayer] OnPlayerError handler error: {ex.Message}"); }
        }
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
    /// <summary>切歌时由上层传入的数据库已知时长（毫秒）。ExoPlayer 尚未 Prepare 完成时
    /// Duration 返回 0，用它兜底，避免通知栏进度条在切歌间隙显示 00:00。</summary>
    private long _knownDurationMs;

    /// <summary>设置数据库已知的歌曲时长（秒），用于 ExoPlayer 未就绪期间通知栏的时长兜底显示</summary>
    public void UpdateKnownDuration(double seconds)
    {
        _knownDurationMs = seconds > 0 ? (long)(seconds * 1000) : 0;
    }

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

        // Android 13+ 必须在运行时授予 POST_NOTIFICATIONS，否则 StartForeground 会抛 SecurityException
        _ = Microsoft.Maui.ApplicationModel.MainThread.InvokeOnMainThreadAsync(RequestNotificationPermissionAsync);

        try { ForegroundPlayerService.Start(ctx, _currentTitle, _currentArtist); }
        catch (Exception ex) { Log.Debug("AudioPlayerService.Android", $"[AudioPlayer] FG start: {ex.Message}"); }
        // 重新（刷新）前台通知与 MediaSession 状态：被其他应用抢占音频焦点 / 媒体会话后，
        // 回到本应用播放（Resume / 夺回焦点）时需立即重建通知栏与锁屏控件，
        // 而不是等到下一首才开始。UpdateForegroundNotification 会 re-assert MediaSession.Active
        // 并刷新 PlaybackState/Metadata，促使系统重新展示本应用的媒体通知。
        try { UpdateForegroundNotification(); } catch { }
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
            Log.Debug("AudioPlayerService.Android", $"[AudioPlayer] 通知权限申请异常: {ex.Message}");
        }
    }

    /// <summary>停止前台播放服务</summary>
    private void StopForegroundService()
    {
        var ctx = _androidContext ?? global::Android.App.Application.Context;
        try { ForegroundPlayerService.Stop(ctx); } catch { }
    }

    /// <summary>
    /// 在确实没有下一首可播放（队列结束）时由上层调用：
    /// 停止播放并移除前台通知 / 释放 MediaSession，避免通知栏常驻。
    /// 自然播放下一曲的正常切歌【不】应调用此方法（见 OnPlaybackStateChanged 的 STATE_ENDED 处理）。
    /// </summary>
    public void StopAndHideNotification()
    {
        try { PlatformStop(); } catch { }
        try { StopForegroundService(); } catch { }
        _mainHandler.Post(() =>
        {
            ReleaseWakeLock();
            AbandonAudioFocus();
            try { PlaybackStateChanged?.Invoke(this, false); } catch { }
        });
    }

    /// <summary>
    /// 在加载新歌、刷新通知元数据前由上层调用：重置准备/进度缓存，
    /// 使 GetPlatformCurrentPositionSeconds/Duration 立即返回 0，
    /// 避免通知栏与 MediaSession 在切歌间隙回显上一首的旧进度（锁屏"时间特别旧"）。
    /// </summary>
    public void NotifySongSwitching()
    {
        _isPrepared = false;
        _cachedPositionMs = 0;
    }

    /// <summary>更新前台通知的播放状态与歌曲信息</summary>
    private void UpdateForegroundNotification()
    {
        try
        {
            Android.Graphics.Bitmap? albumArt = null;
            // 加锁保护 _notificationBitmap：通知栏进度回调（1s/次）与切歌/状态变更可能并发调用本方法，
            // Recycle 与 ForegroundPlayerService 使用 bitmap 若竞态会触发 native SIGSEGV。
            lock (_notifBitmapLock)
            {
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
                        catch (Exception ex) { Log.Debug("AudioPlayerService.Android", $"[Notif] decode bitmap error: {ex.Message}"); }
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
            }
            long positionMs = 0;
            long durationMs = 0;
            try
            {
                positionMs = (long)(CurrentPosition * 1000);
                durationMs = (long)(Duration * 1000);
            }
            catch { }
            // ExoPlayer 未就绪时 Duration=0：用数据库已知时长兜底，
            // 避免切歌间隙 MediaSession 收到 duration=0 导致进度条显示 00:00
            if (durationMs <= 0 && _knownDurationMs > 0)
                durationMs = _knownDurationMs;
            ForegroundPlayerService.UpdatePlayState(_currentTitle, _currentArtist, IsPlaying, _currentIsFavorite, albumArt, positionMs, durationMs);
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
                ALog.Error("AudioPlayerSvc", $"OnNotifLyricsRequested failed: {ex.GetType().Name}: {ex.Message}");
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

    // ═══════════════════════════════════════
    // WebDAV HTTP 认证
    // ═══════════════════════════════════════

    /// <summary>
    /// 全局 Authenticator，向 HttpURLConnection 提供 Basic Auth 凭证。
    /// ExoPlayer 的 DefaultHttpDataSource 内部使用 HttpURLConnection，
    /// 后者默认不解析 URL 中的 userinfo，需通过 Authenticator 在 401 时提供凭证。
    /// 凭证由 PlayInternalAsync 从当前播放 URL 的 userinfo 提取并写入静态字段。
    /// 仅当静态字段非空时返回凭证，不影响其他 HTTP 请求。
    /// </summary>
    private sealed class WebDavAuthenticator : Java.Net.Authenticator
    {
        protected override Java.Net.PasswordAuthentication? PasswordAuthentication
        {
            get
            {
                var user = _currentAuthUser;
                var pass = _currentAuthPass;
                if (string.IsNullOrEmpty(user) || pass == null) return null;
                return new Java.Net.PasswordAuthentication(user, pass.ToCharArray());
            }
        }
    }
}
