using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 跨平台音频播放服务，使用平台原生 MediaPlayer API。
/// Android: Android.Media.MediaPlayer
/// Windows: Windows.Media.Playback.MediaPlayer
/// </summary>
public partial class AudioPlayerService : IAudioPlayerService, IDisposable
{
    private string? _currentFilePath;
    private bool _disposed;
    private System.Threading.Timer? _positionTimer;

    public event EventHandler<bool>? PlaybackStateChanged;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler? PlaybackCompleted;

    public bool IsPlaying => GetPlatformIsPlaying();
    public double CurrentPosition => GetPlatformCurrentPositionSeconds();
    public double Duration => GetPlatformDurationSeconds();

    public double Volume
    {
        get => GetPlatformVolume();
        set => SetPlatformVolume(Math.Clamp(value, 0.0, 1.0));
    }

    public string? CurrentSongFilePath => _currentFilePath;

    public AudioPlayerService()
    {
        InitializePlatform();
    }

    public Task InitializeAsync()
    {
        System.Diagnostics.Debug.WriteLine("[AudioPlayerService] Initialized");
        return Task.CompletedTask;
    }

    public Task PlayAsync(string filePath)
    {
        try
        {
            _currentFilePath = filePath;
            PlatformPlay(BuildSourceUri(filePath));
            StartPositionTimer();
            PlaybackStateChanged?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] Play error: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    public Task PauseAsync()
    {
        PlatformPause();
        PlaybackStateChanged?.Invoke(this, false);
        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        PlatformResume();
        StartPositionTimer();
        PlaybackStateChanged?.Invoke(this, true);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        PlatformStop();
        StopPositionTimer();
        PlaybackStateChanged?.Invoke(this, false);
        return Task.CompletedTask;
    }

    public Task SeekAsync(TimeSpan position)
    {
        PlatformSeek(position);
        PositionChanged?.Invoke(this, position);
        return Task.CompletedTask;
    }

    #region 进度定时器

    private void StartPositionTimer()
    {
        StopPositionTimer();
        _positionTimer = new System.Threading.Timer(_ =>
        {
            try
            {
                // 始终拉取当前位置，确保进度条实时更新
                // （即使 IsPlaying 在 buffering/seek 期间短暂返回 false，
                //  只要 _player 存在，CurrentPosition 仍可正确反映播放进度）
                var pos = CurrentPosition;
                PositionChanged?.Invoke(this, TimeSpan.FromSeconds(pos));

                if (IsPlaying)
                {
                    // 在播放中：等待下一秒再拉
                }
                CheckPlatformCompletion();
            }
            catch { }
        }, null, 500, 500);
    }

    private void StopPositionTimer()
    {
        _positionTimer?.Dispose();
        _positionTimer = null;
    }

    #endregion

    private static Uri BuildSourceUri(string filePathOrUrl)
    {
        if (filePathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            filePathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            filePathOrUrl.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase) ||
            filePathOrUrl.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(filePathOrUrl);
        }
        var fullPath = Path.GetFullPath(filePathOrUrl);
        return new Uri($"file://{fullPath}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopPositionTimer();
        DisposePlatform();
    }

    // 平台实现由 partial class 文件提供
    partial void InitializePlatform();
    partial void PlatformPlay(Uri source);
    partial void PlatformPause();
    partial void PlatformResume();
    partial void PlatformStop();
    partial void PlatformSeek(TimeSpan position);
    partial void CheckPlatformCompletion();
    private partial bool GetPlatformIsPlaying();
    private partial double GetPlatformCurrentPositionSeconds();
    private partial double GetPlatformDurationSeconds();
    private partial double GetPlatformVolume();
    partial void SetPlatformVolume(double volume);
    partial void DisposePlatform();
}
