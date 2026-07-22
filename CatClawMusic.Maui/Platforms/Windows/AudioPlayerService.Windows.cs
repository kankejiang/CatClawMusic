using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Maui.Services.Equalizer;

namespace CatClawMusic.Maui.Services;

public partial class AudioPlayerService
{
    private MediaPlayer? _winPlayer;
    private SystemMediaTransportControls? _smtc;
    private double _volume = 1.0;
    /// <summary>淡入淡出当前系数（0.0 ~ 1.0），与 _volume 相乘得到实际播放音量</summary>
    private float _xfadeFactor = 1.0f;

    /// <summary>均衡器开启时使用的 AudioGraph 实时 DSP 播放引擎</summary>
    private WinEqualizerEngine? _eqEngine;

    // SMTC 显示用的当前歌曲信息
    private string _currentTitle = "";
    private string _currentArtist = "";
    private string? _currentCoverPath;

    partial void InitializePlatform()
    {
        _winPlayer = new MediaPlayer();
        _winPlayer.MediaEnded += OnMediaEnded;
        _winPlayer.MediaFailed += OnMediaFailed;
        _winPlayer.MediaOpened += OnMediaOpened;

        // 配置 SystemMediaTransportControls（SMTC）：
        // 提供音量浮层、锁屏控件、硬件媒体键支持，即使应用不在前台也能响应媒体键
        try
        {
            _smtc = _winPlayer.SystemMediaTransportControls;
            if (_smtc != null)
            {
                _smtc.IsPlayEnabled = true;
                _smtc.IsPauseEnabled = true;
                _smtc.IsNextEnabled = true;
                _smtc.IsPreviousEnabled = true;
                _smtc.IsStopEnabled = false;
                _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
                _smtc.ButtonPressed += OnSmtcButtonPressed;
            }
        }
        catch (Exception ex)
        {
            Log.Debug("AudioPlayerService.Windows", $"[AudioPlayerService.Windows] SMTC init failed: {ex.Message}");
        }
    }

    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        UpdateSmtcPlaybackStatus(MediaPlaybackStatus.Stopped);
        NotifyPlaybackCompleted();
        PlaybackStateChanged?.Invoke(this, false);
        StopPositionTimer();
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        Log.Debug("AudioPlayerService.Windows", $"[AudioPlayerService.Windows] MediaFailed: {args.Error} - {args.ErrorMessage}");
        UpdateSmtcPlaybackStatus(MediaPlaybackStatus.Stopped);
        PlaybackStateChanged?.Invoke(this, false);
        StopPositionTimer();
    }

    private void OnMediaOpened(MediaPlayer sender, object args)
    {
        // 媒体打开后立即推送一次位置/时长更新，避免等待 500ms 定时器
        try
        {
            var pos = CurrentPosition;
            var dur = Duration;
            if (dur > 0)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    DurationChanged?.Invoke(this, dur);
                    PositionChanged?.Invoke(this, TimeSpan.FromSeconds(pos));
                });
            }
        }
        catch { }
    }

    partial void PlatformPlay(Uri source)
    {
        if (_winPlayer == null) InitializePlatform();

        if (EqualizerSettings.Enabled)
        {
            // EQ 开启：走 AudioGraph DSP 管线（异步加载，失败回退 MediaPlayer）
            _ = PlayWithEqEngineAsync(source);
            return;
        }

        StopEqEngine();
        PlayWithMediaPlayer(source);
    }

    private async Task PlayWithEqEngineAsync(Uri source)
    {
        try
        {
            _eqEngine ??= new WinEqualizerEngine();
            _eqEngine.MediaEnded -= OnEqEngineEnded;
            _eqEngine.MediaEnded += OnEqEngineEnded;

            if (await _eqEngine.LoadAsync(source))
            {
                try { _winPlayer?.Pause(); } catch { }
                _eqEngine.Volume = _volume;
                _eqEngine.Play();
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    StartPositionTimer();
                    UpdateSmtcPlaybackStatus(MediaPlaybackStatus.Playing);
                    PlaybackStateChanged?.Invoke(this, true);
                });
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Debug("AudioPlayerService.Windows", $"[EQ-Win] 引擎加载失败: {ex.Message}");
        }

        // 回退到 MediaPlayer
        StopEqEngine();
        MainThread.BeginInvokeOnMainThread(() => PlayWithMediaPlayer(source));
    }

    private void PlayWithMediaPlayer(Uri source)
    {
        try
        {
            _winPlayer!.Source = MediaSource.CreateFromUri(source);
            _winPlayer.Play();
            StartPositionTimer();
            UpdateSmtcPlaybackStatus(MediaPlaybackStatus.Playing);
        }
        catch (Exception ex)
        {
            Log.Debug("AudioPlayerService.Windows", $"[AudioPlayerService.Windows] Play error: {ex.Message}");
        }
    }

    private void OnEqEngineEnded(object? sender, EventArgs e)
    {
        UpdateSmtcPlaybackStatus(MediaPlaybackStatus.Stopped);
        NotifyPlaybackCompleted();
        PlaybackStateChanged?.Invoke(this, false);
        StopPositionTimer();
    }

    private void StopEqEngine()
    {
        if (_eqEngine != null)
        {
            _eqEngine.MediaEnded -= OnEqEngineEnded;
            _eqEngine.Stop();
        }
    }

    partial void PlatformPause()
    {
        if (_eqEngine?.IsActive == true)
        {
            _eqEngine.Pause();
        }
        else
        {
            try { _winPlayer?.Pause(); } catch { }
        }
        StopPositionTimer();
        UpdateSmtcPlaybackStatus(MediaPlaybackStatus.Paused);
    }

    partial void PlatformResume()
    {
        if (_eqEngine?.IsActive == true)
        {
            _eqEngine.Play();
        }
        else
        {
            try { _winPlayer?.Play(); } catch { }
        }
        StartPositionTimer();
        UpdateSmtcPlaybackStatus(MediaPlaybackStatus.Playing);
    }

    partial void PlatformStop()
    {
        StopEqEngine();
        try { _winPlayer?.Pause(); _winPlayer!.Source = null; } catch { }
        StopPositionTimer();
        UpdateSmtcPlaybackStatus(MediaPlaybackStatus.Stopped);
    }

    partial void PlatformSeek(TimeSpan position)
    {
        if (_eqEngine?.IsActive == true)
        {
            _eqEngine.Seek(position);
        }
        else
        {
            try { _winPlayer!.Position = position; } catch { }
        }
    }

    private partial bool GetPlatformIsPlaying()
    {
        if (_eqEngine?.IsActive == true)
            return _eqEngine.IsPlaying;
        try { return _winPlayer?.PlaybackSession?.PlaybackState == MediaPlaybackState.Playing; }
        catch { return false; }
    }

    private partial double GetPlatformCurrentPositionSeconds()
    {
        if (_eqEngine?.IsActive == true)
            return _eqEngine.Position.TotalSeconds;
        try { return _winPlayer?.PlaybackSession?.Position.TotalSeconds ?? 0; }
        catch { return 0; }
    }

    private partial double GetPlatformDurationSeconds()
    {
        if (_eqEngine?.IsActive == true)
            return _eqEngine.Duration.TotalSeconds;
        try
        {
            var d = _winPlayer?.PlaybackSession?.NaturalDuration.TotalSeconds ?? 0;
            return double.IsNaN(d) ? 0 : d;
        }
        catch { return 0; }
    }

    private partial double GetPlatformVolume() => _volume;

    partial void SetPlatformVolume(double volume)
    {
        _volume = volume;
        if (_eqEngine != null) _eqEngine.Volume = volume;
        try { if (_winPlayer != null) _winPlayer.Volume = _volume * _xfadeFactor; } catch { }
    }

    /// <summary>将淡入淡出系数应用到 MediaPlayer 实际音量（_volume × factor）</summary>
    /// <param name="factor">淡入淡出系数 0.0 ~ 1.0</param>
    partial void ApplyCrossfadeVolume(double factor)
    {
        _xfadeFactor = (float)factor;
        try { if (_winPlayer != null) _winPlayer.Volume = _volume * _xfadeFactor; } catch { }
    }

    /// <summary>将当前均衡器设置应用到 AudioGraph DSP 引擎（实时生效）</summary>
    partial void ApplyEqualizerPlatform()
    {
        try { _eqEngine?.RebuildFilters(); } catch { }
    }

    partial void DisposePlatform()
    {
        _eqEngine?.Dispose();
        _eqEngine = null;
        if (_winPlayer != null)
        {
            try
            {
                if (_smtc != null)
                {
                    _smtc.ButtonPressed -= OnSmtcButtonPressed;
                    _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
                }
                _winPlayer.MediaEnded -= OnMediaEnded;
                _winPlayer.MediaFailed -= OnMediaFailed;
                _winPlayer.MediaOpened -= OnMediaOpened;
                _winPlayer.Dispose();
            }
            catch { }
            _winPlayer = null;
            _smtc = null;
        }
    }

    partial void CheckPlatformCompletion()
    {
        // Windows uses MediaPlayer.MediaEnded event — handled in OnMediaEnded
    }

    // ─── SMTC (SystemMediaTransportControls) 集成 ───

    /// <summary>处理 SMTC 按钮按下事件（播放/暂停/上一首/下一首）</summary>
    private void OnSmtcButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        switch (args.Button)
        {
            case SystemMediaTransportControlsButton.Play:
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try { _winPlayer?.Play(); } catch { }
                    PlaybackStateChanged?.Invoke(this, true);
                    UpdateSmtcPlaybackStatus(MediaPlaybackStatus.Playing);
                });
                break;
            case SystemMediaTransportControlsButton.Pause:
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try { _winPlayer?.Pause(); } catch { }
                    PlaybackStateChanged?.Invoke(this, false);
                    UpdateSmtcPlaybackStatus(MediaPlaybackStatus.Paused);
                });
                break;
            case SystemMediaTransportControlsButton.Next:
                _ = PlayNextRequested?.Invoke();
                break;
            case SystemMediaTransportControlsButton.Previous:
                _ = PlayPreviousRequested?.Invoke();
                break;
        }
    }

    /// <summary>更新 SMTC 播放状态（线程安全）</summary>
    private void UpdateSmtcPlaybackStatus(MediaPlaybackStatus status)
    {
        try
        {
            if (_smtc != null)
                _smtc.PlaybackStatus = status;
        }
        catch { }
    }

    /// <summary>更新当前歌曲信息并刷新 SMTC 显示（标题/艺术家/封面）</summary>
    /// <param name="title">歌曲标题</param>
    /// <param name="artist">歌曲艺术家</param>
    public void UpdateSongInfo(string title, string artist)
    {
        _currentTitle = title;
        _currentArtist = artist;
        RefreshSmtcDisplay();
    }

    /// <summary>更新当前歌曲封面路径并刷新 SMTC 显示</summary>
    /// <param name="coverPath">封面本地文件路径，为 null 表示无封面</param>
    public void UpdateCoverPath(string? coverPath)
    {
        _currentCoverPath = coverPath;
        RefreshSmtcDisplay();
    }

    /// <summary>更新收藏状态（SMTC 无直接收藏控件，仅保存状态供未来扩展）</summary>
    /// <param name="isFavorite">是否已收藏</param>
    public void UpdateFavoriteState(bool isFavorite)
    {
        // SMTC 标准控件不包含收藏按钮，此处保留方法以与 Android 实现保持接口一致
    }

    /// <summary>刷新 SMTC 显示 updater（标题/艺术家/封面缩略图）</summary>
    private void RefreshSmtcDisplay()
    {
        if (_smtc == null) return;
        try
        {
            var updater = _smtc.DisplayUpdater;
            updater.Type = MediaPlaybackType.Music;
            updater.MusicProperties.Title = _currentTitle ?? "";
            updater.MusicProperties.Artist = _currentArtist ?? "";
            updater.MusicProperties.AlbumArtist = _currentArtist ?? "";

            // 设置封面缩略图（从本地文件路径加载）
            if (!string.IsNullOrEmpty(_currentCoverPath) && File.Exists(_currentCoverPath))
            {
                try
                {
                    var file = StorageFile.GetFileFromPathAsync(_currentCoverPath).GetAwaiter().GetResult();
                    updater.Thumbnail = RandomAccessStreamReference.CreateFromFile(file);
                }
                catch (Exception ex)
                {
                    Log.Debug("AudioPlayerService.Windows", $"[AudioPlayerService.Windows] SMTC thumbnail failed: {ex.Message}");
                    updater.Thumbnail = null;
                }
            }
            else
            {
                updater.Thumbnail = null;
            }

            updater.Update();
        }
        catch (Exception ex)
        {
            Log.Debug("AudioPlayerService.Windows", $"[AudioPlayerService.Windows] RefreshSmtcDisplay failed: {ex.Message}");
        }
    }
}
