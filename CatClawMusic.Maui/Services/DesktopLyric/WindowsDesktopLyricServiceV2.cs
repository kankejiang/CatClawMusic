using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Services.DesktopLyric;
using CatClawMusic.Maui.Pages;

#if WINDOWS
namespace CatClawMusic.Maui.Services;

public class WindowsDesktopLyricServiceV2 : IDesktopLyricService
{
    private Microsoft.Maui.Controls.Window? _window;
    private DesktopLyricOverlay? _overlay;
    private Microsoft.UI.Windowing.AppWindow? _appWindow;
    private LrcLyrics? _lyrics;
    private string _currentText = "";
    private LyricsSettingsService S => LyricsSettingsService.Instance;
    public bool IsShowing => _window != null;

    // 透明重试 timer 与其目标窗口：字段化以便 Hide/Destroying 时停止并退订
    // （旧版局部 timer 匿名闭包无法停止，Service 关闭后仍可能残留 350ms 轮询）
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _transparentRetryTimer;
    private Microsoft.UI.Xaml.Window? _transparentRetryWindow;
    private int _transparentRetryClears;

    public void Show()
    {
        try
        {
            if (_window != null) return;
            _overlay = new DesktopLyricOverlay();
            BindEvents();
            _window = new Microsoft.Maui.Controls.Window(_overlay) { Title = "" };
            _window.HandlerChanged += OnWindowHandlerChanged;
            _window.Destroying += OnWindowDestroying;
            ApplyLook();
            Application.Current?.OpenWindow(_window);
            if (_window.Handler != null) SetupWindow(null, EventArgs.Empty);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"{nameof(WindowsDesktopLyricServiceV2)} Show: {ex.Message}"); }
    }

    public void Hide()
    {
        if (_window == null) return;
        CleanupWindow(_window, closeWindow: true);
    }

    /// <summary>统一清理（Hide 与 Destroying 共用，幂等）：停透明重试 timer → 停 Overlay 光标
    /// 轮询 → 解绑 Overlay/AppWindow/Window 全部事件 → 清引用 → （可选）关闭窗口。
    /// 旧版 Hide 只 CloseWindow，长生命周期 Service 永久持有已关闭窗口的 Overlay/AppWindow
    /// 与 10Hz 轮询 timer，反复开关累积泄漏。</summary>
    private void CleanupWindow(Microsoft.Maui.Controls.Window? window, bool closeWindow)
    {
        try
        {
            if (_transparentRetryTimer != null)
            {
                _transparentRetryTimer.Tick -= OnTransparentRetryTick;
                _transparentRetryTimer.Stop();
                _transparentRetryTimer = null;
                _transparentRetryWindow = null;
                _transparentRetryClears = 0;
            }

            if (_overlay != null)
            {
                _overlay.PrevClicked -= OnOverlayPrev;
                _overlay.PlayPauseClicked -= OnOverlayPlayPause;
                _overlay.NextClicked -= OnOverlayNext;
                _overlay.LockClicked -= OnOverlayLock;
                _overlay.CloseClicked -= OnOverlayClose;
                _overlay.Cleanup();
            }

            if (_window != null)
            {
                _window.HandlerChanged -= OnWindowHandlerChanged;
                _window.Destroying -= OnWindowDestroying;
            }

            if (_appWindow != null)
                _appWindow.Changed -= OnAppWindowChanged;
        }
        catch { }

        _overlay = null;
        _appWindow = null;

        if (closeWindow && window != null)
        {
            _window = null;
            try { Application.Current?.CloseWindow(window); } catch { }
        }
        else if (ReferenceEquals(_window, window))
        {
            _window = null;
        }
    }

    private void OnWindowDestroying(object? sender, EventArgs e)
    {
        // 窗口正在销毁：只做资源解绑，不再调用 CloseWindow（避免在 Destroying 中二次关闭）
        CleanupWindow(sender as Microsoft.Maui.Controls.Window, closeWindow: false);
    }

    public void UpdateLyric(string? text) { _currentText = text ?? ""; MainThread.BeginInvokeOnMainThread(() => _overlay?.UpdateLyric(_currentText, S.DesktopHighlightColor)); }
    public void UpdateLyricLines(string? currentText, string? nextText, double progress)
    {
        _currentText = currentText ?? "";
        MainThread.BeginInvokeOnMainThread(() => _overlay?.UpdateLyric(_currentText, nextText, S.DesktopHighlightColor));
    }
    public void SetLyrics(LrcLyrics? lyrics) => _lyrics = lyrics;
    public void UpdateFillProgress(double p) { }
    public void ApplySettings() => MainThread.BeginInvokeOnMainThread(ApplyLook);
    public Task<bool> CheckPermissionAsync() => Task.FromResult(true);
    public Task<bool> RequestPermissionAsync() => Task.FromResult(true);

    // ─── Overlay 按钮事件（命名 handler：可退订；匿名 lambda 无法 -=）───

    private void BindEvents()
    {
        if (_overlay == null) return;
        _overlay.PrevClicked += OnOverlayPrev;
        _overlay.PlayPauseClicked += OnOverlayPlayPause;
        _overlay.NextClicked += OnOverlayNext;
        _overlay.LockClicked += OnOverlayLock;
        _overlay.CloseClicked += OnOverlayClose;
    }

    private void OnOverlayPrev(object? sender, EventArgs e) { }

    private void OnOverlayPlayPause(object? sender, EventArgs e)
    {
        var audio = MauiProgram.Services.GetService<IAudioPlayerService>();
        _ = (audio?.IsPlaying == true ? audio.PauseAsync() : audio?.ResumeAsync());
    }

    private void OnOverlayNext(object? sender, EventArgs e) { }

    private void OnOverlayLock(object? sender, EventArgs e) => ToggleLock();

    private void OnOverlayClose(object? sender, EventArgs e) => Hide();

    private void ToggleLock() { S.DesktopLocked = !S.DesktopLocked; _overlay?.SetLocked(S.DesktopLocked); }

    private void ApplyLook()
    {
        if (_overlay == null) return;
        _overlay.SetFontSize(S.DesktopFontSize);
        _overlay.UpdateLyric(_currentText, S.DesktopHighlightColor);
        _overlay.SetLocked(S.DesktopLocked);
        // 背景黑度滑条 → 页面层半透明黑底（网易云式；0.08 下限保证文字可读性）
        _overlay.SetBackgroundOpacity(Math.Clamp(S.DesktopBgOpacity, 0.08, 1.0));
        // 单行/双行模式 → 布局 + 窗口高度（双行需要更高窗口容纳第二行）
        _overlay.SetMode(S.DesktopLyricMode == LyricsSettingsService.DesktopMode.Double);
        ResizeForMode();
    }

    /// <summary>按当前模式调整窗口高度（单行 fontSize×7 / 双行 fontSize×10），顶部位置保持不变。</summary>
    private void ResizeForMode()
    {
        try
        {
            if (_appWindow == null || _window?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWin) return;
            var doubleLine = S.DesktopLyricMode == LyricsSettingsService.DesktopMode.Double;
            var scale = DesktopLyricNativeHelper.GetScaleAdjustment(nativeWin);
            var w = (int)(980 * scale);
            var h = (int)(Math.Max(170, S.DesktopFontSize * (doubleLine ? 10.0 : 7.0)) * scale);
            _appWindow.MoveAndResize(new global::Windows.Graphics.RectInt32
            { X = _appWindow.Position.X, Y = _appWindow.Position.Y, Width = w, Height = h });
        }
        catch { }
    }

    private void OnWindowHandlerChanged(object? sender, EventArgs e) => SetupWindow(sender, e);

    private void SetupWindow(object? sender, EventArgs e)
    {
#if WINDOWS
        try
        {
            if (_window?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWin) return;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWin);
            var wid = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(wid);

            // AppWindow.Changed 订阅去重（HandlerChanged 与 Show 的显式调用可能重复进入）
            if (!ReferenceEquals(_appWindow, appWindow))
            {
                if (_appWindow != null) _appWindow.Changed -= OnAppWindowChanged;
                _appWindow = appWindow;
                appWindow.Changed += OnAppWindowChanged;
            }
            var scale = DesktopLyricNativeHelper.GetScaleAdjustment(nativeWin);

            // Standard presenter: disable min/max (only close stays)
            if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p)
            { p.IsMinimizable = false; p.IsMaximizable = false; }

            // Extend into title bar—required for DWM acrylic
            appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            nativeWin.SetTitleBar(null);

            // ⚠️ 不要用 CreateForContextMenu 做无边框！它生成的 WS_POPUP 无阴影窗口
            // 不参与 DWM 亚克力渲染（半透明毛玻璃完全不生效，已验证）。
            // 保持标准 OverlappedPresenter（有阴影/装饰）+ Win32 去 caption 按钮即可。
            DesktopLyricNativeHelper.RemoveSystemButtons(hwnd);

            // Topmost
            DesktopLyricNativeHelper.SetTopMost(hwnd);

            // 网易云式半透明（WinUIEx 成熟开源方案）：
            // ① DWM 扩展客户区 → 去 Win32 窗口层背景（窗口透明前提，勿删！）
            DesktopLyricNativeHelper.ExtendFrameIntoClientArea(hwnd);

            // ② SystemBackdrop = WinUIEx.TransparentTintBackdrop → 解决
            //    Composition Visual 层黑底 + 窗口透桌面（微软默认 DesktopAcrylicBackdrop
            //    在无边框窗口回退纯色，WinUIEx 正确处理）。
            try
            {
                nativeWin.SystemBackdrop = new WinUIEx.TransparentTintBackdrop(
                    Windows.UI.Color.FromArgb(0, 0, 0, 0));
            }
            catch { }

            // ③ 深度清 MAUI 容器链背景：Window.Content → MAUI 根容器 → 页面宿主，
            //    清除残留的不透明主题色/白底。透明三件套至此完备：
            //    ① DwmExtendFrameIntoClientArea(-1) 清 Win32 窗口层（上方已做）
            //    ② TransparentTintBackdrop(0,0,0,0) 清 Composition Visual 层（上方已做）
            //    ③ MakeRootTransparent 清 MAUI/XAML 容器链（此处）
            //    三步齐全 → 窗口真正全透明（透出桌面），歌词文字正常显示。
            //    注意：不再使用 WS_EX_LAYERED + LWA_COLORKEY 颜色键方案——
            //    颜色键只对 GDI 窗口有效，WinUI 3 DirectComposition 渲染不受其影响
            //    （microsoft-ui-xaml#8469），且 WS_EX_LAYERED 会干扰 Composition 合成。
            DesktopLyricNativeHelper.MakeRootTransparent(nativeWin);

            // 注入窗口句柄：多窗口场景 ContentPage.Window 可能为 null，
            // OnCursorTick 的 GetHwnd 依赖此注入才能可靠获取窗口客户区。
            _overlay?.SetHwnd(hwnd);

            // 初始化：鼠标位置未知，先按"移出"处理（窗口即刻全透明，tick 120ms 内纠正）
            _overlay?.ApplyMouseOutTransparent();

            // 确保光标轮询启动（用 DispatcherQueue 比 MAUI Dispatcher 更可靠）
            _overlay?.StartCursorTracking(nativeWin.DispatcherQueue);

            // 延迟重试深度清背景：MAUI 布局完成时机晚于 HandlerChanged，
            // 容器链可能尚未完整构建，350ms × 3 次重做 MakeRootTransparent 覆盖布局时机。
            ScheduleTransparentRetry(nativeWin);

            // Size and position
            var work = Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea;
            var w = (int)(980 * scale);
            var doubleLine = S.DesktopLyricMode == LyricsSettingsService.DesktopMode.Double;
            var h = (int)(Math.Max(170, S.DesktopFontSize * (doubleLine ? 10.0 : 7.0)) * scale);
            appWindow.MoveAndResize(new global::Windows.Graphics.RectInt32
            { X = work.X + (work.Width - w) / 2, Y = work.Y + (int)(work.Height * Math.Clamp(S.DesktopPosY, 0.1, 0.95)), Width = w, Height = h });

            // 拖动由 DesktopLyricOverlay 的 PanGestureRecognizer 处理（背景区域拖拽），
            // 不使用 AppWindow.TitleBar.SetDragRectangles——该方法依赖 WS_CAPTION 标题栏，
            // 而 RemoveSystemButtons 已去除 WS_CAPTION，两者冲突会导致拖动失效。

            // 深度清透明会连页面 RootLayout 的半透明黑底一起清掉，这里重新施加页面背景
            ApplyLook();
        }
        catch { }
#endif
    }

    /// <summary>窗口位置变化 → 保存 Y 比例（命名 handler，可随 CleanupWindow 退订）</summary>
    private void OnAppWindowChanged(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
    {
        if (!args.DidPositionChange) return;
        try
        {
            var work = Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea;
            S.DesktopPosY = Math.Clamp((sender.Position.Y - work.Y) / (double)work.Height, 0.1, 0.95);
        }
        catch { }
    }

    /// <summary>
    /// 延迟多轮深度清 MAUI 容器链背景（幂等）：MAUI 在 HandlerChanged 之后、布局完成时
    /// 才设置容器结构/样式，此时 Window.Content 树可能尚未完整构建。用 DispatcherQueueTimer
    /// 每 350ms 重做一次 MakeRootTransparent + ApplyLook（共 3 次），覆盖 MAUI 后续布局时机；
    /// 鼠标位置由 Overlay 的光标轮询（120ms）独立维护。timer 字段化，CleanupWindow 时停止。
    /// </summary>
    private void ScheduleTransparentRetry(Microsoft.UI.Xaml.Window nativeWin)
    {
        try
        {
            var queue = nativeWin.DispatcherQueue;
            if (queue == null) return;
            _transparentRetryTimer = queue.CreateTimer();
            _transparentRetryTimer.Interval = TimeSpan.FromMilliseconds(350);
            _transparentRetryTimer.IsRepeating = true;
            _transparentRetryClears = 0;
            _transparentRetryWindow = nativeWin;
            _transparentRetryTimer.Tick += OnTransparentRetryTick;
            _transparentRetryTimer.Start();
        }
        catch { }
    }

    private void OnTransparentRetryTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object e)
    {
        try
        {
            var win = _transparentRetryWindow;
            if (win == null) { sender.Stop(); return; }
            DesktopLyricNativeHelper.MakeRootTransparent(win);
            MainThread.BeginInvokeOnMainThread(ApplyLook);
            if (++_transparentRetryClears >= 3) sender.Stop();
        }
        catch { }
    }
}
#endif
