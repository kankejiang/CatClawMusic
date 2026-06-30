using Windows.Media.Core;
using Windows.Media.Playback;

namespace CatClawMusic.Maui.Services;

public partial class AudioPlayerService
{
    private MediaPlayer? _winPlayer;
    private double _volume = 1.0;

    partial void InitializePlatform()
    {
        _winPlayer = new MediaPlayer();
        _winPlayer.MediaEnded += (_, _) =>
        {
            PlaybackCompleted?.Invoke(this, EventArgs.Empty);
            PlaybackStateChanged?.Invoke(this, false);
            StopPositionTimer();
        };
    }

    partial void PlatformPlay(Uri source)
    {
        if (_winPlayer == null) InitializePlatform();
        try
        {
            _winPlayer!.Source = MediaSource.CreateFromUri(source);
            _winPlayer.Play();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioPlayerService.Windows] Play error: {ex.Message}");
        }
    }

    partial void PlatformPause()
    {
        try { _winPlayer?.Pause(); } catch { }
    }

    partial void PlatformResume()
    {
        try { _winPlayer?.Play(); } catch { }
    }

    partial void PlatformStop()
    {
        try { _winPlayer?.Pause(); _winPlayer!.Source = null; } catch { }
    }

    partial void PlatformSeek(TimeSpan position)
    {
        try { _winPlayer!.Position = position; } catch { }
    }

    private partial bool GetPlatformIsPlaying()
    {
        try { return _winPlayer?.PlaybackSession?.PlaybackState == MediaPlaybackState.Playing; }
        catch { return false; }
    }

    private partial double GetPlatformCurrentPositionSeconds()
    {
        try { return _winPlayer?.PlaybackSession?.Position.TotalSeconds ?? 0; }
        catch { return 0; }
    }

    private partial double GetPlatformDurationSeconds()
    {
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
        try { _winPlayer!.Volume = volume; } catch { }
    }

    partial void DisposePlatform()
    {
        if (_winPlayer != null)
        {
            try { _winPlayer.Dispose(); } catch { }
            _winPlayer = null;
        }
    }

    partial void CheckPlatformCompletion()
    {
        // Windows uses MediaPlayer.MediaEnded event — handled in InitializePlatform
    }
}
