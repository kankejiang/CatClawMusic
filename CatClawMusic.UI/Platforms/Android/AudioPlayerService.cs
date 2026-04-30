using Android.Media;
using Android.OS;
using CatClawMusic.Core.Interfaces;
using System.Timers;

namespace CatClawMusic.UI.Platforms.Android;

/// <summary>
/// Android 音频播放服务（封装 MediaPlayer + WakeLock 后台保活）
/// </summary>
public class AudioPlayerService : IAudioPlayerService, IDisposable
{
    private MediaPlayer? _mediaPlayer;
    private bool _isPrepared;
    private System.Timers.Timer? _positionTimer;
    private int _volume = 100;
    private PowerManager.WakeLock? _wakeLock;
    private string? _currentPath;

    public bool IsPlaying => _mediaPlayer?.IsPlaying ?? false;
    public string? CurrentSongFilePath => _currentPath;
    public int AudioSessionId => _mediaPlayer?.AudioSessionId ?? 0;
    public TimeSpan CurrentPosition => TimeSpan.FromMilliseconds(_mediaPlayer?.CurrentPosition ?? 0);
    public TimeSpan Duration => TimeSpan.FromMilliseconds(_mediaPlayer?.Duration ?? 0);

    public int Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 100);
            if (_mediaPlayer != null)
                _mediaPlayer.SetVolume(_volume / 100f, _volume / 100f);
        }
    }

    public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;
    public event EventHandler<TimeSpan>? PositionChanged;
#pragma warning disable CS0067
    public event Action<byte[]>? PcmDataAvailable;
#pragma warning restore CS0067

    public async Task PlayAsync(string filePathOrUrl)
    {
        EnsurePlayer();
        _currentPath = filePathOrUrl;
        _mediaPlayer!.Reset();
        _isPrepared = false;

        // content:// URI 需要用 Context overload
        if (filePathOrUrl.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
        {
            var ctx = global::Android.App.Application.Context;
            var uri = global::Android.Net.Uri.Parse(filePathOrUrl);
            if (uri != null)
                _mediaPlayer.SetDataSource(ctx, uri);
        }
        else
        {
            await _mediaPlayer.SetDataSourceAsync(filePathOrUrl);
        }

        _mediaPlayer.SetVolume(_volume / 100f, _volume / 100f);

        var tcs = new TaskCompletionSource<bool>();
        EventHandler? preparedHandler = null;
        preparedHandler = (s, e) =>
        {
            _mediaPlayer.Prepared -= preparedHandler;
            _isPrepared = true;
            tcs.TrySetResult(true);
        };
        _mediaPlayer.Prepared += preparedHandler;
        _mediaPlayer.PrepareAsync();

        await tcs.Task;
        _mediaPlayer.Start();

        // 申请 WakeLock 防止 CPU 休眠导致播放卡顿
        AcquireWakeLock();

        StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs { State = PlaybackState.Playing });
        StartPositionTimer();
    }

    public Task PauseAsync()
    {
        if (_mediaPlayer != null) _mediaPlayer.Pause();
        StopPositionTimer();
        ReleaseWakeLock();
        StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs { State = PlaybackState.Paused });
        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        if (_mediaPlayer != null && _isPrepared)
        {
            _mediaPlayer.Start();
            AcquireWakeLock();
            StartPositionTimer();
            StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs { State = PlaybackState.Playing });
        }
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        StopPositionTimer();
        if (_mediaPlayer != null)
        {
            _mediaPlayer.Stop();
            _mediaPlayer.Reset();
        }
        ReleaseWakeLock();
        StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs { State = PlaybackState.Stopped });
        return Task.CompletedTask;
    }

    public Task SeekAsync(TimeSpan position)
    {
        if (_mediaPlayer != null && _isPrepared)
            _mediaPlayer.SeekTo((int)position.TotalMilliseconds);
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
            catch (Java.Lang.SecurityException)
            {
                // WAKE_LOCK 权限在某些 ROM (如 MIUI) 上可能被拒绝，静默忽略
                _wakeLock = null;
            }
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

    private void EnsurePlayer()
    {
        if (_mediaPlayer == null)
        {
            _mediaPlayer = new MediaPlayer();
            _mediaPlayer.Completion += OnCompletion;
            _mediaPlayer.Error += OnError;
        }
    }

    private void OnCompletion(object? sender, EventArgs e)
    {
        StopPositionTimer();
        ReleaseWakeLock();
        StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs { State = PlaybackState.Stopped });
    }

    private void OnError(object? sender, MediaPlayer.ErrorEventArgs e)
    {
        StopPositionTimer();
        ReleaseWakeLock();
        StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs
        {
            State = PlaybackState.Error,
            ErrorMessage = $"MediaPlayer error: {e.What} - {e.Extra}"
        });
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
        if (_mediaPlayer != null && _isPrepared)
            PositionChanged?.Invoke(this, CurrentPosition);
    }

    public void Dispose()
    {
        StopPositionTimer();
        ReleaseWakeLock();
        if (_mediaPlayer != null)
        {
            _mediaPlayer.Completion -= OnCompletion;
            _mediaPlayer.Error -= OnError;
            _mediaPlayer.Release();
            _mediaPlayer.Dispose();
            _mediaPlayer = null;
        }
    }
}
