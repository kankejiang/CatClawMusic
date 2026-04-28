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

    public bool IsPlaying => _mediaPlayer?.IsPlaying ?? false;
    public TimeSpan Position => TimeSpan.FromMilliseconds(_mediaPlayer?.CurrentPosition ?? 0);
    public TimeSpan Duration => TimeSpan.FromMilliseconds(_mediaPlayer?.Duration ?? 0);

    public int Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 100);
            _mediaPlayer?.SetVolume(_volume / 100f, _volume / 100f);
        }
    }

    public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;
    public event EventHandler<TimeSpan>? PositionChanged;

    public async Task PlayAsync(string filePathOrUrl)
    {
        EnsurePlayer();
        _mediaPlayer!.Reset();
        _isPrepared = false;

        await _mediaPlayer.SetDataSourceAsync(filePathOrUrl);
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
        _mediaPlayer?.Pause();
        StopPositionTimer();
        ReleaseWakeLock();
        StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs { State = PlaybackState.Paused });
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        StopPositionTimer();
        _mediaPlayer?.Stop();
        _mediaPlayer?.Reset();
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
            _wakeLock = pm.NewWakeLock(WakeLockFlags.Partial, "CatClawMusic:AudioPlayback");
            _wakeLock.Acquire();
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
            PositionChanged?.Invoke(this, Position);
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
