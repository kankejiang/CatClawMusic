using Android.OS;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.UI.Services;
using System.Timers;
using AndroidHandler = Android.OS.Handler;
using ALog = Android.Util.Log;
using Am = Android.Media.AudioManager;

namespace CatClawMusic.UI.Platforms.Android;

#pragma warning disable CS0618
public class AudioPlayerService : IAudioPlayerService, IDisposable
{
    private AndroidX.Media3.ExoPlayer.SimpleExoPlayer? _player;
    private bool _isPrepared;
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
    public bool IsPlaying => _player?.IsPlaying ?? false;
    public string? CurrentSongFilePath => _currentPath;
    public int AudioSessionId => 0;

    public TimeSpan CurrentPosition => _cachedPositionMs > 0
        ? TimeSpan.FromMilliseconds(_cachedPositionMs)
        : TimeSpan.Zero;
    public TimeSpan Duration => _player != null && _player.Duration > 0
        ? TimeSpan.FromMilliseconds(_player.Duration)
        : TimeSpan.Zero;

    public int Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 100);
            if (_player != null) _player.Volume = _volume / 100f;
        }
    }

    public event EventHandler<CatClawMusic.Core.Interfaces.PlaybackStateChangedEventArgs>? StateChanged;
    public event EventHandler<TimeSpan>? PositionChanged;
#pragma warning disable CS0067
    public event Action<byte[]>? PcmDataAvailable;
#pragma warning restore CS0067

    private readonly AudioFocusChangeListener? _focusListener;

    public AudioPlayerService()
    {
        var ctx = global::Android.App.Application.Context;
        _audioManager = (Am)ctx.GetSystemService(global::Android.Content.Context.AudioService)!;
        _focusListener = new AudioFocusChangeListener(this);
    }

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

    private void HandleFocusChange(global::Android.Media.AudioFocus focusChange)
    {
        ALog.Debug("CatClaw", $"[CatClaw] AudioFocus: {focusChange}");
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

    private void RequestAudioFocus()
    {
        try
        {
            if (_audioManager == null || _focusListener == null) return;
            var result = _audioManager.RequestAudioFocus(
                _focusListener,
                global::Android.Media.Stream.Music,
                global::Android.Media.AudioFocus.Gain);
            ALog.Debug("CatClaw", $"[CatClaw] RequestAudioFocus: {result}");
        }
        catch (Exception ex)
        {
            ALog.Warn("CatClaw", $"[CatClaw] RequestAudioFocus failed: {ex.Message}");
        }
    }

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

    public async Task PlayAsync(string filePathOrUrl)
    {
        _isPrepared = false;
        _lastPlaybackState = 1;
        _cachedPositionMs = 0;

        ALog.Debug("CatClaw", $"[CatClaw] PlayAsync: path={filePathOrUrl?.Substring(0, Math.Min(120, filePathOrUrl?.Length ?? 0))}");

        try
        {
            // 提取 Basic 认证信息并剥离 URL 中的 userinfo
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

                        // 生成 Basic Auth 请求头
                        var credentials = $"{authUser}:{authPass}";
                        var base64Credentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(credentials));
                        authHeader = $"Basic {base64Credentials}";

                        // 剥离 userinfo，构造干净的 URL
                        var cleanUri = new System.UriBuilder(parsedUri)
                        {
                            UserName = "",
                            Password = ""
                        };
                        playUrl = cleanUri.Uri.AbsoluteUri;

                        ALog.Debug("CatClaw", $"[CatClaw] PlayAsync: 提取 Basic Auth，剥离后 URL={playUrl.Substring(0, Math.Min(80, playUrl.Length))}...");
                    }
                }
                catch (Exception ex)
                {
                    ALog.Debug("CatClaw", $"[CatClaw] PlayAsync: URL 解析异常: {ex.Message}");
                }
            }

            await RunOnMainThreadAsync(() => EnsurePlayer(authHeader));

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

            await RunOnMainThreadAsync(() => _player!.PlayWhenReady = true);
            _isPrepared = true;
            _currentPath = filePathOrUrl;

            RequestAudioFocus();
            AcquireWakeLock();
            StateChanged?.Invoke(this, new CatClawMusic.Core.Interfaces.PlaybackStateChangedEventArgs { State = PlaybackState.Playing });
            StartPositionTimer();
            ForegroundPlayerService.Start(global::Android.App.Application.Context);
        }
        catch (System.OperationCanceledException) { ALog.Debug("CatClaw", "[CatClaw] PlayAsync 被取消"); }
        catch (Exception ex)
        {
            ALog.Debug("CatClaw", $"[CatClaw] PlayAsync 失败: {ex.Message}");
            StateChanged?.Invoke(this, new CatClawMusic.Core.Interfaces.PlaybackStateChangedEventArgs { State = PlaybackState.Error, ErrorMessage = $"播放失败: {ex.Message}" });
        }
    }

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

    public Task ResumeAsync()
    {
        if (_player != null)
            _mainHandler.Post(() =>
            {
                _player.PlayWhenReady = true;
                StateChanged?.Invoke(this, new CatClawMusic.Core.Interfaces.PlaybackStateChangedEventArgs { State = PlaybackState.Playing });
            });
        return Task.CompletedTask;
    }

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

    public Task SeekAsync(TimeSpan position)
    {
        if (_player != null)
            _mainHandler.Post(() => _player.SeekTo((long)position.TotalMilliseconds));
        return Task.CompletedTask;
    }

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

    private void ReleaseWakeLock()
    {
        if (_wakeLock != null && _wakeLock.IsHeld) { _wakeLock.Release(); _wakeLock = null; }
    }

    private Task RunOnMainThreadAsync(Action action)
    {
        if (Looper.MyLooper() == Looper.MainLooper) { action(); return Task.CompletedTask; }
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _mainHandler.Post(() => { try { action(); tcs.SetResult(); } catch (Exception ex) { tcs.SetException(ex); } });
        return tcs.Task;
    }

    private Task<T> RunOnMainThreadAsync<T>(Func<T> func)
    {
        if (Looper.MyLooper() == Looper.MainLooper) return Task.FromResult(func());
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _mainHandler.Post(() => { try { tcs.SetResult(func()); } catch (Exception ex) { tcs.SetException(ex); } });
        return tcs.Task;
    }

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

        if (!string.IsNullOrEmpty(authHeader))
        {
            httpFactory.SetDefaultRequestProperties(new Dictionary<string, string>
            {
                { "Authorization", authHeader }
            });
        }

        var mediaSourceFactory = new AndroidX.Media3.ExoPlayer.Source.DefaultMediaSourceFactory(ctx)
            .SetDataSourceFactory(new CatClawDataSourceFactory(httpFactory, ctx));

        _player = new AndroidX.Media3.ExoPlayer.SimpleExoPlayer.Builder(ctx)
            .SetMediaSourceFactory(mediaSourceFactory)
            .Build();

        _lastPlaybackState = _player.PlaybackState;
    }

    private void StartPositionTimer()
    {
        StopPositionTimer();
        _positionTimer = new System.Timers.Timer(500);
        _positionTimer.Elapsed += OnPositionTimerElapsed;
        _positionTimer.AutoReset = true;
        _positionTimer.Start();
    }

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
            catch (Exception ex) { ALog.Debug("CatClaw", $"[CatClaw] Timer 异常: {ex.Message}"); }
        });
    }

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

internal class CatClawDataSource : Java.Lang.Object, AndroidX.Media3.DataSource.IDataSource
{
    private readonly AndroidX.Media3.DataSource.IDataSourceFactory _httpFactory;
    private readonly global::Android.Content.Context _ctx;
    private AndroidX.Media3.DataSource.IDataSource? _current;

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
        if (scheme == "content")
        {
            _current = new AndroidX.Media3.DataSource.ContentDataSource(_ctx);
        }
        else
        {
            _current = _httpFactory.CreateDataSource();
        }
        return _current.Open(dataSpec);
    }

    public int Read(byte[]? buffer, int offset, int length)
        => _current?.Read(buffer, offset, length) ?? 0;

    public global::Android.Net.Uri? Uri => _current?.Uri;

    public IDictionary<string, IList<string>>? ResponseHeaders
        => _current?.ResponseHeaders;

    public void Close() => _current?.Close();

    public void AddTransferListener(AndroidX.Media3.DataSource.ITransferListener? transferListener)
    {
    }
}
