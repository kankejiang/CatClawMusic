using Android.OS;
using CatClawMusic.Core.Interfaces;
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
        _isPrepared = false;
        _lastPlaybackState = 1;
        _cachedPositionMs = 0;

        try
        {
            var playUrl = filePathOrUrl;
            string? authHeader = null;

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

            await RunOnMainThreadAsync(() => EnsurePlayer(authHeader));

            if (!playUrl.Contains("://") && !playUrl.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
                playUrl = "file://" + playUrl;

            var uri = global::Android.Net.Uri.Parse(playUrl);
            if (uri == null)
            {
                StateChanged?.Invoke(this, new CatClawMusic.Core.Interfaces.PlaybackStateChangedEventArgs { State = PlaybackState.Error, ErrorMessage = "URI 解析失败" });
                return;
            }

            var mediaItem = AndroidX.Media3.Common.MediaItem.FromUri(uri);

            var prevTcs = _readyTcs;
            _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            prevTcs?.TrySetCanceled();

            await RunOnMainThreadAsync(() =>
            {
                _player!.Stop();
                _player.ClearMediaItems();
                _player.SetMediaItem(mediaItem);
                _player.Prepare();
            });

            for (int i = 0; i < 100; i++)
            {
                await Task.Delay(100);
                if (_readyTcs.Task.IsCompleted) break;
                var state = await RunOnMainThreadAsync(() => _player!.PlaybackState);
                if (state == 3) { _readyTcs.TrySetResult(true); break; }
                if (state == 1 && i > 5)
                {
                    var ex = await RunOnMainThreadAsync(() => _player!.PlayerError);
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
                await RunOnMainThreadAsync(() => _player!.PlayWhenReady = true);
                RequestAudioFocus();
                AcquireWakeLock();
                StateChanged?.Invoke(this, new CatClawMusic.Core.Interfaces.PlaybackStateChangedEventArgs { State = PlaybackState.Playing });
                StartPositionTimer();
                ForegroundPlayerService.Start(global::Android.App.Application.Context);
            }
            else
            {
                await RunOnMainThreadAsync(() => _player!.PlayWhenReady = false);
                StateChanged?.Invoke(this, new CatClawMusic.Core.Interfaces.PlaybackStateChangedEventArgs { State = PlaybackState.Paused });
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
            _mainHandler.Post(() => _player.SeekTo((long)position.TotalMilliseconds));
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

    /// <summary>在主线程执行操作（返回 Task）</summary>
    private Task RunOnMainThreadAsync(Action action)
    {
        if (Looper.MyLooper() == Looper.MainLooper) { action(); return Task.CompletedTask; }
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _mainHandler.Post(() => { try { action(); tcs.SetResult(); } catch (Exception ex) { tcs.SetException(ex); } });
        return tcs.Task;
    }

    /// <summary>在主线程执行带返回值的操作</summary>
    private Task<T> RunOnMainThreadAsync<T>(Func<T> func)
    {
        if (Looper.MyLooper() == Looper.MainLooper) return Task.FromResult(func());
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
            .SetDataSourceFactory(new CatClawDataSourceFactory(httpFactory, ctx));

        var builder = new AndroidX.Media3.ExoPlayer.SimpleExoPlayer.Builder(ctx)
            .SetMediaSourceFactory(mediaSourceFactory);

        // 注入 TeeAudioProcessor（含 10 段软件 EQ）到 ExoPlayer 音频管道
        // 注意：Xamarin 绑定未暴露 SimpleExoPlayer.Builder.SetAudioSink()，
        // 因此通过 JNI 直接调用底层 Java ExoPlayer.Builder.setAudioSink()
        try
        {
            var teeProcessor = MainApplication.Services.GetService<TeeAudioProcessor>();
            if (teeProcessor != null)
            {
                var audioSink = new AndroidX.Media3.ExoPlayer.Audio.DefaultAudioSink.Builder(ctx)
                    .SetAudioProcessors(new AndroidX.Media3.Common.Audio.BaseAudioProcessor[] { teeProcessor })
                    .Build();

                var builderClass = Java.Lang.Class.ForName("androidx.media3.exoplayer.ExoPlayer$Builder");
                var audioSinkClass = Java.Lang.Class.ForName("androidx.media3.exoplayer.audio.AudioSink");
                var setAudioSinkMethod = builderClass.GetMethod("setAudioSink", audioSinkClass);
                setAudioSinkMethod.Invoke(builder, audioSink);

                ALog.Debug("CatClaw", "[CatClaw] TeeAudioProcessor (10-band EQ) injected via JNI");
            }
        }
        catch (Exception ex)
        {
            ALog.Warn("CatClaw", $"[CatClaw] Failed to inject TeeAudioProcessor: {ex.Message}");
        }

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

    /// <summary>启动播放位置定时器（200ms 间隔）</summary>
    private void StartPositionTimer()
    {
        StopPositionTimer();
        _positionTimer = new System.Timers.Timer(100);
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
                if (state == 4 && _lastPlaybackState != 4)
                {
                    _lastPlaybackState = 4;
                    StopPositionTimer();
                    ReleaseWakeLock();
                    StateChanged?.Invoke(this, new CatClawMusic.Core.Interfaces.PlaybackStateChangedEventArgs { State = PlaybackState.Stopped });
                    return;
                }

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

    /// <summary>释放播放器资源</summary>
    public void Dispose()
    {
        StopPositionTimer();
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

    public CatClawDataSourceFactory(
        AndroidX.Media3.DataSource.IDataSourceFactory httpFactory,
        global::Android.Content.Context ctx)
    {
        _httpFactory = httpFactory;
        _ctx = ctx;
    }

    public AndroidX.Media3.DataSource.IDataSource CreateDataSource()
    {
        return new CatClawDataSource(_httpFactory, _ctx);
    }
}

/// <summary>自定义数据源，根据 URI scheme 动态选择 HTTP 或 content 数据源</summary>
internal class CatClawDataSource : Java.Lang.Object, AndroidX.Media3.DataSource.IDataSource
{
    private readonly AndroidX.Media3.DataSource.IDataSourceFactory _httpFactory;
    private readonly global::Android.Content.Context _ctx;
    private AndroidX.Media3.DataSource.IDataSource? _current;
    private global::Android.Net.Uri? _contentUri;
    private System.IO.Stream? _contentStream;
    private long _contentLength = -1;
    private long _contentPosition;
    private static readonly IDictionary<string, IList<string>> _contentResponseHeaders =
        new Dictionary<string, IList<string>>();

    public CatClawDataSource(
        AndroidX.Media3.DataSource.IDataSourceFactory httpFactory,
        global::Android.Content.Context ctx)
    {
        _httpFactory = httpFactory;
        _ctx = ctx;
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

    public int Read(byte[]? buffer, int offset, int length)
    {
        if (_contentStream != null)
        {
            try
            {
                int read = _contentStream.Read(buffer!, offset, length);
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
