using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 桌面歌词协调器：连接音频播放服务、歌词服务和桌面歌词悬浮窗服务。
/// 订阅播放进度变化，同步当前歌词行到悬浮窗。
/// </summary>
public class DesktopLyricManager
{
    private const string Tag = "DesktopLyricMgr";
    private readonly IAudioPlayerService _audioService;
    private readonly ILyricsService _lyricsService;
    private readonly IDesktopLyricService _desktopLyricService;
    private LrcLyrics? _currentLyrics;
    private int _currentLineIndex = -1;

    /// <summary>桌面歌词开关状态变化事件</summary>
    public event Action<bool>? StateChanged;

    /// <summary>桌面歌词开启失败事件（权限不足等）</summary>
    public event Action? EnableFailed;

    public DesktopLyricManager(
        IAudioPlayerService audioService,
        ILyricsService lyricsService,
        IDesktopLyricService desktopLyricService)
    {
        _audioService = audioService;
        _lyricsService = lyricsService;
        _desktopLyricService = desktopLyricService;

        _audioService.PositionChanged += OnPositionChanged;
    }

    /// <summary>当前桌面歌词是否正在显示</summary>
    public bool IsShowing => _desktopLyricService.IsShowing;

    /// <summary>设置当前歌词数据（切歌时调用）</summary>
    public void SetLyrics(LrcLyrics? lyrics)
    {
        _currentLyrics = lyrics;
        _currentLineIndex = -1;
        _desktopLyricService.SetLyrics(lyrics);
        if (lyrics == null && _desktopLyricService.IsShowing)
            MainThread.BeginInvokeOnMainThread(() => _desktopLyricService.UpdateLyric(""));
    }

    /// <summary>开启桌面歌词</summary>
    public async Task<bool> EnableAsync()
    {
#if ANDROID
        Android.Util.Log.Info(Tag, $"EnableAsync() called, IsShowing={_desktopLyricService.IsShowing}");
#endif
        // 检查权限
        if (!await _desktopLyricService.CheckPermissionAsync())
        {
#if ANDROID
            Android.Util.Log.Warn(Tag, "EnableAsync: permission not granted, requesting...");
#endif
            await _desktopLyricService.RequestPermissionAsync();
            EnableFailed?.Invoke();
            return false; // 需要用户授权后再次调用
        }

        // WindowManager.AddView 必须在 UI 线程
        await MainThread.InvokeOnMainThreadAsync(() => _desktopLyricService.Show());

        if (!_desktopLyricService.IsShowing)
        {
#if ANDROID
            Android.Util.Log.Error(Tag, "EnableAsync: Show() did not set IsShowing, firing EnableFailed");
#endif
            EnableFailed?.Invoke();
            return false;
        }

        LyricsSettingsService.Instance.DesktopLyricEnabled = true;
        StateChanged?.Invoke(true);

        // 立即更新一次当前歌词
        if (_currentLyrics != null)
        {
            var pos = TimeSpan.FromSeconds(_audioService.CurrentPosition);
            var (text, progress) = ComputeLyricUpdate(pos);
            if (text != null)
                _desktopLyricService.UpdateLyric(text);
            if (progress >= 0)
                _desktopLyricService.UpdateFillProgress(progress);
        }
#if ANDROID
        Android.Util.Log.Info(Tag, "EnableAsync: success");
#endif
        return true;
    }

    /// <summary>关闭桌面歌词</summary>
    public void Disable()
    {
        MainThread.BeginInvokeOnMainThread(() => _desktopLyricService.Hide());
        LyricsSettingsService.Instance.DesktopLyricEnabled = false;
        StateChanged?.Invoke(false);
    }

    /// <summary>从设置恢复桌面歌词状态（应用启动时调用）</summary>
    public async Task RestoreAsync()
    {
        if (!LyricsSettingsService.Instance.DesktopLyricEnabled) return;
        await EnableAsync();
    }

    /// <summary>应用设置变更</summary>
    public void ApplySettings()
    {
        _desktopLyricService.ApplySettings();
    }

    /// <summary>检查是否有悬浮窗权限</summary>
    public Task<bool> CheckPermissionAsync() => _desktopLyricService.CheckPermissionAsync();

    /// <summary>请求悬浮窗权限</summary>
    public Task<bool> RequestPermissionAsync() => _desktopLyricService.RequestPermissionAsync();

    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        if (!_desktopLyricService.IsShowing) return;
        // PositionChanged 可能在后台线程触发，UI 操作需切回主线程
        var snapshotLine = _currentLineIndex;
        var (text, progress) = ComputeLyricUpdate(position);
        if (text == null && progress < 0) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (text != null)
                _desktopLyricService.UpdateLyric(text);
            if (progress >= 0)
                _desktopLyricService.UpdateFillProgress(progress);
        });
    }

    /// <summary>计算当前应显示的歌词文本和填充进度（可在任意线程调用）</summary>
    private (string? text, double progress) ComputeLyricUpdate(TimeSpan position)
    {
        if (_currentLyrics == null || _currentLyrics.Lines.Count == 0)
            return ("", 1.0);

        var newIndex = _lyricsService.GetCurrentLyricIndex(_currentLyrics, position);
        if (newIndex < 0 || newIndex >= _currentLyrics.Lines.Count) return (null, -1);

        var line = _currentLyrics.Lines[newIndex];
        string? text = null;
        if (newIndex != _currentLineIndex)
        {
            _currentLineIndex = newIndex;
            text = line.Text;
        }

        double progress;
        var settingsMode = LyricsSettingsService.Instance.LyricsMode;
        if (settingsMode == LyricsSettingsService.Mode.Line)
        {
            progress = 1.0;
        }
        else
        {
            // 逐字模式
            var lineStart = line.Timestamp;
            var lineEnd = newIndex + 1 < _currentLyrics.Lines.Count
                ? _currentLyrics.Lines[newIndex + 1].Timestamp
                : lineStart + TimeSpan.FromSeconds(5);

            if (position <= lineStart)
                progress = 0;
            else if (position >= lineEnd)
                progress = 1.0;
            else if (line.WordTimestamps != null && line.WordTimestamps.Count > 0)
                progress = CalculateSyllableProgress(line.WordTimestamps, position, lineStart, lineEnd);
            else
            {
                var totalMs = (lineEnd - lineStart).TotalMilliseconds;
                var elapsedMs = (position - lineStart).TotalMilliseconds;
                progress = totalMs > 0 ? Math.Clamp(elapsedMs / totalMs, 0.0, 1.0) : 1.0;
            }
        }
        return (text, progress);
    }

    /// <summary>按音节时间戳计算填充进度</summary>
    private static double CalculateSyllableProgress(
        List<WordTimestamp> words, TimeSpan position, TimeSpan lineStart, TimeSpan lineEnd)
    {
        double totalChars = 0;
        foreach (var w in words) totalChars += w.Word.Length;
        if (totalChars <= 0) return 0;

        double filledChars = 0;
        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            var wordStart = lineStart + w.Start;
            var wordDur = w.Duration;
            var wordEnd = wordStart + wordDur;

            if (position >= wordEnd)
            {
                filledChars += w.Word.Length;
            }
            else if (position >= wordStart)
            {
                // 当前音节内按时间比例
                var wordProgress = wordDur.TotalMilliseconds > 0
                    ? (position - wordStart).TotalMilliseconds / wordDur.TotalMilliseconds
                    : 1.0;
                filledChars += w.Word.Length * Math.Clamp(wordProgress, 0.0, 1.0);
            }
        }

        return Math.Clamp(filledChars / totalChars, 0.0, 1.0);
    }
}
