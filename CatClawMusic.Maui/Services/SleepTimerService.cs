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
    private double _fadeBaseVolume = -1;
    private int _waitingFadeRemaining;

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
        // 注意：歌曲播放完毕的“停止”逻辑由 AppViewModels.OnPlaybackCompleted 驱动
        // （先判断本服务是否处于等待歌曲结束阶段，是则暂停且不切下一首），
        // 此处不再订阅 PlaybackCompleted，避免与 AppViewModels 的自动下一曲逻辑产生竞态/重复处理。
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
            _fadeBaseVolume = -1;
            _waitingFadeRemaining = 0;

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
        lock (_lock)
        {
            if (!_isRunning) return;

            if (_waitingForSongEnd)
            {
                // 等待歌曲结束阶段：从进入等待起做一段固定 20 秒淡出（与歌曲剩余时长无关，
                // 保证音量单调下降，不再因“歌曲剩余>20s不动、最后20s跳回满音量”而失效）。
                if (FadeOutEnabled)
                {
                    if (_fadeBaseVolume < 0)
                        _fadeBaseVolume = _player.Volume;
                    _waitingFadeRemaining = Math.Max(0, _waitingFadeRemaining - 1);
                    var factor = FadeOutSeconds > 0 ? _waitingFadeRemaining / (double)FadeOutSeconds : 0;
                    try { _player.Volume = _fadeBaseVolume * factor; } catch { }
                }
                return;
            }

            _remainingSeconds--;
            remaining = _remainingSeconds;

            // 淡出：最后 N 秒按比例降低音量（单调下降）
            if (FadeOutEnabled && remaining <= FadeOutSeconds && remaining > 0)
            {
                if (_originalVolume < 0)
                    _originalVolume = _player.Volume;   // 用户真实音量，供停止后恢复
                if (_fadeBaseVolume < 0)
                    _fadeBaseVolume = _originalVolume;  // 淡出基准（与用户音量一致）
                var factor = remaining / (double)FadeOutSeconds;
                try { _player.Volume = _fadeBaseVolume * factor; } catch { }
            }

            if (remaining <= 0)
            {
                if (StopAfterCurrentSong && _player.IsPlaying)
                {
                    // 时间到但歌曲未完：进入等待阶段，并从当前音量起重新做一段 20 秒淡出
                    _waitingForSongEnd = true;
                    _waitingFadeRemaining = FadeOutSeconds;
                    // 以当前实际音量作为后续淡出基准，避免与倒计时淡出衔接处音量跳变；
                    // 注意：_originalVolume（用户真实音量）保持不变，供停止后恢复使用。
                    _fadeBaseVolume = _player.Volume;
                }
                else
                {
                    StopTimerInternal();
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

    /// <summary>由 AppViewModels 在歌曲播放完毕且本服务处于“等待歌曲结束”阶段时调用：
    /// 暂停播放、恢复音量并结束定时（不再自动切下一首）。</summary>
    public void StopOnSongCompleted()
    {
        bool handled;
        lock (_lock)
        {
            handled = _isRunning && _waitingForSongEnd;
            if (handled) StopTimerInternal();
        }

        if (handled)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                RestoreVolume();
                try { await _player.PauseAsync(); } catch { }
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
            _fadeBaseVolume = -1;
        }
    }

    public void Dispose()
    {
        lock (_lock) { StopTimerInternal(); }
    }
}
