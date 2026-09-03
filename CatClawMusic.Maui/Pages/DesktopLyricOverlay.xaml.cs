using System.Runtime.InteropServices;

#if WINDOWS
namespace CatClawMusic.Maui.Pages;

public partial class DesktopLyricOverlay : ContentPage
{
    public DesktopLyricOverlay() { InitializeComponent(); }

    public void UpdateLyric(string text, string textColor)
    {
        LyricLabel.Text = string.IsNullOrWhiteSpace(text) ? "欢迎使用猫爪音乐" : text;
        LyricLabel.TextColor = Microsoft.Maui.Graphics.Color.FromArgb(textColor);
    }

    /// <summary>更新当前行 + 下一行（双行模式）。next 为 null 时隐藏第二行。</summary>
    public void UpdateLyric(string text, string? next, string textColor)
    {
        UpdateLyric(text, textColor);
        if (NextLyricLabel == null) return;
        NextLyricLabel.Text = next ?? "";
        NextLyricLabel.IsVisible = !string.IsNullOrEmpty(next);
    }

    /// <summary>设置单行/双行模式：双行时当前行略上移，显示下一行。</summary>
    public void SetMode(bool doubleLine)
    {
        if (NextLyricLabel == null) return;
        NextLyricLabel.IsVisible = doubleLine && !string.IsNullOrEmpty(NextLyricLabel.Text);
    }

    public void SetFontSize(double f)
    {
        LyricLabel.FontSize = f;
        if (NextLyricLabel != null)
            NextLyricLabel.FontSize = Math.Max(12, f - 6);
    }
    public void SetPlaying(bool p) => PlayPauseBtn.Text = p ? "\u23F8" : "\u25B6";

    private bool _locked;
    public void SetLocked(bool l) { _locked = l; LockBtn.Text = l ? "\U0001F512" : "\U0001F513"; }

    /// <summary>
    /// 设置整窗半透明黑背景（网易云式：窗口透明 + 页面层半透明黑底 → 必生效，
    /// 不依赖亚克力渲染）。opacity 0=全透明，1=全黑不透明。
    /// 只记录用户期望黑度 _currentOpacity，实际显示由当前光标位置决定
    /// （鼠标在窗口内=半透明黑 / 移出=全透明），避免设置项与鼠标进出状态互相覆盖。
    /// </summary>
    public void SetBackgroundOpacity(double opacity)
    {
        _currentOpacity = Math.Clamp(opacity, 0.0, 1.0);
        if (_cursorInside)
            ApplyMouseInBackground();
        else
            ApplyMouseOutTransparent();
    }

    private double _currentOpacity = 0.55;

    // ─── 鼠标进出：窗口内半透明黑底 / 窗口外全透明（歌词文字保留） ───
    // 不依赖 PointerEntered/PointerExited 事件（MAUI 手势层与 WinUI 事件层在无边框
    // 悬浮窗口上均不可靠，实测多次丢失）→ 改为 Win32 轮询光标位置：
    // GetCursorPos + GetWindowRect 每 120ms 判断一次，100% 可靠。
    // RemoveSystemButtons 已去 WS_THICKFRAME，窗口矩形≈可见客户区，精度足够。
    //
    // 透明方案（纯 Composition，不再用 WS_EX_LAYERED + LWA_COLORKEY 颜色键）：
    // 颜色键只对 GDI 绘制的传统窗口有效，WinUI 3 通过 DirectComposition 在独立层
    // 渲染，SetLayeredWindowAttributes 无法影响该层（microsoft-ui-xaml#8469）。
    // 改为：TransparentTintBackdrop(0,0,0,0) 清 Composition Visual 层 +
    // DwmExtendFrameIntoClientArea(-1) 清 Win32 窗口层 +
    // MakeRootTransparent 深度清 MAUI 容器链 → 窗口真正全透明。
    // 鼠标移出时 RootLayout 直接设 Transparent，移入时设半透明黑底。
#if WINDOWS
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _cursorTimer;

    /// <summary>光标当前是否位于窗口客户区（可见区域）内，由轮询定时器维护。</summary>
    private bool _cursorInside;

    /// <summary>由 SetupWindow 注入的窗口句柄（多窗口场景 ContentPage.Window 可能为 null）。</summary>
    private IntPtr _injectedHwnd;

    /// <summary>注入窗口句柄，供 OnCursorTick 的 GetHwnd 使用。</summary>
    public void SetHwnd(IntPtr hwnd) => _injectedHwnd = hwnd;

    /// <summary>鼠标移出：全透明 + 隐藏工具栏（窗口透出桌面，只显示歌词）。</summary>
    public void ApplyMouseOutTransparent()
    {
        Toolbar.IsVisible = false;
        SetNativeBackground(0, 0, 0, 0);
    }

    /// <summary>鼠标移入：半透明黑底 + 显示工具栏。</summary>
    private void ApplyMouseInBackground()
    {
        Toolbar.IsVisible = true;
        SetNativeBackground(0, 0, 0, _currentOpacity);
    }

    /// <summary>
    /// 直接设置 WinUI 原生 Panel.Background，绕过 MAUI 属性变更检测。
    /// MakeRootTransparent 深度清背景时直接改了 WinUI Panel.Background，
    /// MAUI BackgroundColor setter 检测到托管值没变就跳过原生更新 → 背景卡透明。
    /// 直接操作原生属性确保每次都生效。
    /// </summary>
    private void SetNativeBackground(byte r, byte g, byte b, double opacity)
    {
        try
        {
            if (RootLayout.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.Panel panel)
            {
                var alpha = (byte)(Math.Clamp(opacity, 0, 1) * 255);
                panel.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(alpha, r, g, b));
            }
            else
            {
                RootLayout.BackgroundColor = opacity > 0
                    ? Microsoft.Maui.Graphics.Color.FromRgba(r, g, b, opacity)
                    : Microsoft.Maui.Graphics.Colors.Transparent;
            }
        }
        catch { }
    }

    /// <summary>用 DispatcherQueue 启动光标轮询（比 MAUI Dispatcher 更可靠，
    /// 与 ScheduleTransparentRetry 同方式）。由 SetupWindow 调用。</summary>
    public void StartCursorTracking(Microsoft.UI.Dispatching.DispatcherQueue queue)
    {
        try
        {
            if (_cursorTimer == null)
            {
                _cursorTimer = queue.CreateTimer();
                _cursorTimer.Interval = TimeSpan.FromMilliseconds(100);
                _cursorTimer.Tick += OnCursorTick;
            }
            _cursorTimer.Start();
        }
        catch { }
    }

    /// <summary>显式清理：停止 10Hz 光标轮询、解除注入句柄。
    /// 由服务层在真正关闭窗口时调用——OnDisappearing 会因多窗口失焦误触发，不能依赖；
    /// 旧版依赖"DispatcherQueueTimer 在窗口销毁时自动停止"，Service 仍持 Overlay 强引用时不可靠。</summary>
    public void Cleanup()
    {
        try
        {
            if (_cursorTimer != null)
            {
                _cursorTimer.Stop();
                _cursorTimer.Tick -= OnCursorTick;
                _cursorTimer = null;
            }
        }
        catch { }
        _injectedHwnd = IntPtr.Zero;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // 窗口重新激活时重启光标轮询（多窗口场景失焦后 OnDisappearing 不再停 timer，
        // 但 DispatcherQueue 可能被系统暂停，这里保险重启）
        try { _cursorTimer?.Start(); } catch { }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // 不停 timer！多窗口场景下用户点击其他窗口时 overlay 失焦会触发 OnDisappearing，
        // 停 timer 后 OnAppearing 不一定能重启 → 鼠标检测永久失效。
        // DispatcherQueueTimer 在窗口销毁时自动停止，无需手动管理。
    }

    private void OnCursorTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object e)
    {
        try
        {
            var h = GetHwnd();
            if (h == IntPtr.Zero) return;
            if (!GetCursorPos(out var pt)) return;
            if (!GetWindowRect(h, out var rc)) return;
            var inside = pt.X >= rc.Left && pt.X < rc.Right && pt.Y >= rc.Top && pt.Y < rc.Bottom;

            // 鼠标进出状态变化 → 切换背景/工具栏
            if (inside != _cursorInside)
            {
                _cursorInside = inside;
                if (inside)
                    ApplyMouseInBackground();
                else
                    ApplyMouseOutTransparent();
            }
            // 拖动由 RootLayout 上的 PanGestureRecognizer 处理，无需轮询
        }
        catch { }
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }
#endif

    public event EventHandler? PrevClicked, PlayPauseClicked, NextClicked, LockClicked, CloseClicked;
    private void OnPrev(object? s, EventArgs e) => PrevClicked?.Invoke(s!, e);
    private void OnPlayPause(object? s, EventArgs e) => PlayPauseClicked?.Invoke(s!, e);
    private void OnNext(object? s, EventArgs e) => NextClicked?.Invoke(s!, e);
    private void OnLock(object? s, EventArgs e) => LockClicked?.Invoke(s!, e);
    private void OnClose(object? s, EventArgs e) => CloseClicked?.Invoke(s!, e);

#if WINDOWS
    private int _startX, _startY;

    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        // 锁定状态禁止拖动（防止误触移动桌面歌词位置）
        if (_locked) return;
        var h = GetHwnd(); if (h == IntPtr.Zero) return;
        if (e.StatusType == GestureStatus.Running)
            NativeMoveWindow(h, _startX + (int)e.TotalX, _startY + (int)e.TotalY);
        else if (e.StatusType == GestureStatus.Started)
            NativeGetPos(h, out _startX, out _startY);
    }

    private IntPtr GetHwnd()
    {
        if (_injectedHwnd != IntPtr.Zero) return _injectedHwnd;
        try { if (Window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window nw) return WinRT.Interop.WindowNative.GetWindowHandle(nw); }
        catch { }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    private const uint SWP_NOSIZE = 1, SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10;

    private static void NativeGetPos(IntPtr hWnd, out int x, out int y)
    { if (GetWindowRect(hWnd, out var r)) { x = r.Left; y = r.Top; } else { x = y = 0; } }

    private static void NativeMoveWindow(IntPtr hWnd, int x, int y)
    { SetWindowPos(hWnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE); }
#endif
}
#endif