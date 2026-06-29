using Android.Media;

namespace CatClawMusic.Maui.Services;

public partial class AudioPlayerService
{
    private MediaPlayer? _mediaPlayer;
    private float _volume = 1.0f;
    private bool _isPrepared;

    partial void InitializePlatform()
    {
        _mediaPlayer = new MediaPlayer();
        _mediaPlayer.Completion += (_, _) =>
        {
            PlaybackCompleted?.Invoke(this, EventArgs.Empty);
            PlaybackStateChanged?.Invoke(this, false);
            StopPositionTimer();
        };
    }

    partial void PlatformPlay(Uri source)
    {
        if (_mediaPlayer == null) InitializePlatform();
        try
        {
            _mediaPlayer!.Reset();
            if (source.Scheme == "file")
                _mediaPlayer.SetDataSource(source.LocalPath);
            else
                _mediaPlayer.SetDataSource(source.ToString());
            _mediaPlayer.Prepare();
            _mediaPlayer.Start();
            _isPrepared = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioPlayerService.Android] Play error: {ex.Message}");
        }
    }

    partial void PlatformPause()
    {
        try { _mediaPlayer?.Pause(); } catch { }
    }

    partial void PlatformResume()
    {
        try { _mediaPlayer?.Start(); } catch { }
    }

    partial void PlatformStop()
    {
        try { _mediaPlayer?.Stop(); _isPrepared = false; } catch { }
    }

    partial void PlatformSeek(TimeSpan position)
    {
        try { _mediaPlayer?.SeekTo((int)position.TotalMilliseconds); } catch { }
    }

    private partial bool GetPlatformIsPlaying() => _mediaPlayer?.IsPlaying ?? false;

    private partial double GetPlatformCurrentPositionSeconds()
    {
        try { return _mediaPlayer != null && _isPrepared ? _mediaPlayer.CurrentPosition / 1000.0 : 0; }
        catch { return 0; }
    }

    private partial double GetPlatformDurationSeconds()
    {
        try { return _mediaPlayer != null && _isPrepared ? _mediaPlayer.Duration / 1000.0 : 0; }
        catch { return 0; }
    }

    private partial double GetPlatformVolume() => _volume;

    partial void SetPlatformVolume(double volume)
    {
        _volume = (float)volume;
        try { _mediaPlayer?.SetVolume(_volume, _volume); } catch { }
    }

    partial void DisposePlatform()
    {
        if (_mediaPlayer != null)
        {
            try
            {
                _mediaPlayer.Release();
                _mediaPlayer.Dispose();
            }
            catch { }
            _mediaPlayer = null;
        }
    }
}
