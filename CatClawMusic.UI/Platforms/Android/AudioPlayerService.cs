using Android.OS;
using CatClawMusic.Core.Interfaces;
using System.Timers;
using AndroidHandler = Android.OS.Handler;
using ALog = Android.Util.Log;

namespace CatClawMusic.UI.Platforms.Android;

/// <summary>
/// Android 音频播放服务（封装 SimpleExoPlayer + WakeLock 后台保活）
/// 替代了旧的 MediaPlayer，原生支持 content:// URI、内置错误恢复
/// </summary>
#pragma warning disable CS0618 // SimpleExoPlayer 虽标记 deprecated 但功能正常，后续 Media3 版本会更新
public class AudioPlayerService : IAudioPlayerService, IDisposable
{
    private AndroidX.Media3.ExoPlayer.SimpleExoPlayer? _player;
    private bool _isPrepared;
    private System.Timers.Timer? _positionTimer;
    private int _volume = 100;
    private PowerManager.WakeLock? _wakeLock;
    private string? _currentPath;
    private TaskCompletionSource<bool>? _readyTcs;
    private int _lastPlaybackState; // 上次轮询到的播放状态
    private long _cachedPositionMs; // 缓存的主线程位置值，供任意线程安全读取
    private readonly AndroidHandler _mainHandler = new(Looper.MainLooper!);

    public bool IsPlaying => _player?.IsPlaying ?? false;
    public string? CurrentSongFilePath => _currentPath;
    public int AudioSessionId => 0; // Media3 通过 AudioAttributes 管理音频会话

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
            if (_player != null)
                _player.Volume = _volume / 100f;
        }
    }

    public event EventHandler<CatClawMusic.Core.Interfaces.PlaybackStateChangedEventArgs>? StateChanged;
    public event EventHandler<TimeSpan>? PositionChanged;
#pragma warning disable CS0067
    public event Action<byte[]>? PcmDataAvailable;
#pragma warning restore CS0067

    public async Task PlayAsync(string filePathOrUrl)
    {
        _isPrepared = false;
        _lastPlaybackState = 1; // StateIdle

        ALog.Debug("CatClaw",$"[CatClaw] PlayAsync: path={filePathOrUrl?.Substring(0, Math.Min(120, filePathOrUrl?.Length ?? 0))}");

        try
        {
            // SimpleExoPlayer.Builder().Build() 必须在主线程
            await RunOnMainThreadAsync(() => EnsurePlayer());

            var uri = global::Android.Net.Uri.Parse(filePathOrUrl);
            if (uri == null)
            {
                StateChanged?.Invoke(this, new CatClawMusic.Core.Interfaces.PlaybackStateChangedEventArgs { State = PlaybackState.Error, ErrorMessage = "URI 解析失败" });
                return;
            }

            var mediaItem = AndroidX.Media3.Common.MediaItem.FromUri(uri);

            var prevTcs = _readyTcs;
            _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            prevTcs?.TrySetCanceled();

            // ExoPlayer API 必须在主线程调用
            await RunOnMainThreadAsync(() =>
            {
                _player!.Stop();
                _player.ClearMediaItems();
                _player.SetMediaItem(mediaItem);
                _player.Prepare();
            });

            // 轮询等待 STATE_READY（最多等 10 秒）
            for (int i = 0; i < 100; i++)
            {
                await Task.Delay(100);
                if (_readyTcs.Task.IsCompleted) break;
                // 必须通过主线程读取 ExoPlayer 状态
                var state = await RunOnMainThreadAsync(() => _player!.PlaybackState);
                if (state == 3)
                {
                    _readyTcs.TrySetResult(true);
                    break;
                }
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

            AcquireWakeLock();
            ALog.Debug("CatClaw","[CatClaw] PlayAsync: 即将触发 Playing 事件");
            StateChanged?.Invoke(this, new CatClawMusic.Core.Interfaces.PlaybackStateChangedEventArgs { State = PlaybackState.Playing });
            ALog.Debug("CatClaw","[CatClaw] PlayAsync: 即将启动位置定时器");
            StartPositionTimer();
            ALog.Debug("CatClaw","[CatClaw] PlayAsync: 位置定时器已启动");
        }
        catch (System.OperationCanceledException)
        {
            ALog.Debug("CatClaw","[CatClaw] PlayAsync 被取消");
        }
        catch (Exception ex)
        {
            ALog.Debug("CatClaw",$"[CatClaw] PlayAsync 失败: {ex.Message}");
            StateChanged?.Invoke(this, new CatClawMusic.Core.Interfaces.PlaybackStateChangedEventArgs
            {
                State = PlaybackState.Error,
                ErrorMessage = $"播放失败: {ex.Message}"
            });
        }
    }

    public Task PauseAsync()
    {
        if (_player != null)
            _mainHandler.Post(() => _player!.PlayWhenReady = false);
        StopPositionTimer();
        ReleaseWakeLock();
        StateChanged?.Invoke(this, new CatClawMusic.Core.Interfaces.PlaybackStateChangedEventArgs { State = PlaybackState.Paused });
        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        if (_player != null && _isPrepared)
        {
            _mainHandler.Post(() => _player!.PlayWhenReady = true);
            AcquireWakeLock();
            StartPositionTimer();
            StateChanged?.Invoke(this, new CatClawMusic.Core.Interfaces.PlaybackStateChangedEventArgs { State = PlaybackState.Playing });
        }
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        StopPositionTimer();
        if (_player != null)
            _mainHandler.Post(() => { _player!.Stop(); _player.PlayWhenReady = false; });
        ReleaseWakeLock();
        StateChanged?.Invoke(this, new CatClawMusic.Core.Interfaces.PlaybackStateChangedEventArgs { State = PlaybackState.Stopped });
        return Task.CompletedTask;
    }

    public Task SeekAsync(TimeSpan position)
    {
        if (_player != null && _isPrepared)
            _mainHandler.Post(() => _player!.SeekTo((int)position.TotalMilliseconds));
        return Task.CompletedTask;
    }

    private void AcquireWakeLock()
    {
        var ctx = global::Android.App.Application.Context;
        var pm = (PowerManager?)ctx.GetSystemService(global::Android.Content.Context.PowerService);
        if (pm != null)
        {
            try
            {
                _wakeLock = pm.NewWakeLock(WakeLockFlags.Partial, "CatClawMusic:AudioPlayback");
                _wakeLock?.Acquire();
            }
            catch (Java.Lang.SecurityException) { _wakeLock = null; }
        }
    }

    private void ReleaseWakeLock()
    {
        if (_wakeLock != null && _wakeLock.IsHeld)
        {
            _wakeLock.Release();
            _wakeLock = null;
        }
    }

    /// <summary>在主线程上执行 ExoPlayer API 调用</summary>
    private Task RunOnMainThreadAsync(Action action)
    {
        if (Looper.MyLooper() == Looper.MainLooper)
        {
            action();
            return Task.CompletedTask;
        }
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _mainHandler.Post(() =>
        {
            try { action(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    /// <summary>在主线程上执行并返回值</summary>
    private Task<T> RunOnMainThreadAsync<T>(Func<T> func)
    {
        if (Looper.MyLooper() == Looper.MainLooper)
            return Task.FromResult(func());
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _mainHandler.Post(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    private void EnsurePlayer()
    {
        if (_player == null)
        {
            var ctx = global::Android.App.Application.Context;
            _player = new AndroidX.Media3.ExoPlayer.SimpleExoPlayer.Builder(ctx).Build();
            _lastPlaybackState = _player.PlaybackState;
        }
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
        // System.Timers.Timer 在 ThreadPool 线程触发，ExoPlayer 所有属性读/写都必须在主线程
        _mainHandler.Post(() =>
        {
            try
            {
                if (_player == null || !_isPrepared) return;

                var currentPosMs = _player.CurrentPosition;
                _cachedPositionMs = currentPosMs; // 缓存供其他线程安全读取
                ALog.Debug("CatClaw",$"[CatClaw] Timer: pos={currentPosMs}ms state={_player.PlaybackState} playing={_player.IsPlaying}");
                PositionChanged?.Invoke(this, TimeSpan.FromMilliseconds(currentPosMs));

                // 轮询检测播放状态变化
                var state = _player.PlaybackState;

                // 检测播放完毕：STATE_ENDED (4)
                if (state == 4 && _lastPlaybackState != 4)
                {
                    _lastPlaybackState = 4;
                    StopPositionTimer();
                    ReleaseWakeLock();
                    StateChanged?.Invoke(this, new CatClawMusic.Core.Interfaces.PlaybackStateChangedEventArgs { State = PlaybackState.Stopped });
                    return;
                }

                // 检测错误
                var error = _player.PlayerError;
                if (error != null && _lastPlaybackState != 1)
                {
                    _lastPlaybackState = 1;
                    StopPositionTimer();
                    ReleaseWakeLock();
                    StateChanged?.Invoke(this, new CatClawMusic.Core.Interfaces.PlaybackStateChangedEventArgs
                    {
                        State = PlaybackState.Error,
                        ErrorMessage = $"ExoPlayer error: {error.ErrorCodeName}"
                    });
                    return;
                }

                _lastPlaybackState = state;
            }
            catch (Exception ex)
            {
                ALog.Debug("CatClaw",$"[CatClaw] Timer 异常: {ex.Message}");
            }
        });
    }

    public void Dispose()
    {
        StopPositionTimer();
        ReleaseWakeLock();
        if (_player != null)
        {
            _mainHandler.Post(() =>
            {
                _player.Release();
                _player.Dispose();
            });
            _player = null;
        }
    }
}
