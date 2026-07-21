using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.Services;

/// <summary>睡眠定时服务：倒计时结束后暂停播放，支持"播完当前歌曲后停止"与"淡出音量"</summary>
public class SleepTimerService : IDisposable
{
    private readonly IAudioPlayerService _player;
    private System.Threading.Timer? _timer;
    private readonly object _lock = new();

    private int _remainingSeconds;
    private int _totalSeconds;
    private bool _isRunning;
    private bool _waitingForSongEnd;
    private double _originalVolume = -1;

    /// <summary>淡出持续秒数</summary>
    public const int FadeOutSeconds = 20;

    /// <summary>定时器是否正在运行（含等待歌曲结束阶段）</summary>
    public bool IsRunning { get { lock (_lock) return _isRunning; } }

    /// <summary>是否处于"等待当前歌曲播完"阶段</summary>
    public bool IsWaitingForSongEnd { get { lock (_lock) return _waitingForSongEnd; } }

    /// <summary>剩余秒数</summary>
    public int RemainingSeconds { get { lock (_lock) return _remainingSeconds; } }

    /// <summary>总定时秒数</summary>
    public int TotalSeconds { get { lock (_lock) return _totalSeconds; } }

    /// <summary>是否设置了播完当前歌曲后停止</summary>
    public bool StopAfterCurrentSong { get; private set; }

    /// <summary>是否设置了淡出</summary>
    public bool FadeOutEnabled { get; private set; }

    /// <summary>每秒 Tick 事件（参数为剩余秒数）</summary>
    public event EventHandler<int>? Tick;

    /// <summary>定时状态变化（启动/取消/完成）</summary>
    public event EventHandler? StateChanged;

    public SleepTimerService(IAudioPlayerService player)
    {
        _player = player;
        _player.PlaybackCompleted += OnPlaybackCompleted;
    }

    /// <summary>启动定时</summary>
    /// <param name="minutes">定时分钟数</param>
    /// <param name="stopAfterSong">时间到后等待当前歌曲播完再停止</param>
    /// <param name="fadeOut">最后 20 秒淡出音量</param>
    public void Start(int minutes, bool stopAfterSong, bool fadeOut)
    {
        lock (_lock)
        {
            StopTimerInternal();
            _totalSeconds = minutes * 60;
            _remainingSeconds = _totalSeconds;
            _isRunning = true;
            _waitingForSongEnd = false;
            StopAfterCurrentSong = stopAfterSong;
            FadeOutEnabled = fadeOut;
            _originalVolume = -1;

            _timer = new System.Threading.Timer(OnTimerTick, null, 1000, 1000);
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>取消定时并恢复音量</summary>
    public void Cancel()
    {
        lock (_lock)
        {
            if (!_isRunning) return;
            StopTimerInternal();
        }
        RestoreVolume();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void StopTimerInternal()
    {
        _timer?.Dispose();
        _timer = null;
        _isRunning = false;
        _waitingForSongEnd = false;
    }

    private void OnTimerTick(object? state)
    {
        int remaining;
        bool waiting;
        lock (_lock)
        {
            if (!_isRunning) return;

            if (_waitingForSongEnd)
            {
                // 等待歌曲结束阶段：检查歌曲剩余时间做淡出
                if (FadeOutEnabled)
                    ApplySongEndFade();
                return;
            }

            _remainingSeconds--;
            remaining = _remainingSeconds;

            // 淡出：最后 N 秒按比例降低音量
            if (FadeOutEnabled && remaining <= FadeOutSeconds && remaining > 0)
            {
                if (_originalVolume < 0)
                    _originalVolume = _player.Volume;
                _player.Volume = _originalVolume * (remaining / (double)FadeOutSeconds);
            }

            if (remaining <= 0)
            {
                if (StopAfterCurrentSong && _player.IsPlaying)
                {
                    // 时间到但歌曲未完：进入等待阶段
                    _waitingForSongEnd = true;
                    waiting = true;
                }
                else
                {
                    StopTimerInternal();
                    waiting = false;
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        RestoreVolume();
                        _ = _player.PauseAsync();
                        StateChanged?.Invoke(this, EventArgs.Empty);
                    });
                    return;
                }
            }
        }

        MainThread.BeginInvokeOnMainThread(() => Tick?.Invoke(this, remaining));
    }

    /// <summary>等待歌曲结束阶段：根据歌曲剩余时长淡出</summary>
    private void ApplySongEndFade()
    {
        try
        {
            var duration = _player.Duration;
            var position = _player.CurrentPosition;
            if (duration <= 0) return;
            var songRemaining = duration - position;

            if (songRemaining <= FadeOutSeconds && songRemaining > 0)
            {
                if (_originalVolume < 0)
                    _originalVolume = _player.Volume;
                _player.Volume = _originalVolume * (songRemaining / FadeOutSeconds);
            }
        }
        catch { }
    }

    /// <summary>歌曲播放完毕回调：若处于等待阶段则执行停止</summary>
    private void OnPlaybackCompleted(object? sender, EventArgs e)
    {
        bool shouldStop;
        lock (_lock)
        {
            shouldStop = _isRunning && _waitingForSongEnd;
            if (shouldStop)
                StopTimerInternal();
        }

        if (shouldStop)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                RestoreVolume();
                StateChanged?.Invoke(this, EventArgs.Empty);
            });
        }
    }

    private void RestoreVolume()
    {
        if (_originalVolume >= 0)
        {
            _player.Volume = _originalVolume;
            _originalVolume = -1;
        }
    }

    public void Dispose()
    {
        _player.PlaybackCompleted -= OnPlaybackCompleted;
        lock (_lock) { StopTimerInternal(); }
    }
}
