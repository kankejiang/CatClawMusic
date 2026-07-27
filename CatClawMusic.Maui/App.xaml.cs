using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Services;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using System.Threading;

namespace CatClawMusic.Maui;

public partial class App : Application
{
#if WINDOWS
    public static Microsoft.UI.Windowing.AppWindow? CurrentAppWindow { get; set; }
#endif

    public App()
    {
        StartupLog("App.ctor: InitializeComponent start");
        InitializeComponent();
        StartupLog("App.ctor: InitializeComponent done");

        // 应用已保存的语言偏好（在 UI 构建前生效，确保首屏即正确语言）
        try
        {
            LocalizationService.Initialize();
            StartupLog("App.ctor: Localization initialized");
        }
        catch (Exception ex) { StartupLog($"App.ctor: Localization failed - {ex.Message}"); }

        // 应用主题
        try
        {
            var themeService = MauiProgram.Services.GetRequiredService<IThemeService>();
            themeService.ApplyTheme();
            // 系统深浅色变化（跟随系统模式）时实时重应用主题，避免需重启才生效
            this.RequestedThemeChanged += (s, e) =>
            {
                MainThread.BeginInvokeOnMainThread(() => themeService.ApplyTheme());
            };
            StartupLog("App.ctor: Theme applied");
        }
        catch (Exception ex) { StartupLog($"App.ctor: Theme failed - {ex.Message}"); }

        // 设置 LyricsService 的 PluginManager 和 NetworkMusicServiceFactory（属性注入，避免循环依赖）
        var lyricsService = MauiProgram.Services.GetService<ILyricsService>() as LyricsService;
        if (lyricsService != null)
        {
            lyricsService.PluginManager = MauiProgram.Services.GetRequiredService<IPluginManager>();
            lyricsService.NetworkMusicServiceFactory = () => MauiProgram.Services.GetService<INetworkMusicService>();
        }

        // 初始化所有已启用的插件（fire-and-forget）
        _ = Task.Run(async () =>
        {
            try { await MauiProgram.Services.GetRequiredService<IPluginManager>().InitializeAllAsync(); }
            catch (Exception ex)
            {
                Log.Debug("App.xaml", $"[CatClaw] PluginManager init failed: {ex.Message}");
            }
        });

        StartupLog("App.ctor: done");
    }

    /// <summary>应用启动：诊断日志默认关闭，仅由设置页开关控制。
    /// 若用户此前已开启（按 Preferences 持久化恢复），则在后台恢复记录并弹一次非阻塞 Toast 提示。</summary>
    protected override void OnStart()
    {
        base.OnStart();

        // 冷启动时清理 MAUI Share 框架遗留的中转缓存（external cache 下 <固定uuid>/<随机uuid>/ 目录）。
        // 放在冷启动而非"分享完成后"，是因为接收端 App 是异步读取 content URI 的，
        // 分享后立即删会误删正在被读取的文件，导致接收端报"文件不存在"。
        _ = Task.Run(() =>
        {
            try { Pages.NowPlayingPage.CleanupShareStagingCache(); }
            catch { }
        });

        // 若上次已开启诊断日志：后台线程记录一条启动标记（构造时已按 Preferences 恢复 IsEnabled）
        _ = Task.Run(() =>
        {
            try
            {
                if (LogService.Instance is { } log && log.IsEnabled)
                {
                    log.Info("App", "诊断日志已开启（按上次设置恢复，崩溃堆栈将写入 debug.log）");
                    log.Flush();
                }
            }
            catch { }
        });

        // 仅当已开启时提示用户去哪个文件夹取文件（非阻塞 Toast，约 3.5 秒后自动消失）
        _ = MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await Task.Delay(700);
            try
            {
#if ANDROID
                if (LogService.Instance is { } log && log.IsEnabled)
                    ShowDiagnosticToast();
#endif
            }
            catch { }
        });
    }

#if ANDROID
    /// <summary>启动提示 Toast：告知用户诊断日志已开始记录、去哪个文件夹取文件。</summary>
    private void ShowDiagnosticToast()
    {
        try
        {
            var ctx = Android.App.Application.Context;
            var msg = "已开始记录诊断日志\n用文件管理器打开 CatClawMusic 文件夹，取 debug.log 发我";
            var toast = Android.Widget.Toast.MakeText(ctx, msg, Android.Widget.ToastLength.Long);
            toast.Show();
        }
        catch { }
    }
#endif

    /// <summary>应用进入后台时调用：flush 听歌时长，避免被系统杀死时丢失数据</summary>
    protected override void OnSleep()
    {
        base.OnSleep();
#if ANDROID
#endif
        try
        {
            var vm = MauiProgram.Services.GetService<NowPlayingViewModel>();
            vm?.OnAppSleep();
        }
        catch (Exception ex)
        {
            Log.Debug("App.xaml", $"[OnSleep] flush 听歌时长失败: {ex.Message}");
        }
    }

    /// <summary>应用从后台恢复时调用：重启听歌时长计时</summary>
    protected override void OnResume()
    {
        base.OnResume();
#if ANDROID
        // 兜底：后台期间旋转且 MainDisplayInfoChanged 未触发时，回前台立即校正布局
        ApplyOrientationLayout();
#endif
        try
        {
            var vm = MauiProgram.Services.GetService<NowPlayingViewModel>();
            vm?.OnAppResume();
        }
        catch (Exception ex)
        {
            Log.Debug("App.xaml", $"[OnResume] 重启听歌计时失败: {ex.Message}");
        }
    }

    private static void StartupLog(string msg)
    {
        Log.Debug("App.xaml", $"[STARTUP] {msg}");
        try
        {
            var logPath = Path.Combine(Path.GetTempPath(), "catclaw_startup.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] APP: {msg}\n");
        }
        catch { }
    }

    /// <summary>
    /// Shell 导航完成后触发：为非 MainPage 的二级页面根 Grid 自动添加 SafeAreaPaddingBehavior，
    /// 让内容避开状态栏区域。已添加 Behavior 的页面不会重复添加。
    /// </summary>
    private void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
    {
        if (sender is not Shell shell) return;
        var currentPage = shell.CurrentPage;
        if (currentPage == null) return;

        // 全面屏页面自身处理 SafeArea（毛玻璃/歌词背景延伸到系统栏），跳过
        if (currentPage is Pages.MainPage or Pages.DesktopMainPage
            or Pages.NowPlayingPage or Pages.FullLyricsPage) return;

        // ContentPage 才有 Content 属性
        if (currentPage is not ContentPage contentPage) return;

        // 找到页面内容的根 Grid
        if (contentPage.Content is not Grid rootGrid) return;

        // 检查是否已添加 SafeAreaPaddingBehavior，避免重复添加
        foreach (var behavior in rootGrid.Behaviors)
        {
            if (behavior is SafeAreaPaddingBehavior)
                return; // 已存在，跳过
        }

        rootGrid.Behaviors.Add(new SafeAreaPaddingBehavior());
    }

    /// <summary>当前是否应处于横屏布局：手动强制横屏为 true，手动强制竖屏为 false，
    /// 否则按物理方向。Windows 桌面端始终为 true（桌面布局即横屏布局）。</summary>
    public static bool IsLandscapeMode()
    {
#if ANDROID
        if (Application.Current is App app)
        {
            if (app._manualLandscape) return true;
            if (app._manualPortrait) return false;
        }
        return DeviceDisplay.Current.MainDisplayInfo.Orientation == DisplayOrientation.Landscape;
#else
        return true;
#endif
    }

#if ANDROID
    /// <summary>正在强制切换到横屏的过渡标志（实际到达横屏后由 DisplayOrientationChanged 清除）。</summary>
    private bool _manualLandscape;
    /// <summary>正在强制切换到竖屏的过渡标志（实际到达竖屏后由 DisplayOrientationChanged 清除）。</summary>
    private bool _manualPortrait;
    /// <summary>当前是否由用户锁定在横屏模式（用于按钮状态判断与切换，不受物理旋转回调清除）。</summary>
    private bool _manualLandscapeLocked;

    /// <summary>强制进入横屏：锁定 SensorLandscape，延迟一帧切换 Shell 布局。</summary>
    public void ForceLandscape()
    {
        _manualLandscape = true;
        _manualPortrait = false;
        _manualLandscapeLocked = true;
        try
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity != null)
                activity.RequestedOrientation = Android.Content.PM.ScreenOrientation.SensorLandscape;
        }
        catch { }
        // 延迟到下一个消息循环，让当前按钮事件处理完毕后再切 Shell 根页面
        MainThread.BeginInvokeOnMainThread(() => ApplyOrientationLayout());
    }

    /// <summary>强制进入竖屏（返回键 / 再次点旋转按钮）：锁定 SensorPortrait，切回竖屏手机布局。</summary>
    public void ReleaseManualLandscape()
    {
        _manualLandscape = false;
        _manualPortrait = true;
        _manualLandscapeLocked = false;
        try
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity != null)
                activity.RequestedOrientation = Android.Content.PM.ScreenOrientation.SensorPortrait;
        }
        catch { }
        // 直接切回手机布局，不依赖 DisplayOrientation 是否已经变为 Portrait
        MainThread.BeginInvokeOnMainThread(() => ApplyOrientationLayout());
    }

    /// <summary>切换横竖屏（播放页旋转按钮）：横屏与竖屏切换。</summary>
    public void ToggleManualLandscape()
    {
        if (_manualLandscapeLocked) ReleaseManualLandscape();
        else ForceLandscape();
    }

    /// <summary>物理旋转回调：当设备实际到达强制方向后释放对应手动标志，之后按传感器方向切换布局。</summary>
    private void OnDisplayOrientationChanged(object? sender, DisplayInfoChangedEventArgs e)
    {
        var orientation = e.DisplayInfo.Orientation;
        Android.Util.Log.Info("CatClaw", $"[Orientation] DisplayChanged: {orientation}, manualLandscape={_manualLandscape}, manualPortrait={_manualPortrait}, locked={_manualLandscapeLocked}");
        if (_manualLandscape && orientation == DisplayOrientation.Landscape)
            _manualLandscape = false; // 已实际到达横屏，释放强制横屏
        else if (_manualPortrait && orientation == DisplayOrientation.Portrait)
            _manualPortrait = false; // 已实际到达竖屏，释放强制竖屏
        ApplyOrientationLayout();
    }

    /// <summary>按当前方向直选 Shell 根页面：横屏→DesktopMainPage（桌面侧栏），竖屏→MainPage（手机 Tab）。
    /// 触发源：MainDisplayOrientationChanged（物理旋转）、ForceLandscape/ReleaseManualLandscape（按钮）、
    /// OnResume（后台回前台兜底）、CreateWindow（首次启动）。幂等：已正确则跳过。
    /// 实现：Clear+Add Shell.Items（与 Windows CreateWindow 同模式），强制 Shell 重建渲染。</summary>
    public void ApplyOrientationLayout(Shell? shellOverride = null)
    {
        try
        {
            var shell = shellOverride ?? Shell.Current;
            if (shell == null) { Android.Util.Log.Warn("CatClaw", "[Orientation] shell==null, skip"); return; }

            bool landscape = IsLandscapeMode();

            // 幂等：检查当前根页面是否已正确
            var currentContent = shell.Items.SelectMany(i => i.Items).SelectMany(s => s.Items).FirstOrDefault()?.Content;
            if (landscape && currentContent is Pages.DesktopMainPage) return;
            if (!landscape && currentContent is Pages.MainPage) return;

            Android.Util.Log.Info("CatClaw", $"[Orientation] 切换布局 → {(landscape ? "DesktopMainPage" : "MainPage")}");

            ContentPage newPage = landscape
                ? MauiProgram.Services.GetRequiredService<Pages.DesktopMainPage>()
                : MauiProgram.Services.GetRequiredService<Pages.MainPage>();

            // 与 Windows CreateWindow 同模式：Clear+Add 强制 Shell 重建
            shell.Items.Clear();
            shell.Items.Add(new ShellContent { Content = newPage, Route = "main" });
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error("CatClaw", $"[Orientation] 布局切换失败: {ex}");
        }
    }
#endif

    /// <summary>用户是否已手动强制横屏（供 UI 按钮判断状态/图标）。
    /// Android 下由 _manualLandscapeLocked 控制；其他平台始终 false。</summary>
    public bool ManualLandscape =>
#if ANDROID
        _manualLandscapeLocked;
#else
        false;
#endif

    protected override Window CreateWindow(IActivationState? activationState)
    {
        StartupLog("CreateWindow: start");
        var shell = MauiProgram.Services.GetRequiredService<AppShell>();
        StartupLog("CreateWindow: AppShell resolved");
        shell.Navigated += OnShellNavigated;
        StartupLog("CreateWindow: creating Window");

#if WINDOWS
        // Windows: use desktop layout with sidebar
        var desktopPage = MauiProgram.Services.GetRequiredService<Pages.DesktopMainPage>();
        shell.Items.Clear();
        shell.Items.Add(new ShellContent { Content = desktopPage });

        var window = new Window(shell)
        {
            Width = 1200,
            Height = 800,
            MinimumWidth = 900,
            MinimumHeight = 600,
        };

        window.HandlerChanged += (s, e) =>
        {
            if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                CurrentAppWindow = appWindow;

                if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                {
                    presenter.SetBorderAndTitleBar(true, false);
                }

                // 通过 P/Invoke 移除 WS_CAPTION，彻底隐藏系统标题栏
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500);
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        RemoveSystemTitleBar(hwnd);
                        nativeWindow.SizeChanged += (_, _) => RemoveSystemTitleBar(hwnd);
                    });
                });
            }
        };
#else
        var window = new Window(shell);

#if ANDROID
        // 物理旋转 → 直切 Shell 布局（单一路径，无需 modal 推弹和多路对账）
        DeviceDisplay.MainDisplayInfoChanged -= OnDisplayOrientationChanged;
        DeviceDisplay.MainDisplayInfoChanged += OnDisplayOrientationChanged;
        // 首次启动按当前方向直选布局（Shell.Current 尚未就绪，传实例）
        ApplyOrientationLayout(shell);
#endif
#endif

        StartupLog("CreateWindow: Window created, returning");
        return window;
    }

#if WINDOWS
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private const int GWL_STYLE = -16;
    private const int WS_BORDER = 0x00800000;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_DLGFRAME = 0x00400000;
    private const int WS_THICKFRAME = 0x00040000;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private static void RemoveSystemTitleBar(IntPtr hwnd)
    {
        try
        {
            int style = GetWindowLong(hwnd, GWL_STYLE);
            // 移除标题栏相关样式，保留可调整大小的边框
            style &= ~(WS_CAPTION | WS_BORDER | WS_DLGFRAME);
            SetWindowLong(hwnd, GWL_STYLE, style);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }
        catch { }
    }
#endif
}
