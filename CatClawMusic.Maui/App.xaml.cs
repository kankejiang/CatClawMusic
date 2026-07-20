using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Services;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Extensions.DependencyInjection;

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

    /// <summary>应用进入后台时调用：flush 听歌时长，避免被系统杀死时丢失数据</summary>
    protected override void OnSleep()
    {
        base.OnSleep();
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

        // MainPage 自身已处理 SafeArea，跳过
        if (currentPage is Pages.MainPage) return;

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
