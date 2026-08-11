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
    private LrcLyrics? _lyrics;
    private string _currentText = "";
    private LyricsSettingsService S => LyricsSettingsService.Instance;
    public bool IsShowing => _window != null;

    public void Show()
    {
        try { if (_window != null) return; _overlay = new DesktopLyricOverlay(); BindEvents(); _window = new Microsoft.Maui.Controls.Window(_overlay) { Title = "" }; _window.HandlerChanged += SetupWindow; _window.Destroying += (_, _) => _window = null; ApplyLook(); Application.Current?.OpenWindow(_window); if (_window.Handler != null) SetupWindow(null, EventArgs.Empty); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"{nameof(WindowsDesktopLyricServiceV2)} Show: {ex.Message}"); }
    }

    public void Hide() { if (_window == null) return; var w = _window; _window = null; Application.Current?.CloseWindow(w); }
    public void UpdateLyric(string? text) { _currentText = text ?? ""; MainThread.BeginInvokeOnMainThread(() => _overlay?.UpdateLyric(_currentText, S.DesktopHighlightColor)); }
    public void SetLyrics(LrcLyrics? lyrics) => _lyrics = lyrics;
    public void UpdateFillProgress(double p) { }
    public void ApplySettings() => MainThread.BeginInvokeOnMainThread(ApplyLook);
    public Task<bool> CheckPermissionAsync() => Task.FromResult(true);
    public Task<bool> RequestPermissionAsync() => Task.FromResult(true);

    private void BindEvents()
    {
        if (_overlay == null) return;
        var audio = MauiProgram.Services.GetService<IAudioPlayerService>();
        _overlay.PrevClicked += (_, _) => { };
        _overlay.PlayPauseClicked += (_, _) => _ = (audio?.IsPlaying == true ? audio.PauseAsync() : audio?.ResumeAsync());
        _overlay.NextClicked += (_, _) => { };
        _overlay.LockClicked += (_, _) => ToggleLock();
        _overlay.CloseClicked += (_, _) => Hide();
    }

    private void ToggleLock() { S.DesktopLocked = !S.DesktopLocked; _overlay?.SetLocked(S.DesktopLocked); }

    private void ApplyLook()
    {
        if (_overlay == null) return;
        _overlay.SetFontSize(S.DesktopFontSize);
        _overlay.UpdateLyric(_currentText, S.DesktopHighlightColor);
        _overlay.SetLocked(S.DesktopLocked);
        // 背景黑度滑条 → 页面层半透明黑底（网易云式；0.08 下限保证文字可读性）
        _overlay.SetBackgroundOpacity(Math.Clamp(S.DesktopBgOpacity, 0.08, 1.0));
    }

    private void SetupWindow(object? sender, EventArgs e)
    {
#if WINDOWS
        try
        {
            if (_window?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWin) return;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWin);
            var wid = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(wid);
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
            var h = (int)(Math.Max(170, S.DesktopFontSize * 7.0) * scale);
            appWindow.MoveAndResize(new global::Windows.Graphics.RectInt32
            { X = work.X + (work.Width - w) / 2, Y = work.Y + (int)(work.Height * Math.Clamp(S.DesktopPosY, 0.1, 0.95)), Width = w, Height = h });
            appWindow.Changed += (_, args) => { if (args.DidPositionChange) S.DesktopPosY = Math.Clamp((appWindow.Position.Y - work.Y) / (double)work.Height, 0.1, 0.95); };

            // 拖动由 DesktopLyricOverlay 的 PanGestureRecognizer 处理（背景区域拖拽），
            // 不使用 AppWindow.TitleBar.SetDragRectangles——该方法依赖 WS_CAPTION 标题栏，
            // 而 RemoveSystemButtons 已去除 WS_CAPTION，两者冲突会导致拖动失效。

            // 深度清透明会连页面 RootLayout 的半透明黑底一起清掉，这里重新施加页面背景
            ApplyLook();
        }
        catch { }
#endif
    }

    /// <summary>
    /// 延迟多轮深度清 MAUI 容器链背景（幂等）：MAUI 在 HandlerChanged 之后、布局完成时
    /// 才设置容器结构/样式，此时 Window.Content 树可能尚未完整构建。用 DispatcherQueueTimer
    /// 每 350ms 重做一次 MakeRootTransparent + ApplyLook（共 3 次），覆盖 MAUI 后续布局时机；
    /// 鼠标位置由 Overlay 的光标轮询（120ms）独立维护。
    /// </summary>
    private void ScheduleTransparentRetry(Microsoft.UI.Xaml.Window nativeWin)
    {
        try
        {
            var queue = nativeWin.DispatcherQueue;
            if (queue == null) return;
            var timer = queue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(350);
            timer.IsRepeating = true;
            var clears = 0;
            timer.Tick += (_, _) =>
            {
                try
                {
                    DesktopLyricNativeHelper.MakeRootTransparent(nativeWin);
                    MainThread.BeginInvokeOnMainThread(ApplyLook);
                    if (++clears >= 3) timer.Stop();
                }
                catch { }
            };
            timer.Start();
        }
        catch { }
    }
}
#endif