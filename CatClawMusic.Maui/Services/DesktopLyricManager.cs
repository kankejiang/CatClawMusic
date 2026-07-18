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
    private readonly IInteractionStateService? _interactionState;
    private LrcLyrics? _currentLyrics;
    private int _currentLineIndex = -1;

    /// <summary>桌面歌词开关状态变化事件</summary>
    public event Action<bool>? StateChanged;

    /// <summary>桌面歌词开启失败事件（权限不足等）</summary>
    public event Action? EnableFailed;

    public DesktopLyricManager(
        IAudioPlayerService audioService,
        ILyricsService lyricsService,
        IDesktopLyricService desktopLyricService,
        IInteractionStateService? interactionState = null)
    {
        _audioService = audioService;
        _lyricsService = lyricsService;
        _desktopLyricService = desktopLyricService;
        _interactionState = interactionState;

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

#if ANDROID
        // 同步通知栏桌面歌词按钮状态（从设置页开启时通知栏状态可能不一致）
        Platforms.Android.ForegroundPlayerService.SyncLyricsEnabled(true);
#endif

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

    /// <summary>关闭桌面歌词（通知栏关闭时调用，同时解锁）</summary>
    public void Disable()
    {
        MainThread.BeginInvokeOnMainThread(() => _desktopLyricService.Hide());
        LyricsSettingsService.Instance.DesktopLyricEnabled = false;
        // 关闭时解锁，下次开启时为解锁状态
        LyricsSettingsService.Instance.DesktopLocked = false;
#if ANDROID
        // 同步通知栏桌面歌词按钮状态
        Platforms.Android.ForegroundPlayerService.SyncLyricsEnabled(false);
#endif
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

    // 缓存主线程调度委托，避免每次 tick 创建新 Action 闭包
    private string? _pendingLyricText;
    private double _pendingLyricProgress = -1;
    private Action? _cachedLyricUpdate;

    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        if (!_desktopLyricService.IsShowing) return;
        // 用户交互（Tab 滑动/列表滚动）时暂停桌面歌词更新，避免主线程消息队列堆积影响流畅度。
        // 滑动停止后会自动恢复（下一个 tick 即同步到当前位置）。
        // IsUserInteracting 同时覆盖 Tab 滑动（BeginInteraction）和列表滚动（NotifyScrollStarted）。
        if (_interactionState?.IsUserInteracting == true) return;
        // PositionChanged 可能在后台线程触发，UI 操作需切回主线程
        var (text, progress) = ComputeLyricUpdate(position);
        if (text == null && progress < 0) return;

        _pendingLyricText = text;
        _pendingLyricProgress = progress;
        _cachedLyricUpdate ??= () =>
        {
            if (_pendingLyricText != null)
                _desktopLyricService.UpdateLyric(_pendingLyricText);
            if (_pendingLyricProgress >= 0)
                _desktopLyricService.UpdateFillProgress(_pendingLyricProgress);
        };
        MainThread.BeginInvokeOnMainThread(_cachedLyricUpdate);
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

        var lineMode = LyricsSettingsService.Instance.LyricsMode == LyricsSettingsService.Mode.Line;
        var progress = LyricFillCalculator.ComputeFillProgress(
            line, newIndex, _currentLyrics.Lines, position, lineMode);

        return (text, progress);
    }
}
