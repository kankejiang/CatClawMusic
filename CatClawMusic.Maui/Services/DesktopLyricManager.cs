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
        lock (_updateLock)
        {
            _currentLyrics = lyrics;
            _currentLineIndex = -1;
            // 丢弃未消费的旧歌词 pending，防止切歌后旧行文本闪现
            _pendingLyricText = null;
            _pendingNextText = null;
            _pendingLyricProgress = -1;
            _hasPending = false;
        }
        _desktopLyricService.SetLyrics(lyrics);
        if (lyrics == null && _desktopLyricService.IsShowing)
            MainThread.BeginInvokeOnMainThread(() => _desktopLyricService.UpdateLyricLines("", null, -1));
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
            (string? text, string? next, double progress) update;
            lock (_updateLock) { update = ComputeLyricUpdate(pos); }
            if (update.text != null || update.next != null)
                _desktopLyricService.UpdateLyricLines(update.text, update.next, update.progress);
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

    // ─── 主线程合并(latest-wins)───
    // PositionChanged 最高约 60Hz:每个 tick 只更新 pending 快照,主线程队列最多一个 drain 回调,
    // 避免 60Hz 投递堆积;drain 时一次性取走最新值。锁同时保护 _currentLineIndex(ComputeLyricUpdate
    // 会推进行号)与 SetLyrics(切歌)互斥,并保证"切行文本不会被后续 progress-only tick 覆盖为空"。
    private readonly object _updateLock = new();
    private string? _pendingLyricText;
    private string? _pendingNextText;
    private double _pendingLyricProgress = -1;
    private bool _hasPending;
    private int _updatePosted;              // 0/1:主线程队列中是否已有待执行的 drain
    private Action? _cachedLyricUpdate;     // 缓存委托,避免每 tick 分配闭包

    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        if (!_desktopLyricService.IsShowing) return;
        // 用户交互（Tab 滑动/列表滚动）时暂停桌面歌词更新，避免主线程消息队列堆积影响流畅度。
        // 滑动停止后会自动恢复（下一个 tick 即同步到当前位置）。
        if (_interactionState?.IsUserInteracting == true) return;

        string? text, next; double progress;
        lock (_updateLock)
        {
            (text, next, progress) = ComputeLyricUpdate(position);
            if (text == null && next == null && progress < 0) return;

            if (text != null)
            {
                // 行切换:当前行/下一行整体替换(允许 next=null 表示单行模式或无下一行)
                _pendingLyricText = text;
                _pendingNextText = next;
            }
            _pendingLyricProgress = progress;
            _hasPending = true;
        }

        // 主线程队列中已有一个 drain → 本次 tick 只合并数据,不再投递
        if (Interlocked.Exchange(ref _updatePosted, 1) != 0) return;
        _cachedLyricUpdate ??= DrainPendingUpdate;
        MainThread.BeginInvokeOnMainThread(_cachedLyricUpdate);
    }

    /// <summary>主线程 drain:取走最新 pending 快照并应用到悬浮窗(每个 UI 帧最多一次)</summary>
    private void DrainPendingUpdate()
    {
        string? text = null, next = null;
        double progress = -1;
        bool has;
        lock (_updateLock)
        {
            has = _hasPending;
            if (has)
            {
                text = _pendingLyricText;
                next = _pendingNextText;
                progress = _pendingLyricProgress;
                _pendingLyricText = null;
                _pendingNextText = null;
                _pendingLyricProgress = -1;
                _hasPending = false;
            }
        }
        // 先清投递标志再应用:drain 期间到达的新 tick 可重新入队,不丢最后一帧
        Interlocked.Exchange(ref _updatePosted, 0);

        if (!has) return;
        if (!_desktopLyricService.IsShowing) return; // Hide 后不再触碰悬浮窗

        if (text != null || next != null)
            _desktopLyricService.UpdateLyricLines(text, next, progress);
        else if (progress >= 0)
            _desktopLyricService.UpdateFillProgress(progress);
    }

    /// <summary>
    /// 计算当前应显示的歌词行（当前行 + 双行模式下的下一行）与填充进度（可在任意线程调用）。
    /// </summary>
    private (string? text, string? next, double progress) ComputeLyricUpdate(TimeSpan position)
    {
        if (_currentLyrics == null || _currentLyrics.Lines.Count == 0)
            return ("", null, 1.0);

        var newIndex = _lyricsService.GetCurrentLyricIndex(_currentLyrics, position);
        if (newIndex < 0 || newIndex >= _currentLyrics.Lines.Count) return (null, null, -1);

        var line = _currentLyrics.Lines[newIndex];
        string? text = null;
        string? next = null;
        if (newIndex != _currentLineIndex)
        {
            _currentLineIndex = newIndex;
            text = line.Text;
            // 双行模式：行变化时附带下一行文本（无下一行时为 null，仅显示当前行）
            if (LyricsSettingsService.Instance.DesktopLyricMode == LyricsSettingsService.DesktopMode.Double
                && newIndex + 1 < _currentLyrics.Lines.Count)
            {
                next = _currentLyrics.Lines[newIndex + 1].Text;
            }
        }

        var lineMode = LyricsSettingsService.Instance.LyricsMode == LyricsSettingsService.Mode.Line;
        var progress = LyricFillCalculator.ComputeFillProgress(
            line, newIndex, _currentLyrics.Lines, position, lineMode);

        return (text, next, progress);
    }
}
