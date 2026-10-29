using CatClawMusic.Core.Interfaces;
using Microsoft.UI.Dispatching;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace CatClawMusic.WinUI.Services;

/// <summary>
/// WinRT <see cref="MediaPlayer"/> 播放内核（对照 MAUI 的
/// <c>Platforms/Windows/AudioPlayerService.Windows.cs</c>）。
/// P1 只做可靠的本地/网络直链播放；均衡器（AudioGraph DSP）、交叉淡变、
/// 网络音频落盘缓存属于 P2，不在此实现 —— 那时在此文件上扩展，而不是另起一套。
/// </summary>
public sealed class MediaPlayerAudioService : IAudioPlayerService, IDisposable
{
    private readonly MediaPlayer _player = new();
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _timer;
    private string? _currentPath;
    private double _volume = 1.0;
    private bool _disposed;

    public MediaPlayerAudioService()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("MediaPlayerAudioService 必须在 UI 线程上创建。");

        // 与 MAUI 版一致：断开 SMTC，避免系统媒体浮层盖住应用自己的 UI。
        try { _player.CommandManager.IsEnabled = false; } catch { }

        _player.MediaOpened += OnMediaOpened;
        _player.MediaEnded += OnMediaEnded;
        _player.MediaFailed += OnMediaFailed;
        _player.Volume = _volume;

        _timer = _dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(300);
        _timer.Tick += (_, _) => PositionChanged?.Invoke(this, TimeSpan.FromSeconds(CurrentPosition));
    }

    public event EventHandler<bool>? PlaybackStateChanged;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler<double>? DurationChanged;
    public event EventHandler? PlaybackCompleted;

    public bool IsPlaying
    {
        get
        {
            try { return _player.PlaybackSession?.PlaybackState == MediaPlaybackState.Playing; }
            catch { return false; }
        }
    }

    public double CurrentPosition
    {
        get
        {
            try { return _player.PlaybackSession?.Position.TotalSeconds ?? 0; }
            catch { return 0; }
        }
    }

    public double Duration
    {
        get
        {
            try
            {
                var seconds = _player.PlaybackSession?.NaturalDuration.TotalSeconds ?? 0;
                return double.IsNaN(seconds) ? 0 : seconds;
            }
            catch { return 0; }
        }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            try { _player.Volume = _volume; } catch { }
        }
    }

    public string? CurrentSongFilePath => _currentPath;

    public Task InitializeAsync()
    {
        try { _player.Volume = _volume; } catch { }
        return Task.CompletedTask;
    }

    public Task PlayAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Task.CompletedTask;

        _currentPath = filePath;

        try
        {
            _player.Source = MediaSource.CreateFromUri(ToSourceUri(filePath));
            _player.Play();
            _timer.Start();
        }
        catch (Exception ex)
        {
            Log.Error("player", $"播放失败：{filePath} — {ex.Message}");
            PlaybackStateChanged?.Invoke(this, false);
        }

        return Task.CompletedTask;
    }

    public Task PauseAsync()
    {
        try { _player.Pause(); } catch { }
        _timer.Stop();
        PlaybackStateChanged?.Invoke(this, false);
        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        if (_player.Source is null)
            return Task.CompletedTask;

        try { _player.Play(); _timer.Start(); } catch { }
        PlaybackStateChanged?.Invoke(this, true);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        try
        {
            _player.Pause();
            _player.Source = null;
        }
        catch { }

        _currentPath = null;
        _timer.Stop();
        PlaybackStateChanged?.Invoke(this, false);
        return Task.CompletedTask;
    }

    public Task SeekAsync(TimeSpan position)
    {
        try { _player.PlaybackSession.Position = position; } catch { }
        return Task.CompletedTask;
    }

    private void OnMediaOpened(MediaPlayer sender, object args) => _dispatcher.TryEnqueue(() =>
    {
        var duration = Duration;
        if (duration > 0)
            DurationChanged?.Invoke(this, duration);

        PositionChanged?.Invoke(this, TimeSpan.FromSeconds(CurrentPosition));
        PlaybackStateChanged?.Invoke(this, IsPlaying);
    });

    private void OnMediaEnded(MediaPlayer sender, object args) => _dispatcher.TryEnqueue(() =>
    {
        _timer.Stop();
        PlaybackCompleted?.Invoke(this, EventArgs.Empty);
        PlaybackStateChanged?.Invoke(this, false);
    });

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args) => _dispatcher.TryEnqueue(() =>
    {
        Log.Error("player", $"MediaFailed：{args.Error} — {args.ErrorMessage}（{_currentPath}）");
        _timer.Stop();
        PlaybackStateChanged?.Invoke(this, false);
    });

    /// <summary>本地路径与 http(s) 直链都要能播；相对路径按绝对路径兜底。</summary>
    private static Uri ToSourceUri(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri)
            && (uri.Scheme is "http" or "https" or "file" or "ms-appdata" or "content"))
        {
            return uri;
        }

        return new Uri(Path.GetFullPath(path));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer.Stop();

        try
        {
            _player.MediaOpened -= OnMediaOpened;
            _player.MediaEnded -= OnMediaEnded;
            _player.MediaFailed -= OnMediaFailed;
            _player.Dispose();
        }
        catch { }
    }
}
