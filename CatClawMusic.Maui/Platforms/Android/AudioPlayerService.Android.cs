using Android.Content;
using Android.Media;
using Android.OS;
using AndroidX.Media3.Common;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Maui.Platforms.Android;
using SimpleExoPlayer = AndroidX.Media3.ExoPlayer.SimpleExoPlayer;
namespace CatClawMusic.Maui.Services;

/// <summary>基于 Media3 ExoPlayer + FFmpeg 的 Android 音频播放服务</summary>
public partial class AudioPlayerService
{
    private SimpleExoPlayer? _player;
    private float _volume = 1.0f;
    private volatile bool _isPrepared;
    private volatile bool _isActuallyPlaying;
    private global::Android.Content.Context? _androidContext;
    private PowerManager.WakeLock? _wakeLock;
    private AudioManager? _audioManager;
    private bool _pausedByFocusLoss;
    private long _cachedPositionMs;
    private string? _currentPath;
    private FFmpegService? _ffmpeg;
    private readonly Android.OS.Handler _mainHandler = new(Looper.MainLooper!);
    private long _lastSeekTicks;
    private const int SeekGuardMs = 800;
    private ExoPlayerListener? _playerListener;

    // Audio focus listener
    private AudioFocusListener? _focusListener;

    partial void InitializePlatform()
    {
        var ctx = global::Android.App.Application.Context;
        _audioManager = (AudioManager?)ctx.GetSystemService(Context.AudioService);
        _focusListener = new AudioFocusListener(this);
    }

    public void SetAndroidContext(global::Android.Content.Context context)
    {
        _androidContext = context;
    }

    public void SetFFmpegService(FFmpegService ffmpeg)
    {
        _ffmpeg = ffmpeg;
    }

    // ═══════════════════════════════════════
    // ExoPlayer 构建
    // ═══════════════════════════════════════

    private SimpleExoPlayer EnsurePlayer()
    {
        if (_player != null) return _player;

        var ctx = _androidContext ?? global::Android.App.Application.Context;
        _player = new SimpleExoPlayer.Builder(ctx).Build();
        _player.Volume = _volume;
        _player.RepeatMode = 0; // REPEAT_MODE_OFF
        _player.PlayWhenReady = false;

        // 注册 Listener 准确跟踪播放状态
        _playerListener = new ExoPlayerListener(this);
        _player.AddListener(_playerListener);

        return _player;
    }

    // ═══════════════════════════════════════
    // 播放核心
    // ═══════════════════════════════════════

    partial void PlatformPlay(Uri source)
    {
        _ = PlayInternalAsync(source, autoPlay: true);
    }

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

            // FFmpeg 转码回退：对 ExoPlayer 原生不支持的格式先用 FFmpeg 转 WAV
            var playUri = source;
            var localPath = source.IsFile ? source.LocalPath :
                source.Scheme == "file" ? source.AbsolutePath : null;

            if (localPath != null && _ffmpeg != null && _ffmpeg.IsAvailable && NeedsTranscoding(localPath))
            {
                System.Diagnostics.Debug.WriteLine($"[ExoPlayer] FFmpeg 转码: {Path.GetFileName(localPath)}");
                var wavPath = await _ffmpeg.TranscodeToWavAsync(localPath);
                if (wavPath != null)
                {
                    playUri = new Uri("file://" + wavPath);
                    System.Diagnostics.Debug.WriteLine("[ExoPlayer] FFmpeg 转码完成，使用 WAV 播放");
                }
            }

            var mediaItem = MediaItem.FromUri(global::Android.Net.Uri.Parse(playUri.ToString()));
            player.SetMediaItem(mediaItem);
            player.Prepare();
            player.PlayWhenReady = autoPlay;
            if (autoPlay) player.Play();

            // 不在这里设置 _isPrepared，由 ExoPlayerListener.OnPlaybackStateChanged(STATE_READY) 触发
            AcquireWakeLock();
            RequestAudioFocus();
            StartForegroundService();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExoPlayer] Play error: {ex.Message}");
            // FFmpeg 兜底：如果 ExoPlayer 直接播放失败，尝试转码
            await TryFFmpegFallbackAsync(source);
        }
    }

    /// <summary>ExoPlayer 播放失败后的 FFmpeg 兜底</summary>
    private async Task TryFFmpegFallbackAsync(Uri source)
    {
        var localPath = source.IsFile ? source.LocalPath :
            source.Scheme == "file" ? source.AbsolutePath : null;
        if (localPath == null || _ffmpeg == null || !_ffmpeg.IsAvailable) return;

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
            RequestAudioFocus();
            StartForegroundService();
            System.Diagnostics.Debug.WriteLine("[ExoPlayer] FFmpeg 兜底成功");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExoPlayer] FFmpeg 兜底失败: {ex.Message}");
        }
    }

    private static bool NeedsTranscoding(string filePath)
    {
        var ext = Path.GetExtension(filePath)?.ToLowerInvariant();
        return ext is ".m4a" or ".m4b" or ".mp4" or ".mov" or ".wma"
            or ".ogg" or ".opus" or ".ape" or ".wv" or ".aiff" or ".aif" or ".alac";
    }

    // ═══════════════════════════════════════
    // 播放控制
    // ═══════════════════════════════════════

    partial void PlatformPause()
    {
        try { _player?.Pause(); ReleaseWakeLock(); AbandonAudioFocus(); } catch { }
    }

    partial void PlatformResume()
    {
        try
        {
            if (_player != null)
            {
                _player.PlayWhenReady = true;
                _player.Play();
                AcquireWakeLock();
                RequestAudioFocus();
            }
        }
        catch { }
    }

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
            AbandonAudioFocus();
        }
        catch { }
    }

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

    private partial double GetPlatformVolume() => _volume;

    partial void SetPlatformVolume(double volume)
    {
        _volume = (float)Math.Clamp(volume, 0.0, 1.0);
        try { if (_player != null) _player.Volume = _volume; } catch { }
    }

    partial void DisposePlatform()
    {
        StopForegroundService();
        ReleaseWakeLock();
        AbandonAudioFocus();
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
        private readonly AudioPlayerService _owner;

        public ExoPlayerListener(AudioPlayerService owner) => _owner = owner;

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
                            _owner.PositionChanged?.Invoke(_owner, TimeSpan.FromSeconds(pos / 1000.0));
                    }
                    catch { }
                });
            }
            System.Diagnostics.Debug.WriteLine($"[ExoPlayer] State={playbackState} prepared={_owner._isPrepared}");
        }

        public void OnIsPlayingChanged(bool isPlaying)
        {
            _owner._isActuallyPlaying = isPlaying;
            System.Diagnostics.Debug.WriteLine($"[ExoPlayer] IsPlaying={isPlaying}");
            // 同步通知上层 PlaybackStateChanged
            try { _owner.PlaybackStateChanged?.Invoke(_owner, isPlaying); }
            catch { }
            // 播放开始时确保 timer 在运行
            if (isPlaying)
            {
                _owner._mainHandler.Post(() => _owner.StartPositionTimer());
            }
        }

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

    private void AcquireWakeLock()
    {
        if (_wakeLock == null)
        {
            var ctx = _androidContext ?? global::Android.App.Application.Context;
            var pm = (PowerManager?)ctx.GetSystemService(Context.PowerService);
            _wakeLock = pm?.NewWakeLock(WakeLockFlags.Partial, "CatClaw:Playback");
        }
        if (_wakeLock?.IsHeld == false)
        {
            try { _wakeLock.Acquire(); } catch { }
        }
    }

    private void ReleaseWakeLock()
    {
        if (_wakeLock?.IsHeld == true)
        {
            try { _wakeLock.Release(); } catch { }
        }
    }

    // ═══════════════════════════════════════
    // Audio Focus
    // ═══════════════════════════════════════

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
                break;
            case AudioFocus.Loss:
                _pausedByFocusLoss = false;
                _ = PauseAsync();
                break;
            case AudioFocus.LossTransient:
                if (IsPlaying) { _pausedByFocusLoss = true; _ = PauseAsync(); }
                break;
            case AudioFocus.LossTransientCanDuck:
                SetPlatformVolume(_volume * 0.3);
                break;
        }
    }
    // ═══════════════════════════════════════
    // Audio Focus Listener
    // ═══════════════════════════════════════

    private class AudioFocusListener : Java.Lang.Object, AudioManager.IOnAudioFocusChangeListener
    {
        private readonly AudioPlayerService _s;
        public AudioFocusListener(AudioPlayerService s) => _s = s;
        public void OnAudioFocusChange(AudioFocus focusChange)
        {
            _s._mainHandler.Post(() => _s.HandleAudioFocusChange(focusChange));
        }
    }

    // ═══════════════════════════════════════
    // 前台服务
    // ═══════════════════════════════════════

    private string _currentTitle = "";
    private string _currentArtist = "";

    public void UpdateSongInfo(string title, string artist)
    {
        _currentTitle = title;
        _currentArtist = artist;
        UpdateForegroundNotification();
    }

    private void StartForegroundService()
    {
        var ctx = _androidContext ?? global::Android.App.Application.Context;
        try { ForegroundPlayerService.Start(ctx, _currentTitle, _currentArtist); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AudioPlayer] FG start: {ex.Message}"); }
    }

    private void StopForegroundService()
    {
        var ctx = _androidContext ?? global::Android.App.Application.Context;
        try { ForegroundPlayerService.Stop(ctx); } catch { }
    }

    private void UpdateForegroundNotification()
    {
        try { ForegroundPlayerService.UpdatePlayState(_currentTitle, _currentArtist, IsPlaying); } catch { }
    }
}
