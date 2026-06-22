using Android.OS;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Timers;
using AndroidHandler = Android.OS.Handler;
using ALog = Android.Util.Log;
using Am = Android.Media.AudioManager;

namespace CatClawMusic.UI.Platforms.Android;

/// <summary>基于 ExoPlayer 的 Android 音频播放服务，支持本地文件、网络流、音频焦点管理和唤醒锁</summary>
#pragma warning disable CS0618
public class AudioPlayerService : IAudioPlayerService, IDisposable
{
    private AndroidX.Media3.ExoPlayer.SimpleExoPlayer? _player;
    private bool _isPrepared;
    private int _lastNotifiedSessionId;
    private System.Timers.Timer? _positionTimer;
    private int _volume = 100;
    private PowerManager.WakeLock? _wakeLock;
    private string? _currentPath;
    private TaskCompletionSource<bool>? _readyTcs;
    private int _lastPlaybackState;
    private long _cachedPositionMs;
    private string? _currentAuthHeader;
    private readonly AndroidHandler _mainHandler = new(Looper.MainLooper!);
    /// <summary>最近一次 Seek 的时间戳（UTC ticks），用于在 seek 后短暂抑制 STATE_ENDED 误判</summary>
    private long _lastSeekUtcTicks;
    /// <summary>Seek 抑制窗口（毫秒），在此期间忽略 ExoPlayer 的 STATE_ENDED 以避免拖动进度条误切歌</summary>
    private const int SeekGuardMs = 800;
    /// <summary>播放后延迟缓存清理的取消令牌</summary>
    private CancellationTokenSource? _cacheEvictCts;

    private Am? _audioManager;
    private bool _pausedByFocusLoss;
    private int _preFocusVolume = 100;
    /// <summary>是否正在播放</summary>
    public bool IsPlaying => _player?.IsPlaying ?? false;
    /// <summary>当前播放歌曲的文件路径</summary>
    public string? CurrentSongFilePath => _currentPath;
    /// <summary>音频会话 ID（用于 Visualizer 绑定）</summary>
    public int AudioSessionId
    {
        get
        {
            try
            {
                if (_player == null) return 0;
                var method = _player.Class.GetMethod("getAudioSessionId");
                if (method != null)
                {
                    var result = method.Invoke(_player);
                    if (result is Java.Lang.Integer ji)
                        return ji.IntValue();
                    if (result is Java.Lang.Number jn)
                        return jn.IntValue();
                    return 0;
                }
                return 0;
            }
            catch { return 0; }
        }
    }

    /// <summary>当前播放位置</summary>
    public TimeSpan CurrentPosition => _cachedPositionMs > 0
        ? TimeSpan.FromMilliseconds(_cachedPositionMs)
        : TimeSpan.Zero;

    /// <summary>直接从 ExoPlayer 读取实时位置（毫秒），不依赖定时器缓存</summary>
    public long RealtimePositionMs
    {
        get
        {
            try
            {
                if (_player != null && _isPrepared)
                    return _player.CurrentPosition;
            }
            catch { }
            return _cachedPositionMs;
        }
    }

    /// <summary>当前歌曲总时长</summary>
    public TimeSpan Duration => _player != null && _player.Duration > 0
        ? TimeSpan.FromMilliseconds(_player.Duration)
        : TimeSpan.Zero;

    /// <summary>播放音量（0~100）</summary>
    public int Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 100);
            if (_player != null) _player.Volume = _volume / 100f;
        }
    }

    /// <summary>播放状态变化事件</summary>
    public event EventHandler<CatClawMusic.Core.Interfaces.PlaybackStateChangedEventArgs>? StateChanged;
    /// <summary>播放位置变化事件</summary>
    public event EventHandler<TimeSpan>? PositionChanged;
    /// <summary>音频会话ID变化事件（Player重建时触发，用于重新绑定Visualizer）</summary>
    public event Action<int>? AudioSessionIdChanged;
#pragma warning disable CS0067
    /// <summary>PCM 原始音频数据可用事件（当前未实现，保留接口）</summary>
    public event Action<byte[]>? PcmDataAvailable;
#pragma warning restore CS0067

    private readonly AudioFocusChangeListener? _focusListener;

    /// <summary>初始化音频播放服务，获取 AudioManager 和创建音频焦点监听器</summary>
    public AudioPlayerService()
    {
        var ctx = global::Android.App.Application.Context;
        _audioManager = (Am)ctx.GetSystemService(global::Android.Content.Context.AudioService)!;
        _focusListener = new AudioFocusChangeListener(this);

        // 音频会话变化时重新挂载 SoundEffectManager
        AudioSessionIdChanged += sid =>
        {
            if (sid > 0)
                MainApplication.Services.GetService<SoundEffectManager>()?.Attach(sid);
        };
    }

    /// <summary>音频焦点变化监听器</summary>
    private class AudioFocusChangeListener : Java.Lang.Object, Am.IOnAudioFocusChangeListener
    {
        private readonly WeakReference<AudioPlayerService> _serviceRef;
        public AudioFocusChangeListener(AudioPlayerService service) => _serviceRef = new(service);
        public void OnAudioFocusChange(global::Android.Media.AudioFocus focusChange)
        {
            if (!_serviceRef.TryGetTarget(out var self)) return;
            self._mainHandler.Post(() => self.HandleFocusChange(focusChange));
        }
    }

    /// <summary>处理音频焦点变化（丢失/临时丢失/降低音量/恢复）</summary>
    private void HandleFocusChange(global::Android.Media.AudioFocus focusChange)
    {
        switch (focusChange)
        {
            case global::Android.Media.AudioFocus.Gain:
                if (_pausedByFocusLoss)
                {
                    _pausedByFocusLoss = false;
                    _ = ResumeAsync();
                }
                else if (_volume < _preFocusVolume)
                {
                    Volume = _preFocusVolume;
                }
                break;
            case global::Android.Media.AudioFocus.Loss:
                _pausedByFocusLoss = false;
                _ = PauseAsync();
                break;
            case global::Android.Media.AudioFocus.LossTransient:
                if (IsPlaying)
                {
                    _pausedByFocusLoss = true;
                    _ = PauseAsync();
                }
                break;
            case global::Android.Media.AudioFocus.LossTransientCanDuck:
                _preFocusVolume = _volume;
                Volume = Math.Max(10, _volume / 3);
                break;
        }
    }

    /// <summary>请求音频焦点</summary>
    private void RequestAudioFocus()
    {
        try
        {
            if (_audioManager == null || _focusListener == null) return;
            var result = _audioManager.RequestAudioFocus(
                _focusListener,
                global::Android.Media.Stream.Music,
                global::Android.Media.AudioFocus.Gain);
        }
        catch (Exception ex)
        {
            ALog.Warn("CatClaw", $"[CatClaw] RequestAudioFocus failed: {ex.Message}");
        }
    }

    /// <summary>放弃音频焦点</summary>
    private void AbandonAudioFocus()
    {
        try
        {
            if (_audioManager != null && _focusListener != null)
                _audioManager.AbandonAudioFocus(_focusListener);
            _pausedByFocusLoss = false;
        }
        catch (Exception ex)
        {
            ALog.Warn("CatClaw", $"[CatClaw] AbandonAudioFocus failed: {ex.Message}");
        }
    }

    /// <summary>异步播放指定路径的音频文件或网络流</summary>
    public async Task PlayAsync(string filePathOrUrl)
    {
        await PrepareCoreAsync(filePathOrUrl, autoPlay: true);
    }

    /// <summary>仅准备播放（不自动播放），用于恢复上次播放位置时避免短暂出声</summary>
    public async Task PrepareWithoutPlayAsync(string filePathOrUrl)
    {
        await PrepareCoreAsync(filePathOrUrl, autoPlay: false);
    }

    private async Task PrepareCoreAsync(string filePathOrUrl, bool autoPlay)
    {
        // 先同步完成 PlayLater 检查和状态重置（调用方可能在主线程，直接执行避免委派延迟）
        _isPrepared = false;
        _lastPlaybackState = 1;
        _cachedPositionMs = 0;

        try
        {
            var playUrl = filePathOrUrl;
            string? authHeader = null;

            // WebDAV/OpenList: 尝试通过 GetStreamUrlAsync 获取正确的播放 URL
            // OpenList 的 /dav/ 端点 302 到 CDN，CDN 拒绝 Basic Auth → 需要 /d/ 端点
            // 此处理对正常播放（非 Restore）也必须执行
            // 使用 Task.Run 将网络调用移到后台线程，避免 continuation 阻塞主线程
            if ((filePathOrUrl.StartsWith("http://") || filePathOrUrl.StartsWith("https://"))
                && filePathOrUrl.Contains("@"))
            {
                try
                {
                    var freshUrl = await Task.Run(async () =>
                    {
                        var networkMusic = MainApplication.Services.GetService<INetworkMusicService>();
                        if (networkMusic == null) return null;

                        var profiles = await networkMusic.GetProfilesAsync();
                        var webdavProfile = profiles.FirstOrDefault(p => p.Protocol == ProtocolType.WebDAV && p.IsEnabled);
                        if (webdavProfile == null) return null;

                        var tempSong = new Song { FilePath = filePathOrUrl, RemoteId = filePathOrUrl };
                        return await networkMusic.GetStreamUrlAsync(tempSong, webdavProfile);
                    });

                    if (!string.IsNullOrEmpty(freshUrl) && !freshUrl.Contains("@"))
                    {
                        // 获取到了不含 Auth 的 URL（OpenList /d/ 端点），直接使用
                        playUrl = freshUrl;
                        System.Diagnostics.Debug.WriteLine($"[CatClaw] OpenList 播放 URL 已解析: {freshUrl[..Math.Min(80, freshUrl.Length)]}");
                        // 跳过后续的 Basic Auth 提取，直接到 ExoPlayer 创建
                        goto BuildMediaItem;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CatClaw] OpenList URL 解析失败（回退 WebDAV）: {ex.Message}");
                }
            }

            if (filePathOrUrl.StartsWith("http://") || filePathOrUrl.StartsWith("https://"))
            {
                try
                {
                    var parsedUri = new Uri(filePathOrUrl);
                    if (!string.IsNullOrEmpty(parsedUri.UserInfo))
                    {
                        var parts = parsedUri.UserInfo.Split(':', 2);
                        var authUser = parts[0];
                        var authPass = parts.Length > 1 ? parts[1] : "";

                        var credentials = $"{authUser}:{authPass}";
                        var base64Credentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(credentials));
                        authHeader = $"Basic {base64Credentials}";

                        var cleanUri = new System.UriBuilder(parsedUri)
                        {
                            UserName = "",
                            Password = ""
                        };
                        playUrl = cleanUri.Uri.AbsoluteUri;
                    }
                }
                catch { }
            }
            else if (filePathOrUrl.StartsWith("smb://", StringComparison.OrdinalIgnoreCase))
            {
                var cachedFile = await CacheSmbFileAsync(filePathOrUrl);
                if (cachedFile != null)
                    playUrl = "file://" + cachedFile;
                else
                    throw new Exception("SMB 文件缓存失败");
            }

            // OpenList /d/ URL 已解析完成，跳过 Basic Auth 提取
            BuildMediaItem:

            // FFmpeg 通用转码回退：ExoPlayer 不支持的格式转 WAV 播放
            // 先尝试原生播放，失败后再转码，避免 AAC 等已兼容格式被无谓转码
            string? TryGetLocalPath(string url)
            {
                if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                    return url.Substring(7);
                if (!url.Contains("://"))
                    return url;
                return null;
            }

            bool ShouldTryFFmpeg(string? localPath)
            {
                if (localPath == null) return false;
                var ext = Path.GetExtension(localPath)?.ToLowerInvariant();
                return (ext == ".m4a" || ext == ".m4b" || ext == ".mp4" || ext == ".mov" ||
                        ext == ".wma" || ext == ".ogg" || ext == ".opus" || ext == ".ape" ||
                        ext == ".wv" || ext == ".aiff" || ext == ".aif" || ext == ".alac") &&
                       System.IO.File.Exists(localPath);
            }

            bool IsAlacM4a(string localPath)
            {
                try
                {
                    if (!System.IO.File.Exists(localPath)) return false;

                    // 方法 1：扫描 moov/stsd 中的 alac 标记（最稳健）
                    if (M4aMetadataReader.IsAlac(localPath))
                    {
                        System.Diagnostics.Debug.WriteLine("[CatClaw] IsAlacM4a=true (atom scan)");
                        return true;
                    }

                    // 方法 2：传统音频属性解析
                    using var fs = System.IO.File.OpenRead(localPath);
                    var meta = M4aMetadataReader.ReadAudioProperties(fs);
                    if (meta?.Codec?.Equals("ALAC", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        System.Diagnostics.Debug.WriteLine("[CatClaw] IsAlacM4a=true (properties)");
                        return true;
                    }

                    // 方法 3：Android MediaExtractor MIME 探测
                    try
                    {
                        using var extractor = new global::Android.Media.MediaExtractor();
                        extractor.SetDataSource(localPath);
                        for (int i = 0; i < extractor.TrackCount; i++)
                        {
                            var format = extractor.GetTrackFormat(i);
                            var mime = format.GetString(global::Android.Media.MediaFormat.KeyMime);
                            if (mime?.Contains("alac", StringComparison.OrdinalIgnoreCase) == true)
                            {
                                System.Diagnostics.Debug.WriteLine($"[CatClaw] IsAlacM4a=true (MediaExtractor mime={mime})");
                                return true;
                            }
                        }
                    }
                    catch (Exception probeEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CatClaw] MediaExtractor ALAC 探测失败: {probeEx.Message}");
                    }

                    System.Diagnostics.Debug.WriteLine($"[CatClaw] IsAlacM4a=false (codec={meta?.Codec})");
                    return false;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CatClaw] IsAlacM4a 异常: {ex.Message}");
                    return false;
                }
            }

            bool IsM4aContainer(string? localPath)
            {
                if (localPath == null) return false;
                var ext = System.IO.Path.GetExtension(localPath).ToLowerInvariant();
                return ext == ".m4a" || ext == ".m4b" || ext == ".mp4";
            }

            async Task<string?> TryTranscodeAsync(string localPath)
            {
                try
                {
                    var ffmpeg = MainApplication.Services.GetService<FFmpegService>();
                    if (ffmpeg == null) return null;
                    var path = await ffmpeg.TranscodeToWavAsync(localPath);
                    if (path != null)
                    {
                        ALog.Debug("CatClaw", $"[CatClaw] FFmpeg 转码成功: {Path.GetFileName(path)}");
                        return path;
                    }
                    ALog.Debug("CatClaw", "[CatClaw] FFmpeg 转码失败/未就绪");
                }
                catch (Exception ex)
                {
                    ALog.Debug("CatClaw", $"[CatClaw] FFmpeg 转码异常: {ex.Message}");
                }
                return null;
            }

            async Task<bool> TryPrepareAsync(string url)
            {
                // 直接创建/复用 ExoPlayer（Xamarin Java interop 可从任意线程调用）
                EnsurePlayer(authHeader);

                if (!url.Contains("://") && !url.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
                    url = "file://" + url;

                var uri = global::Android.Net.Uri.Parse(url);
                if (uri == null) return false;

                var mediaItem = AndroidX.Media3.Common.MediaItem.FromUri(uri);

                var prevTcs = _readyTcs;
                _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                prevTcs?.TrySetCanceled();

                _player!.Stop();
                _player.ClearMediaItems();
                _player.SetMediaItem(mediaItem);
                _player.Prepare();

                for (int i = 0; i < 100; i++)
                {
                    await Task.Delay(100);
                    if (_readyTcs.Task.IsCompleted) break;
                    var state = _player!.PlaybackState;
                    if (state == 3) { _readyTcs.TrySetResult(true); break; }
                    if (state == 1 && i > 5)
                    {
                        var ex = _player!.PlayerError;
                        if (ex != null)
                        {
                            _readyTcs.TrySetException(new Exception($"播放准备失败: {ex.ErrorCodeName}"));
                            break;
                        }
                    }
                }

                await _readyTcs.Task;

                _isPrepared = true;
                _currentPath = filePathOrUrl;

                var newSessionId = AudioSessionId;
                if (newSessionId > 0)
                {
                    _lastNotifiedSessionId = newSessionId;
                    _mainHandler.Post(() => AudioSessionIdChanged?.Invoke(newSessionId));
                }

                if (autoPlay)
                {
                    _player!.PlayWhenReady = true;
                    RequestAudioFocus();
                    AcquireWakeLock();
                    StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs { State = PlaybackState.Playing });
                    StartPositionTimer();
                    ForegroundPlayerService.Start(global::Android.App.Application.Context);
                    ScheduleCacheEviction();
                }
                else
                {
                    _player!.PlayWhenReady = false;
                    StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs { State = PlaybackState.Paused });
                }
                return true;
            }

            var localPath = TryGetLocalPath(playUrl);
            var actualPlayUrl = playUrl;
            var isAlac = localPath != null && IsAlacM4a(localPath);

            // ALAC 编码的 m4a 会被 ExoPlayer 静默当作无音频输出，提前用 FFmpeg 转 WAV
            if (isAlac)
            {
                ALog.Warn("CatClaw", "[CatClaw] 检测到 ALAC 编码，强制 FFmpeg 转码");
                System.Diagnostics.Debug.WriteLine("[CatClaw] 检测到 ALAC 编码，强制 FFmpeg 转码");
                var alacTranscoded = await TryTranscodeAsync(localPath!);
                if (alacTranscoded != null)
                {
                    actualPlayUrl = alacTranscoded;
                }
                else
                {
                    throw new Exception("ALAC 文件转码失败，无法播放");
                }
            }

            var prepared = false;
            try
            {
                prepared = await TryPrepareAsync(actualPlayUrl);
            }
            catch (System.OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                ALog.Debug("CatClaw", $"[CatClaw] 原生播放失败，尝试 FFmpeg: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[CatClaw] 原生播放失败，尝试 FFmpeg: {ex.Message}");
            }

            // 兜底：未检测到 ALAC 但原生仍失败，或该格式在兼容列表中，则尝试 FFmpeg 转码
            if (!prepared && ShouldTryFFmpeg(localPath))
            {
                var transcoded = await TryTranscodeAsync(localPath!);
                if (transcoded != null)
                {
                    prepared = await TryPrepareAsync(transcoded);
                }
            }

            if (!prepared)
            {
                throw new Exception("播放准备失败");
            }
        }
        catch (System.OperationCanceledException) { ALog.Warn("CatClaw", "[CatClaw] PlayAsync 被取消"); }
        catch (Exception ex)
        {
            ALog.Warn("CatClaw", $"[CatClaw] PlayAsync 失败: {ex.Message}");
            StateChanged?.Invoke(this, new CatClawMusic.Core.Interfaces.PlaybackStateChangedEventArgs { State = PlaybackState.Error, ErrorMessage = $"播放失败: {ex.Message}" });
        }
    }

    /// <summary>暂停播放</summary>
    public Task PauseAsync()
    {
        if (_player != null)
            _mainHandler.Post(() =>
            {
                _player.PlayWhenReady = false;
                StateChanged?.Invoke(this, new CatClawMusic.Core.Interfaces.PlaybackStateChangedEventArgs { State = PlaybackState.Paused });
            });
        return Task.CompletedTask;
    }

    /// <summary>恢复播放</summary>
    public Task ResumeAsync()
    {
        if (_player != null)
            _mainHandler.Post(() =>
            {
                _player.PlayWhenReady = true;
                StartPositionTimer();
                RequestAudioFocus();
                AcquireWakeLock();
                StateChanged?.Invoke(this, new CatClawMusic.Core.Interfaces.PlaybackStateChangedEventArgs { State = PlaybackState.Playing });
                ForegroundPlayerService.Start(global::Android.App.Application.Context);

                var newSessionId = AudioSessionId;
                if (newSessionId > 0 && newSessionId != _lastNotifiedSessionId)
                {
                    _lastNotifiedSessionId = newSessionId;
                    AudioSessionIdChanged?.Invoke(newSessionId);
                }
            });
        return Task.CompletedTask;
    }

    /// <summary>停止播放并释放资源</summary>
    public Task StopAsync()
    {
        if (_player != null)
            _mainHandler.Post(() =>
            {
                _player.Stop();
                _player.ClearMediaItems();
                StopPositionTimer();
                ReleaseWakeLock();
                AbandonAudioFocus();
                StateChanged?.Invoke(this, new CatClawMusic.Core.Interfaces.PlaybackStateChangedEventArgs { State = PlaybackState.Stopped });
            });
        return Task.CompletedTask;
    }

    /// <summary>跳转到指定播放位置</summary>
    public Task SeekAsync(TimeSpan position)
    {
        if (_player != null)
        {
            _lastSeekUtcTicks = DateTime.UtcNow.Ticks;
            _mainHandler.Post(() =>
            {
                try { _player.SeekTo((long)position.TotalMilliseconds); }
                catch (Exception ex) { ALog.Warn("CatClaw", $"[CatClaw] Seek 失败: {ex.Message}"); }
            });
        }
        return Task.CompletedTask;
    }

    /// <summary>获取唤醒锁，防止 CPU 在播放时休眠</summary>
    private void AcquireWakeLock()
    {
        if (_wakeLock == null)
        {
            var pm = PowerManager.FromContext(global::Android.App.Application.Context)!;
            _wakeLock = pm.NewWakeLock(WakeLockFlags.Partial, "CatClawMusic_AudioPlayback");
            _wakeLock.SetReferenceCounted(false);
        }
        if (!_wakeLock.IsHeld) _wakeLock.Acquire();
    }

    /// <summary>释放唤醒锁</summary>
    private void ReleaseWakeLock()
    {
        if (_wakeLock != null && _wakeLock.IsHeld) { _wakeLock.Release(); _wakeLock = null; }
    }

    /// <summary>在主线程执行操作（返回 Task）。始终通过 Post 异步派发，避免点击处理函数中同步阻塞主线程导致 ANR。</summary>
    private Task RunOnMainThreadAsync(Action action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _mainHandler.Post(() => { try { action(); tcs.SetResult(); } catch (Exception ex) { tcs.SetException(ex); } });
        return tcs.Task;
    }

    /// <summary>在主线程执行带返回值的操作。始终通过 Post 异步派发。</summary>
    private Task<T> RunOnMainThreadAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _mainHandler.Post(() => { try { tcs.SetResult(func()); } catch (Exception ex) { tcs.SetException(ex); } });
        return tcs.Task;
    }

    private static bool _trustAllSslConfigured;

    /// <summary>配置全局 HTTPS 信任所有证书（用于自签名 NAS 服务器）</summary>
    private static void ConfigureTrustAllSsl()
    {
        if (_trustAllSslConfigured) return;
        _trustAllSslConfigured = true;
        try
        {
            var trustAll = new TrustAllManager();
            var sslContext = Javax.Net.Ssl.SSLContext.GetInstance("TLSv1.2");
            sslContext!.Init(null, new Javax.Net.Ssl.ITrustManager[] { trustAll }, new Java.Security.SecureRandom());
            Javax.Net.Ssl.HttpsURLConnection.DefaultSSLSocketFactory = sslContext.SocketFactory;
            // 同时信任所有主机名
            Javax.Net.Ssl.HttpsURLConnection.DefaultHostnameVerifier = new TrustAllHostnameVerifier();
        }
        catch (Exception ex)
        {
            ALog.Warn("CatClaw", $"SSL trust-all failed: {ex.Message}");
        }
    }

    /// <summary>确保 ExoPlayer 实例已创建，支持 Basic Auth 认证头，并注入软件 EQ 音频处理器</summary>
    private void EnsurePlayer(string? authHeader = null)
    {
        var ctx = global::Android.App.Application.Context;

        // 如果 authHeader 变化了，需要重建 Player 以更新 DataSource 配置
        if (_player != null && _currentAuthHeader != authHeader)
        {
            _player.Release();
            _player = null;
        }

        if (_player != null) return;

        _currentAuthHeader = authHeader;

        var httpFactory = new AndroidX.Media3.DataSource.DefaultHttpDataSource.Factory()
            .SetAllowCrossProtocolRedirects(true);

        // HTTPS 自签名证书信任 —— 设置全局 HttpsURLConnection 的 SSLSocketFactory
        ConfigureTrustAllSsl();

        if (!string.IsNullOrEmpty(authHeader))
        {
            httpFactory.SetDefaultRequestProperties(new Dictionary<string, string>
            {
                { "Authorization", authHeader }
            });
        }

        var mediaSourceFactory = new AndroidX.Media3.ExoPlayer.Source.DefaultMediaSourceFactory(ctx)
            .SetDataSourceFactory(new CatClawDataSourceFactory(httpFactory, ctx, authHeader));

        var builder = new AndroidX.Media3.ExoPlayer.SimpleExoPlayer.Builder(ctx)
            .SetMediaSourceFactory(mediaSourceFactory);

        _player = builder.Build();

        try
        {
            var logClass = Java.Lang.Class.ForName("androidx.media3.common.util.Log");
            var setLogLevel = logClass.GetMethod("setLogLevel", Java.Lang.Integer.Type);
            var logLevelError = logClass.GetField("LOG_LEVEL_ERROR");
            if (setLogLevel != null && logLevelError != null)
                setLogLevel.Invoke(null, logLevelError.Get(null));
        }
        catch { }

        _lastPlaybackState = _player.PlaybackState;
        ALog.Debug("CatClaw", $"[CatClaw] Player created, AudioSessionId={AudioSessionId}");

        // 启动时从 SharedPreferences 恢复音效设置
        RestoreAudioSettings();
    }

    /// <summary>
    /// 启动时从 SharedPreferences 恢复音效设置，将 SoundEffectManager 挂载到当前音频会话
    /// </summary>
    private void RestoreAudioSettings()
    {
        try
        {
            var sfxManager = MainApplication.Services.GetService<SoundEffectManager>();
            var sessionId = AudioSessionId;
            if (sfxManager != null && sessionId > 0)
            {
                sfxManager.Attach(sessionId);
                ALog.Debug("CatClaw", $"[CatClaw] SoundEffectManager attached to session {sessionId}");
            }
        }
        catch (Exception ex)
        {
            ALog.Warn("CatClaw", $"[CatClaw] Failed to restore audio settings: {ex.Message}");
        }
    }

    private static string? _smbCacheDir;

    private async Task<string?> CacheSmbFileAsync(string smbUrl)
    {
        try
        {
            var cacheDir = _smbCacheDir ??= Path.Combine(
                global::Android.App.Application.Context.CacheDir!.AbsolutePath, "smb_cache");
            if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

            var hash = Convert.ToBase64String(
                System.Security.Cryptography.MD5.HashData(
                    System.Text.Encoding.UTF8.GetBytes(smbUrl)))
                .Replace('/', '_').Replace('+', '-').TrimEnd('=');
            var ext = Path.GetExtension(smbUrl.Split('?')[0]) ?? ".mp3";
            var cacheFile = Path.Combine(cacheDir, hash + ext);

            if (File.Exists(cacheFile)) return cacheFile;

            foreach (var f in Directory.GetFiles(cacheDir))
            {
                try { if (new FileInfo(f).LastAccessTimeUtc < DateTime.UtcNow.AddDays(3)) File.Delete(f); } catch { }
            }

            var smbService = MainApplication.Services.GetService(typeof(CatClawMusic.Data.SmbService)) as CatClawMusic.Data.SmbService
                ?? MainApplication.Services.GetServices<CatClawMusic.Core.Interfaces.INetworkFileService>()
                    .FirstOrDefault(s => s is CatClawMusic.Data.SmbService) as CatClawMusic.Data.SmbService;
            if (smbService == null) return null;

            var uri = global::Android.Net.Uri.Parse(smbUrl);
            if (uri == null) return null;

            var host = uri.Host ?? "";
            var userInfo = uri.UserInfo ?? "";
            var userName = "";
            var password = "";
            if (!string.IsNullOrEmpty(userInfo))
            {
                var parts = userInfo.Split(':', 2);
                userName = System.Uri.UnescapeDataString(parts[0]);
                if (parts.Length > 1) password = System.Uri.UnescapeDataString(parts[1]);
            }

            var pathSegments = uri.PathSegments;
            var shareName = pathSegments.Count > 0 ? pathSegments[0] : "share";
            var filePath = pathSegments.Count > 1
                ? "\\" + string.Join("\\", pathSegments.Skip(1))
                : "\\";

            var profile = new CatClawMusic.Core.Models.ConnectionProfile
            {
                Host = host, Port = 445,
                UserName = userName, Password = password,
                ShareName = shareName, IsEnabled = true
            };
            smbService.Configure(profile);

            using var srcStream = await smbService.OpenReadAsync(filePath);
            using var dstStream = new FileStream(cacheFile, FileMode.Create, FileAccess.Write, FileShare.None, 65536);
            await srcStream.CopyToAsync(dstStream);
            return cacheFile;
        }
        catch (Exception ex)
        {
            ALog.Warn("CatClaw", $"[CatClaw] SMB 缓存失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>启动播放位置定时器（约 30fps，用于逐字歌词平滑着色）</summary>
    private void StartPositionTimer()
    {
        StopPositionTimer();
        _positionTimer = new System.Timers.Timer(33);
        _positionTimer.Elapsed += OnPositionTimerElapsed;
        _positionTimer.AutoReset = true;
        _positionTimer.Start();
    }

    /// <summary>停止播放位置定时器</summary>
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

    /// <summary>播放位置定时器回调，更新缓存位置并检测播放结束/错误</summary>
    private void OnPositionTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        _mainHandler.Post(() =>
        {
            try
            {
                if (_player == null || !_isPrepared) return;
                var currentPosMs = _player.CurrentPosition;
                _cachedPositionMs = currentPosMs;
                PositionChanged?.Invoke(this, TimeSpan.FromMilliseconds(currentPosMs));

                var state = _player.PlaybackState;
                // Seek 抑制窗口：拖动进度条后 ExoPlayer 可能短暂处于 STATE_ENDED，
                // 此时不应触发自动切歌。只有当位置接近末尾且不在 seek 窗口内才视为真正播放结束。
                var sinceSeekMs = _lastSeekUtcTicks == 0
                    ? long.MaxValue
                    : (long)(DateTime.UtcNow - new DateTime(_lastSeekUtcTicks, DateTimeKind.Utc)).TotalMilliseconds;
                var durationMs = _player.Duration > 0 ? _player.Duration : 0;
                var nearEnd = durationMs > 0 && currentPosMs >= durationMs - 200;
                if (state == 4 && _lastPlaybackState != 4 && sinceSeekMs > SeekGuardMs && nearEnd)
                {
                    _lastPlaybackState = 4;
                    _lastSeekUtcTicks = 0;
                    StopPositionTimer();
                    ReleaseWakeLock();
                    StateChanged?.Invoke(this, new CatClawMusic.Core.Interfaces.PlaybackStateChangedEventArgs { State = PlaybackState.Stopped });
                    return;
                }
                // seek 后如果状态已恢复（非 ENDED），清除 seek 标记
                if (state != 4 && sinceSeekMs > SeekGuardMs)
                    _lastSeekUtcTicks = 0;

                var error = _player.PlayerError;
                if (error != null && _lastPlaybackState != 1)
                {
                    _lastPlaybackState = 1;
                    StopPositionTimer();
                    ReleaseWakeLock();
                    StateChanged?.Invoke(this, new CatClawMusic.Core.Interfaces.PlaybackStateChangedEventArgs { State = PlaybackState.Error, ErrorMessage = $"ExoPlayer error: {error.ErrorCodeName}" });
                    return;
                }

                _lastPlaybackState = state;
            }
            catch (Exception ex) { ALog.Warn("CatClaw", $"[CatClaw] Timer 异常: {ex.Message}"); }
        });
    }

    /// <summary>
    /// 播放开始后 1 分钟延迟执行缓存清理。如果歌曲时长不足 60 秒则跳过。
    /// 每次播放新歌时会取消上一次的定时器。
    /// </summary>
    private void ScheduleCacheEviction()
    {
        _cacheEvictCts?.Cancel();
        _cacheEvictCts?.Dispose();

        var durationMs = _player?.Duration ?? 0;
        if (durationMs > 0 && durationMs < 60_000) return; // 歌曲太短，跳过

        var cts = new CancellationTokenSource();
        _cacheEvictCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), cts.Token);
                var prefs = global::Android.App.Application.Context.GetSharedPreferences(
                    Fragments.GeneralSettingsFragment.PrefsName,
                    global::Android.Content.FileCreationMode.Private);
                int maxGb = prefs!.GetInt(Fragments.GeneralSettingsFragment.KeyCacheSizeGB, 1);
                await Fragments.GeneralSettingsFragment.EvictCacheAsync(maxGb);
            }
            catch (System.OperationCanceledException) { }
            catch (Exception ex) { ALog.Warn("CatClaw", $"[CatClaw] 缓存清理失败: {ex.Message}"); }
        });
    }

    /// <summary>释放播放器资源</summary>
    public void Dispose()
    {
        StopPositionTimer();
        _cacheEvictCts?.Cancel();
        _cacheEvictCts?.Dispose();
        _cacheEvictCts = null;
        ReleaseWakeLock();
        AbandonAudioFocus();
        if (_player != null)
        {
            _mainHandler.Post(() => { _player.Release(); _player.Dispose(); });
            _player = null;
        }
    }
}

/// <summary>自定义数据源工厂，支持 HTTP 和 content:// 协议切换</summary>
internal class CatClawDataSourceFactory : Java.Lang.Object, AndroidX.Media3.DataSource.IDataSourceFactory
{
    private readonly AndroidX.Media3.DataSource.IDataSourceFactory _httpFactory;
    private readonly global::Android.Content.Context _ctx;
    private readonly string? _authHeader;

    public CatClawDataSourceFactory(
        AndroidX.Media3.DataSource.IDataSourceFactory httpFactory,
        global::Android.Content.Context ctx,
        string? authHeader = null)
    {
        _httpFactory = httpFactory;
        _ctx = ctx;
        _authHeader = authHeader;
    }

    public AndroidX.Media3.DataSource.IDataSource CreateDataSource()
    {
        return new CatClawDataSource(_httpFactory, _ctx, _authHeader);
    }
}

/// <summary>自定义数据源，根据 URI scheme 动态选择 HTTP 或 content 数据源。
/// 对于带 Auth 的 HTTP 请求，手动处理 302 重定向（重定向后去掉 Auth 头，CDN 拒带 Auth）。</summary>
internal class CatClawDataSource : Java.Lang.Object, AndroidX.Media3.DataSource.IDataSource
{
    private readonly AndroidX.Media3.DataSource.IDataSourceFactory _httpFactory;
    private readonly global::Android.Content.Context _ctx;
    private readonly string? _authHeader;
    private AndroidX.Media3.DataSource.IDataSource? _current;
    private global::Android.Net.Uri? _contentUri;
    private System.IO.Stream? _contentStream;
    private long _contentLength = -1;
    private long _contentPosition;
    private static readonly IDictionary<string, IList<string>> _contentResponseHeaders =
        new Dictionary<string, IList<string>>();

    // HTTP 直连模式字段
    private System.Net.Http.HttpResponseMessage? _httpResponse;
    private bool _isHttpDirectMode;

    public CatClawDataSource(
        AndroidX.Media3.DataSource.IDataSourceFactory httpFactory,
        global::Android.Content.Context ctx,
        string? authHeader = null)
    {
        _httpFactory = httpFactory;
        _ctx = ctx;
        _authHeader = authHeader;
    }

    public long Open(AndroidX.Media3.DataSource.DataSpec? dataSpec)
    {
        var scheme = dataSpec?.Uri?.Scheme ?? "";
        var path = dataSpec?.Uri?.Path ?? "";

        if (scheme == "content")
        {
            _contentUri = dataSpec!.Uri;
            try
            {
                _contentStream = _ctx.ContentResolver!.OpenInputStream(_contentUri);
                if (_contentStream == null)
                    throw new Java.IO.IOException($"Cannot open content URI: {_contentUri}");

                _contentLength = _contentStream.Length > 0 ? _contentStream.Length : -1;
                _contentPosition = 0;

                if (dataSpec.Position > 0 && _contentStream.CanSeek)
                {
                    _contentStream.Seek((long)dataSpec.Position, System.IO.SeekOrigin.Begin);
                    _contentPosition = (long)dataSpec.Position;
                }

                return _contentLength >= 0 ? _contentLength : -1;
            }
            catch (Java.IO.InterruptedIOException) { return -1; }
            catch (Java.Nio.Channels.ClosedByInterruptException) { return -1; }
            catch (Java.Lang.Exception ex) when (ex.Cause is Java.IO.InterruptedIOException or Java.Nio.Channels.ClosedByInterruptException) { return -1; }
            catch (System.IO.IOException ex) { throw new Java.IO.IOException(ex.Message); }
        }
        else if (scheme == "smb")
        {
            _contentUri = dataSpec!.Uri;
            try
            {
                _contentStream = OpenSmbStream(dataSpec.Uri!);
                _contentLength = _contentStream.Length > 0 ? _contentStream.Length : -1;
                _contentPosition = 0;

                if (dataSpec.Position > 0 && _contentStream.CanSeek)
                {
                    _contentStream.Seek((long)dataSpec.Position, System.IO.SeekOrigin.Begin);
                    _contentPosition = (long)dataSpec.Position;
                }

                return _contentLength >= 0 ? _contentLength : -1;
            }
            catch (Java.IO.InterruptedIOException) { return -1; }
            catch (Java.Nio.Channels.ClosedByInterruptException) { return -1; }
            catch (Java.Lang.Exception ex) when (ex.Cause is Java.IO.InterruptedIOException or Java.Nio.Channels.ClosedByInterruptException) { return -1; }
            catch (System.IO.IOException ex) { throw new Java.IO.IOException(ex.Message); }
            catch (Exception ex) { throw new Java.IO.IOException(ex.Message); }
        }
        else if (string.IsNullOrEmpty(scheme) || scheme == "file")
        {
            _contentUri = dataSpec!.Uri;
            var filePath = dataSpec.Uri?.ToString() ?? "";
            if (filePath.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                filePath = filePath.Substring(7);
            if (string.IsNullOrEmpty(filePath) && !string.IsNullOrEmpty(path))
                filePath = path;

            try
            {
                _contentStream = new System.IO.FileStream(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read);
                _contentLength = _contentStream.Length;
                _contentPosition = 0;

                if (dataSpec.Position > 0 && _contentStream.CanSeek)
                {
                    _contentStream.Seek((long)dataSpec.Position, System.IO.SeekOrigin.Begin);
                    _contentPosition = (long)dataSpec.Position;
                }

                return _contentLength;
            }
            catch (Java.IO.InterruptedIOException) { return -1; }
            catch (Java.Nio.Channels.ClosedByInterruptException) { return -1; }
            catch (Java.Lang.Exception ex) when (ex.Cause is Java.IO.InterruptedIOException or Java.Nio.Channels.ClosedByInterruptException) { return -1; }
            catch (System.IO.IOException ex) { throw new Java.IO.IOException(ex.Message); }
            catch (Exception ex) { throw new Java.IO.IOException(ex.Message); }
        }
        else
        {
            // HTTP/HTTPS：如果有 Auth 头，用 .NET HttpClient 手动处理重定向
            // （DefaultHttpDataSource 302 后仍带 Auth 头，OpenList CDN 返回 400）
            // 注意：此方法运行在 ExoPlayer 的加载线程（后台线程），不会有 NetworkOnMainThreadException
            if (!string.IsNullOrEmpty(_authHeader))
            {
                return OpenHttpWithManualRedirect(dataSpec!);
            }
            _current = _httpFactory.CreateDataSource();
        }
        try
        {
            return _current.Open(dataSpec);
        }
        catch (Java.IO.InterruptedIOException)
        {
            return -1;
        }
        catch (Java.Nio.Channels.ClosedByInterruptException)
        {
            return -1;
        }
        catch (Java.Lang.Exception ex) when (ex.Cause is Java.IO.InterruptedIOException or Java.Nio.Channels.ClosedByInterruptException)
        {
            return -1;
        }
    }

    private System.IO.Stream OpenSmbStream(global::Android.Net.Uri uri)
    {
        var smbService = MainApplication.Services.GetService(typeof(CatClawMusic.Data.SmbService)) as CatClawMusic.Data.SmbService
            ?? MainApplication.Services.GetServices<CatClawMusic.Core.Interfaces.INetworkFileService>()
                .FirstOrDefault(s => s is CatClawMusic.Data.SmbService) as CatClawMusic.Data.SmbService;
        if (smbService == null)
            throw new System.IO.IOException("SMB 服务不可用");

        var host = uri.Host ?? "";
        var userInfo = uri.UserInfo ?? "";
        var userName = "";
        var password = "";
        if (!string.IsNullOrEmpty(userInfo))
        {
            var parts = userInfo.Split(':', 2);
            userName = System.Uri.UnescapeDataString(parts[0]);
            if (parts.Length > 1) password = System.Uri.UnescapeDataString(parts[1]);
        }

        var pathSegments = uri.PathSegments;
        var shareName = pathSegments.Count > 0 ? pathSegments[0] : "share";
        var filePath = pathSegments.Count > 1
            ? "\\" + string.Join("\\", pathSegments.Skip(1))
            : "\\";

        var profile = new CatClawMusic.Core.Models.ConnectionProfile
        {
            Host = host,
            Port = 445,
            UserName = userName,
            Password = password,
            ShareName = shareName,
            IsEnabled = true
        };

        smbService.Configure(profile);
        var streamTask = smbService.OpenReadAsync(filePath);
        streamTask.Wait();
        return streamTask.Result;
    }

    /// <summary>
    /// 用 .NET HttpClient 手动处理 HTTP 请求和 302 重定向。
    /// 首次请求带 Basic Auth，重定向后不带 Auth（CDN 拒带 Auth 头）。
    /// 支持 Range 请求用于 seek。
    /// 此方法运行在 ExoPlayer 加载线程（后台线程）。
    /// </summary>
    private long OpenHttpWithManualRedirect(AndroidX.Media3.DataSource.DataSpec dataSpec)
    {
        try
        {
            var url = dataSpec.Uri?.ToString() ?? "";
            _contentUri = dataSpec.Uri;

            ALog.Debug("CatClaw", $"[CatClawDataSource] HTTP 直连模式: {url[..Math.Min(80, url.Length)]}, position={dataSpec.Position}");

            var currentUrl = url;
            for (int i = 0; i < 5; i++)
            {
                HttpResponseMessage response;

                if (i == 0)
                {
                    // 首次请求：带 Basic Auth
                    var handler = new HttpClientHandler
                    {
                        AllowAutoRedirect = false,
                        ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                    };
                    var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
                    client.DefaultRequestHeaders.Add("Authorization", _authHeader!);

                    var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);
                    if (dataSpec.Position > 0)
                        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue((long)dataSpec.Position, null);

                    // 注意：Android 上 HttpClient.Send() 同步方法不被支持，必须用 SendAsync
                    response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                }
                else
                {
                    // 重定向后：不带 Auth 的 SocketsHttpHandler（纯 .NET 实现，避免 Java HttpURLConnection 重定向循环问题）
                    var noAuthHandler = new System.Net.Http.SocketsHttpHandler
                    {
                        AllowAutoRedirect = false,
                        SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                        {
                            RemoteCertificateValidationCallback = (_, _, _, _) => true
                        },
                        ConnectTimeout = TimeSpan.FromSeconds(15)
                    };
                    var noAuthClient = new HttpClient(noAuthHandler) { Timeout = TimeSpan.FromSeconds(60) };

                    var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);
                    // 仅首次重定向保留 Range 头
                    if (i == 1 && dataSpec.Position > 0)
                        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue((long)dataSpec.Position, null);

                    response = noAuthClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                }

                var statusCode = (int)response.StatusCode;

                if (statusCode == 301 || statusCode == 302 || statusCode == 307 || statusCode == 308)
                {
                    var location = response.Headers.Location;
                    response.Dispose();
                    if (location == null)
                        throw new Java.IO.IOException($"HTTP {statusCode} 重定向但缺少 Location 头");

                    var nextUrl = location.IsAbsoluteUri
                        ? location.ToString()
                        : new Uri(new Uri(currentUrl), location).ToString();

                    // 检测重定向循环（CDN 返回 302 指向自身）—— 比较更新前的 URL
                    if (nextUrl == currentUrl)
                    {
                        ALog.Warn("CatClaw", $"[CatClawDataSource] 检测到重定向循环，URL: {currentUrl[..Math.Min(80, currentUrl.Length)]}");
                        throw new Java.IO.IOException($"CDN 重定向循环: {statusCode}");
                    }

                    var previousUrl = currentUrl;
                    currentUrl = nextUrl;
                    ALog.Debug("CatClaw", $"[CatClawDataSource] 重定向: {statusCode} -> {currentUrl[..Math.Min(80, currentUrl.Length)]}...");

                    // 额外检测：如果两次重定向到同一个 CDN URL（不同签名的循环）
                    if (i >= 2 && nextUrl.Contains("cmecloud.cn") && previousUrl.Contains("cmecloud.cn"))
                    {
                        ALog.Warn("CatClaw", $"[CatClawDataSource] 检测到 CDN 多次重定向，可能循环");
                    }
                    continue;
                }

                response.EnsureSuccessStatusCode();

                // 成功获取响应流（保持 response 存活，读取流时需要它）
                _contentStream = response.Content.ReadAsStream();
                _httpResponse = response;
                _isHttpDirectMode = true;
                _contentPosition = dataSpec.Position;

                // 获取内容长度
                var contentLength = response.Content.Headers.ContentLength;
                if (contentLength.HasValue && contentLength.Value >= 0)
                    _contentLength = contentLength.Value;
                else if (response.Content.Headers.ContentRange?.Length != null)
                    _contentLength = response.Content.Headers.ContentRange.Length.Value;
                else
                    _contentLength = -1;

                ALog.Debug("CatClaw", $"[CatClawDataSource] HTTP 流打开成功, length={_contentLength}, range={dataSpec.Position}");
                return _contentLength >= 0 ? _contentLength : -1;
            }

            throw new Java.IO.IOException("HTTP 重定向次数过多");
        }
        catch (Java.IO.IOException) { throw; }
        catch (Exception ex)
        {
            ALog.Warn("CatClaw", $"[CatClawDataSource] HTTP 直连失败: {ex.Message}");
            // 回退到不带 Auth 的 DefaultHttpDataSource（适用于无需认证的直接 URL）
            _isHttpDirectMode = false;
            var noAuthFactory = new AndroidX.Media3.DataSource.DefaultHttpDataSource.Factory()
                .SetAllowCrossProtocolRedirects(true)
                .SetConnectTimeoutMs(30_000)
                .SetReadTimeoutMs(60_000);
            _current = noAuthFactory.CreateDataSource();
            return _current.Open(dataSpec);
        }
    }

    public int Read(byte[]? buffer, int offset, int length)
    {
        if (_contentStream != null || _isHttpDirectMode)
        {
            try
            {
                int read = _contentStream!.Read(buffer!, offset, length);
                if (read <= 0) return -1;
                _contentPosition += read;
                return read;
            }
            catch (System.IO.IOException) { return -1; }
        }
        try
        {
            return _current?.Read(buffer, offset, length) ?? 0;
        }
        catch (Java.IO.InterruptedIOException)
        {
            return -1;
        }
        catch (Java.Lang.Exception ex) when (ex.Cause is Java.IO.InterruptedIOException)
        {
            return -1;
        }
    }

    public global::Android.Net.Uri? Uri => _contentUri ?? _current?.Uri;

    public IDictionary<string, IList<string>>? ResponseHeaders
        => _contentUri != null ? _contentResponseHeaders : _current?.ResponseHeaders;

    public void Close()
    {
        try { _contentStream?.Close(); } catch { }
        _contentStream = null;
        _contentUri = null;
        _contentLength = -1;
        _contentPosition = 0;
        _isHttpDirectMode = false;
        try { _httpResponse?.Dispose(); } catch { }
        _httpResponse = null;
        _current?.Close();
    }

    public void AddTransferListener(AndroidX.Media3.DataSource.ITransferListener? transferListener)
    {
    }
}

/// <summary>信任所有服务器证书的 TrustManager，用于自签名 HTTPS 连接</summary>
internal class TrustAllManager : Java.Lang.Object, Javax.Net.Ssl.IX509TrustManager
{
    public void CheckClientTrusted(Java.Security.Cert.X509Certificate[]? chain, string? authType) { }
    public void CheckServerTrusted(Java.Security.Cert.X509Certificate[]? chain, string? authType) { }
    public Java.Security.Cert.X509Certificate[] GetAcceptedIssuers() => Array.Empty<Java.Security.Cert.X509Certificate>();
}

/// <summary>信任所有主机名的 HostnameVerifier</summary>
internal class TrustAllHostnameVerifier : Java.Lang.Object, Javax.Net.Ssl.IHostnameVerifier
{
    public bool Verify(string? hostname, Javax.Net.Ssl.ISSLSession? session) => true;
}
