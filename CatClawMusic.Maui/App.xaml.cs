using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Helpers;
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
    public static Microsoft.UI.Xaml.Window? CurrentNativeWindow { get; set; }
    private static IntPtr _appHwnd;

    /// <summary>MAUI 官方 TitleBar 控件实例（Windows 自定义标题栏；主题切换时更新配色）</summary>
    private static Microsoft.Maui.Controls.TitleBar? _mauiTitleBar;
#endif

    /// <summary>
    /// 全局「应用从后台回到前台」事件。
    /// Application 基类只提供 OnResume 虚方法（子类覆写），没有对外可订阅的事件，
    /// 这里显式暴露出来，供播放页/歌词页等订阅，用来在锁屏解锁或切回前台时
    /// 校正歌词锚点与高亮位置（Activity/Handler 重建后旧锚点失效导致高亮漂移）。
    /// </summary>
    public static event EventHandler? Resumed;

    /// <summary>触发 Resumed 全局事件（只在 OnResume 里调用）。</summary>
    private static void RaiseResumed() => Resumed?.Invoke(null, EventArgs.Empty);

    public App()
    {
#if DEBUG
        // 首次异常诊断：定位被 try-catch 吞掉的异常（输出窗口搜 [FC]）
        AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
        {
            var ex = e.Exception;
            if (ex is InvalidOperationException or Microsoft.Maui.Controls.Xaml.XamlParseException)
            {
                System.Diagnostics.Debug.WriteLine($"[FC] {ex.GetType().Name}: {ex.Message}");
                if (ex is Microsoft.Maui.Controls.Xaml.XamlParseException xpe)
                    System.Diagnostics.Debug.WriteLine($"[FC] XAML-FULL: {xpe}");
                System.Diagnostics.Debug.WriteLine($"[FC] SRC: {ex.Source}");
                System.Diagnostics.Debug.WriteLine($"[FC] STACK: {ex.StackTrace}");
                if (ex.InnerException != null)
                    System.Diagnostics.Debug.WriteLine($"[FC] INNER: {ex.InnerException}");
            }
        };
        // 全局未捕获异常落盘：不挂调试器直接运行（Ctrl+F5 / 桌面点图标）时闪退，
        // 堆栈写入 crash.log（Android: /data/data/<pkg>/files/crash.log，可 adb pull），
        // 替代"必须挂调试器才能看到异常"。Debug/Release 都生效。
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try
            {
                var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UnhandledException\n{e.ExceptionObject}";
                File.WriteAllText(Path.Combine(FileSystem.AppDataDirectory, "crash.log"), text);
            }
            catch { }
        };
        // 后台 Task 异常统一标记已观察，避免吞掉的异常影响稳定（仅防二次触发）
        TaskScheduler.UnobservedTaskException += (_, e) => e.SetObserved();
#endif
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

        // 急切实例化 DownloadManager 单例：其构造函数会赋值 DownloadAgentBridge.EnqueueDownload。
        // 该桥接是 Agent 下载工具（kuwo_download/netease_download/download_file）的入口，
        // 若不解析单例（懒加载），Agent 会报"下载功能未初始化"。
        try
        {
            _ = MauiProgram.Services.GetRequiredService<Services.DownloadManager>();
            StartupLog("App.ctor: DownloadManager initialized");
        }
        catch (Exception ex) { StartupLog($"App.ctor: DownloadManager failed - {ex.Message}"); }

        // 初始化所有已启用的插件（fire-and-forget）
        _ = Task.Run(async () =>
        {
            try { await MauiProgram.Services.GetRequiredService<IPluginManager>().InitializeAllAsync(); }
            catch (Exception ex)
            {
                Log.Debug("App.xaml", $"[CatClaw] PluginManager init failed: {ex.Message}");
            }
            finally
            {
                // 无论成败都报告就绪：启动页不因插件异常而卡死
                MauiProgram.Services.GetService<StartupCoordinator>()?.MarkPluginsReady();
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
            catch (Exception ex) { Log.Debug("App", $"清理分享缓存失败: {ex.Message}"); }
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
            catch (Exception ex) { Log.Debug("App", $"恢复诊断日志失败: {ex.Message}"); }
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
            catch (Exception ex) { Log.Debug("App", $"显示诊断 Toast 失败: {ex.Message}"); }
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
        catch (Exception ex) { Log.Debug("App", $"显示诊断 Toast 失败: {ex.Message}"); }
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
        _ = ApplyOrientationLayout();
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

        // 触发全局 Resumed：让播放页/歌词页有机会校正锚点与高亮位置
        RaiseResumed();
    }

    private static void StartupLog(string msg)
    {
        Log.Debug("App.xaml", $"[STARTUP] {msg}");
        try
        {
            var logPath = Path.Combine(Path.GetTempPath(), "catclaw_startup.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] APP: {msg}\n");
        }
        catch (Exception ex) { Log.Debug("App", $"写入启动日志失败: {ex.Message}"); }
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
    /// <summary>关键服务（数据库/插件/FFmpeg）是否已就绪。
    /// 未就绪时 ApplyOrientationLayout 直接返回，让启动加载页等待，避免主界面提前构建
    /// 与服务初始化并发竞争造成启动卡顿。</summary>
    private bool _coreServicesReady;

    /// <summary>启动加载页最多等待时长：服务初始化异常未报告就绪时强制放行，避免无限卡在启动页。</summary>
    private static readonly TimeSpan StartupWaitTimeout = TimeSpan.FromSeconds(10);

    /// <summary>启动加载页最短展示时长：保证用户能看到启动页，也避免加载太快时主界面在
    /// 首帧渲染前就被替换（服务就绪太早会让启动页一帧都来不及显示）。</summary>
    private static readonly TimeSpan MinSplashDuration = TimeSpan.FromSeconds(1.2);

    /// <summary>
    /// 冷启动入口：先展示轻量启动加载页，等关键服务全部就绪（或超时兜底）后再按当前方向
    /// 构建主界面（MainPage/DesktopMainPage）。主界面构建（ViewPager2 + 5 个子页面）与
    /// 数据库/插件/FFmpeg 初始化错峰执行，消除「App 已能操作但仍卡顿」的冷启动窗口。
    /// </summary>
    /// <param name="shell">应用 Shell 实例</param>
    /// <param name="splash">启动加载页实例（用于感知其真正渲染上屏的时机）</param>
    private async Task EnterMainWhenReadyAsync(Shell shell, VisualElement splash)
    {
        try
        {
            var startup = MauiProgram.Services.GetService<StartupCoordinator>();
            if (startup != null)
                await Task.WhenAny(startup.AllReadyTask, Task.Delay(StartupWaitTimeout));
        }
        catch { }

        // 音乐库数据预加载：歌曲列表/协议/总览聚合这些重 IO 都在启动页展示期间完成，
        // 用户首次滑到音乐库 tab 时直接渲染已就绪数据，消除「进入音乐库卡顿」。
        try
        {
            await Task.WhenAny(PreloadLibraryDataAsync(), Task.Delay(StartupWaitTimeout));
        }
        catch { }

        // 等启动页真正渲染上屏（Loaded）后再计最短展示时长：
        // 若从 CreateWindow 起算，慢设备上首帧尚未渲染时预加载可能已完成，
        // 导致主界面在启动页亮相前就替换掉它，用户根本看不到启动页。
        var splashShown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? onLoaded = null;
        onLoaded = (_, _) =>
        {
            splash.Loaded -= onLoaded;
            splashShown.TrySetResult();
        };
        splash.Loaded += onLoaded;
        try
        {
            await Task.WhenAny(splashShown.Task, Task.Delay(TimeSpan.FromSeconds(3)));
        }
        catch { }

        // 启动页最短展示时长：预加载很快时也保证启动页可见、过渡不突兀
        await Task.Delay(MinSplashDuration);

        if (!_coreServicesReady)
        {
            _coreServicesReady = true;
            StartupLog("EnterMainWhenReadyAsync: ready, entering main UI");
            await ApplyOrientationLayout(shell);
        }
    }

    /// <summary>
    /// 启动页期间预加载音乐库数据：协议列表、当前 tab 歌曲列表、总览聚合，
    /// 并预热专辑/艺术家聚合缓存（与 LibraryPage.WarmExploreCaches 同一组操作），
    /// 把重 IO/重聚合的 CPU 开销从「首次进入音乐库时」提前到启动页展示期间。
    /// 完成后置位 LibraryViewModel.IsPreloaded，让页面首次出现时跳过重复加载。
    /// </summary>
    private static async Task PreloadLibraryDataAsync()
    {
        var libraryVm = MauiProgram.Services.GetService<ViewModels.LibraryViewModel>();
        if (libraryVm == null || libraryVm.IsPreloaded) return;
        try
        {
            await Task.WhenAll(
                libraryVm.RefreshProtocolsAsync(),
                libraryVm.CurrentTab == "Local" ? libraryVm.LoadLocalAsync() : libraryVm.LoadNetworkAsync(),
                libraryVm.LoadOverviewDataAsync());

            // 预热专辑/艺术家聚合 + 列表页 VM 静态缓存（命中缓存后二次调用近零成本）
            var explore = MauiProgram.Services.GetService<ExploreDataService>();
            if (explore != null)
            {
                await Task.WhenAll(explore.GetAllAlbumsAsync(), explore.GetAllArtistsAsync());
                var albumsVm = MauiProgram.Services.GetService<ViewModels.AlbumsViewModel>();
                var artistsVm = MauiProgram.Services.GetService<ViewModels.ArtistsViewModel>();
                var warmVmTasks = new List<Task>();
                if (albumsVm != null) warmVmTasks.Add(albumsVm.LoadAsync());
                if (artistsVm != null) warmVmTasks.Add(artistsVm.LoadAsync());
                if (warmVmTasks.Count > 0) await Task.WhenAll(warmVmTasks);
            }

            libraryVm.IsPreloaded = true;
        }
        catch (Exception ex)
        {
            Log.Debug("App.xaml", $"[Splash] 音乐库预加载失败（进入页面时按需加载兜底）: {ex.Message}");
        }
    }

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
        catch (Exception ex) { Log.Debug("App", $"强制横屏失败: {ex.Message}"); }
        // 延迟到下一个消息循环，让当前按钮事件处理完毕后再切 Shell 根页面
        MainThread.BeginInvokeOnMainThread(async () => await ApplyOrientationLayout());
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
        catch (Exception ex) { Log.Debug("App", $"恢复竖屏失败: {ex.Message}"); }
        // 直接切回手机布局，不依赖 DisplayOrientation 是否已经变为 Portrait
        MainThread.BeginInvokeOnMainThread(async () => await ApplyOrientationLayout());
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
        _ = ApplyOrientationLayout();
    }

    /// <summary>按当前方向直选 Shell 根页面：横屏→DesktopMainPage（桌面侧栏），竖屏→MainPage（手机 Tab）。
    /// 触发源：MainDisplayOrientationChanged（物理旋转）、ForceLandscape/ReleaseManualLandscape（按钮）、
    /// OnResume（后台回前台兜底）、CreateWindow（首次启动）。幂等：已正确则跳过。
    /// 实现：Clear+Add Shell.Items（与 Windows CreateWindow 同模式），强制 Shell 重建渲染。</summary>
    public async Task ApplyOrientationLayout(Shell? shellOverride = null)
    {
        try
        {
            // 关键服务未就绪时保持启动加载页，不提前构建主界面（见 EnterMainWhenReadyAsync）
            if (!_coreServicesReady) return;

            var shell = shellOverride ?? DesktopNavigation.TryGetShell();
            if (shell == null) { Android.Util.Log.Warn("CatClaw", "[Orientation] shell==null, skip"); return; }

            bool landscape = IsLandscapeMode();

            // 幂等：检查当前根页面是否已正确
            var currentContent = shell.Items.SelectMany(i => i.Items).SelectMany(s => s.Items).FirstOrDefault()?.Content;
            if (landscape && currentContent is Pages.DesktopMainPage) return;
            if (!landscape && currentContent is Pages.MainPage) return;

            Android.Util.Log.Info("CatClaw", $"[Orientation] 切换布局 → {(landscape ? "DesktopMainPage" : "MainPage")}");

            // 弹出导航栈中所有 PushAsync 推入的页面（横屏时推入的 NowPlayingPage / FullLyricsPage 等）。
            // 若不弹出，切换根页面后这些页面仍留在导航栈上，覆盖新的 MainPage，
            // 导致 ViewPager2 滑动手势和歌词按钮全部失效（竖屏→横屏→竖屏后全屏歌词进不去）。
            // 必须逐个 await PopAsync：PopAsync 是异步方法，不 await 时 NavigationStack.Count
            // 不会同步减少，导致 while 循环无限执行冻结 UI 线程。
            while (shell.Navigation.NavigationStack.Count > 1)
            {
                Android.Util.Log.Info("CatClaw", $"[Orientation] 弹出残留页面: {shell.Navigation.NavigationStack[^1]?.GetType().Name}");
                await shell.Navigation.PopAsync(false);
            }

            ContentPage newPage = landscape
                ? MauiProgram.Services.GetRequiredService<Pages.DesktopMainPage>()
                : MauiProgram.Services.GetRequiredService<Pages.MainPage>();

            // 与 Windows CreateWindow 同模式：Clear+Add 强制 Shell 重建
            shell.Items.Clear();
            shell.Items.Add(new ShellContent { Content = newPage, Route = "main" });

            // 横竖屏根切换可能丢失 UP/ScrollEnded 事件（旧视图树被销毁），
            // 显式复位触摸计数并广播交互结束，防止 IsUserInteracting 卡死
            // 导致歌词同步/桌面歌词永久冻结。
            try
            {
                var interaction = MauiProgram.Services.GetService<IInteractionStateService>();
                interaction?.ResetTouchState();
            }
            catch { }

            // 横竖屏根切换后：新 NowPlayingPage 的歌词高亮/进度状态需强制同步，
            // 否则 VM 歌词索引不变时新页面收不到 PropertyChanged，歌词永久冻结。
            // 延迟一帧等新页面挂载 + OnAppearing 完成后再广播。
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    await Task.Delay(250);
                    var npVm = MauiProgram.Services.GetRequiredService<NowPlayingViewModel>();
                    npVm.RefreshLyricDisplayAfterLayout();
                }
                catch { }
            });

#if ANDROID
            // 切换布局后主动请求重新应用 insets：横竖屏切换时系统栏可见性可能变化
            // （如横屏隐藏状态栏），必须重新派发 OnApplyWindowInsets 才能拿到新的 inset 值，
            // 否则 SafeAreaHelper.TopInset 会停留在旧值（如竖屏的 24dp），导致横屏内容被抬高。
            try
            {
                var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity as MainActivity;
                activity?.SetupEdgeToEdge();
            }
            catch (Exception ex) { Log.Debug("App", $"重新应用 insets 失败: {ex.Message}"); }
#endif
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
        // ═══ 桌面端重建设计：全新空白画布，从零开始逐步搭建 ═══
        // 无边框窗口：不走 Shell（ShellContent 会包一层容器，可能带默认留白产生顶部白条），
        // 直接 Window(Page) 让页面铺满窗口。
        // 启动大小：优先恢复用户上次拖拽保存的大小；无存档时按主显示器分辨率自适应
        // （21:9 及以上超宽屏更宽）。窗口可拖拽调整，SizeChanged 时保存。
        var desktopPage = MauiProgram.Services.GetRequiredService<Pages.DesktopBlankPage>();
        var window = new Window(desktopPage)
        {
            MinimumWidth = 900,
            MinimumHeight = 600,
            // 清空原生窗口标题文字（避免任务栏/Alt+Tab 显示 "CatClawMusic"）
            Title = "",
        };

        // 启动固定窗口分辨率 1600×900（不恢复上次保存的大小，保证每次启动一致），
        // 位置在主显示器工作区居中；小屏保护：工作区不足时按工作区尺寸收窄，避免窗口越界。
        var (workW, workH) = GetWorkAreaLogical();
        window.Width = Math.Min(1600, workW);
        window.Height = Math.Min(900, workH);
        window.X = Math.Max(0, (workW - window.Width) / 2);
        window.Y = Math.Max(0, (workH - window.Height) / 2);

        // MAUI 10 内容根默认按"有标题栏"预留 ~32px 占位（白色）→ 设不可见 TitleBar 让内容显示在标题栏区域
        window.TitleBar = new Microsoft.Maui.Controls.TitleBar { IsVisible = false };

        window.HandlerChanged += (s, e) =>
        {
            if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
            {
                CurrentNativeWindow = nativeWindow;
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                CurrentAppWindow = appWindow;
                _appHwnd = hwnd;
                try
                {
                    // ⓪ 启动固定分辨率 1600×900（物理像素 = 逻辑 × DPI）：平台层强制 MoveAndResize。
                    //    MAUI Window.Width/Height 在 Windows 上不保证生效（OpenWindow/Handler 时序问题），
                    //    必须用 AppWindow 物理像素设置才可靠；小屏保护：工作区不足时按工作区收窄，位置居中。
                    try
                    {
                        var scale = Win32.GetScaleAdjustment(nativeWindow);
                        var work = Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea;
                        var winW = Math.Min((int)(1600 * scale), work.Width);
                        var winH = Math.Min((int)(900 * scale), work.Height);
                        appWindow.MoveAndResize(new global::Windows.Graphics.RectInt32
                        {
                            X = work.X + (work.Width - winW) / 2,
                            Y = work.Y + (work.Height - winH) / 2,
                            Width = winW,
                            Height = winH,
                        });
                    }
                    catch { /* 尺寸设置失败不影响显示 */ }

                    // ① 内容延伸到标题栏区域（隐藏系统标题栏绘制）
                    appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
                    // ② 保留标题栏实体（原生拖拽前提）：不调用 SetBorderAndTitleBar(false,false)！
                    //    移除标题栏后 AppWindow.TitleBar.SetDragRectangles 会失效（官方约束）。
                    //    窗口能力全保留：可最大化/调整大小/最小化。
                    if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter overlappedPresenter)
                    {
                        overlappedPresenter.IsMaximizable = true;
                        overlappedPresenter.IsResizable = true;
                        overlappedPresenter.IsMinimizable = true;
                    }
                    // ③ 显式恢复 Windows 11 原生圆角（SetBorderAndTitleBar 移除后 DWM 默认直角）
                    int cornerRound = 2; // DWMWCP_ROUND
                    DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerRound,
                        (uint)System.Runtime.InteropServices.Marshal.SizeOf<int>());
                    // ④ 窗口根容器背景改蓝色（与页面 BackgroundColor 一致）：MAUI/WinUI 根默认白色 →
                    //    启动瞬间露出的 32px 标题栏占位也是蓝色，视觉无缝无白条闪烁
                    try
                    {
                        if (nativeWindow.Content is Microsoft.UI.Xaml.FrameworkElement rootFe)
                        {
                            SetRootBackgroundTheme(rootFe, 0);
                        }
                    }
                    catch { }
                }
                catch { }

                // ⑥ 窗口激活后（布局完成）：
                //    1) 官方 workaround（dotnet/maui #36040）：反射调用 MAUI 内部
                //       NavigationRootManager.SetTitleBarVisibility(false) —— 折叠 32px 标题栏宿主、
                //       清零 NavigationViewContentMargin、清除非客户区输入区域，一步到位；
                //    2) Dump 视觉树到 %TEMP%\catclaw_startup.log 定位残留白条；
                //    3) KillTopWhiteBar 兜底折叠 + 根容器刷蓝。
                global::Windows.Foundation.TypedEventHandler<object, Microsoft.UI.Xaml.WindowActivatedEventArgs>? firstActivated = null;
                firstActivated = (_, _) =>
                {
                    nativeWindow.Activated -= firstActivated;
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(100);
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            try
                            {
                                // 官方方案：调用 MAUI 内部 SetTitleBarVisibility(false)
                                InvokeMauiSetTitleBarVisibility(window);
                                WinWndLog("==== visual tree after activation ====");
                                DumpVisualTree(nativeWindow.Content, 0, 6);
                                KillTopWhiteBar(nativeWindow.Content);
                                if (nativeWindow.Content is Microsoft.UI.Xaml.FrameworkElement rootFe2)
                                    SetRootBackgroundTheme(rootFe2, 0);
                            }
                            catch { }
                        });
                    });
                };
                nativeWindow.Activated += firstActivated;
            }
        };
#else
        var window = new Window(shell);

#if ANDROID
        // 物理旋转 → 直切 Shell 布局（单一路径，无需 modal 推弹和多路对账）
        DeviceDisplay.MainDisplayInfoChanged -= OnDisplayOrientationChanged;
        DeviceDisplay.MainDisplayInfoChanged += OnDisplayOrientationChanged;

        // 先展示轻量启动加载页，等关键服务（数据库/插件/FFmpeg）就绪后再按方向构建主界面，
        // 避免 ViewPager2 + 5 子页面构建与服务初始化并发竞争主线程/IO 导致启动卡顿。
        StartupLog("CreateWindow: showing splash loading page");
        var splashPage = new Pages.SplashLoadingPage();
        shell.Items.Clear();
        shell.Items.Add(new ShellContent { Content = splashPage });
        _ = EnterMainWhenReadyAsync(shell, splashPage);
#endif
#endif

        StartupLog("CreateWindow: Window created, returning");

#if !ANDROID
        // 桌面平台（Windows 等）：主窗口创建完成后延迟恢复桌面歌词（上次开启过则自动显示悬浮歌词窗口）
        var dlManager = MauiProgram.Services.GetService<Services.DesktopLyricManager>();
        if (dlManager != null)
        {
            Dispatcher.Dispatch(() =>
            {
                try { _ = dlManager.RestoreAsync(); }
                catch (Exception ex)
                {
                    StartupLog($"Restore desktop lyric failed: {ex.Message}");
                }
            });
        }
#endif

        return window;
    }

#if WINDOWS
    /// <summary>
    /// 解析当前是否深色：优先用 ThemeService 的最终生效值（含"跟随系统"判定），
    /// 服务不可用时回退到 MAUI 的 RequestedTheme —— 绝不能盲目回退 true，
    /// 否则浅色模式启动时窗口 chrome 会被强制刷成深色。
    /// </summary>
    private static bool ResolveIsDark(IThemeService? themeService)
    {
        try
        {
            if (themeService != null) return themeService.IsEffectivelyDark();
        }
        catch { }
        return Application.Current?.RequestedTheme == Microsoft.Maui.ApplicationModel.AppTheme.Dark;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    // 主显示器工作区（不含任务栏）尺寸
    private const int SM_CXWORKAREA = 48;
    private const int SM_CYWORKAREA = 49;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    // Win32 原生无标题栏：直接移除窗口 WS_CAPTION 样式（比 MAUI TitleBar/SetBorderAndTitleBar 更彻底）
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    private const int GWL_STYLE = -16;
    private const long WS_CAPTION = 0x00C00000;      // 标题栏（WS_BORDER | WS_DLGFRAME）
    private const long WS_THICKFRAME = 0x00040000;   // 可调整大小边框（最大化/缩放依赖，保留）
    private const uint SWP_FRAMECHANGED = 0x0020;    // 样式变更后强制重绘非客户区
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    // DWM API for dark mode border
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint dwAttribute, ref int pvAttribute, uint cbAttribute);

    private const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const uint DWMWA_BORDER_COLOR = 34;
    private const uint DWMWA_CAPTION_COLOR = 35;
    private const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33; // 值：0=默认 1=不圆角 2=圆角(ROUND)

    /// <summary>主显示器工作区（不含任务栏）尺寸，换算为逻辑像素（物理像素 ÷ DPI 缩放）。</summary>
    private static (double W, double H) GetWorkAreaLogical()
    {
        var workW = GetSystemMetrics(SM_CXWORKAREA);
        var workH = GetSystemMetrics(SM_CYWORKAREA);
        double density = 1.0;
        try { density = Microsoft.Maui.Devices.DeviceDisplay.Current.MainDisplayInfo.Density; }
        catch { }
        return (workW > 0 ? workW / density : 1200, workH > 0 ? workH / density : 800);
    }

    /// <summary>
    /// 无存档时的默认窗口大小（逻辑像素）：按主显示器工作区自适应。
    /// 21:9(≈2.33)/32:9(≈3.56) 超宽屏 → 宽度占比 85%（更宽），标准屏 65%；高度 ≤ 900。
    /// </summary>
    private static (double W, double H) ComputeDefaultWindowSize()
    {
        var (workW, workH) = GetWorkAreaLogical();
        if (workW <= 0 || workH <= 0) return (1200, 800);
        var aspect = workW / workH;
        var ultraWide = aspect >= 2.1; // 16:9≈1.78 < 2.1 < 21:9≈2.33
        var winW = (int)(workW * (ultraWide ? 0.85 : 0.65));
        var winH = Math.Min((int)(workH * 0.78), 900);
        return (winW, winH);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    /// <summary>
    /// 设置 WinUI 窗口根元素的背景色，并清除默认 Margin，
    /// 确保 MAUI 内容能延伸到窗口边缘而不破坏子元素的 UI 样式。
    /// </summary>
    private static void SetRootWindowBackground(Microsoft.UI.Xaml.DependencyObject? element, Microsoft.UI.Xaml.Media.Brush bgBrush)
    {
        if (element == null) return;

        try
        {
            // 清除根元素的 Margin
            if (element is Microsoft.UI.Xaml.FrameworkElement rootFe)
            {
                rootFe.Margin = new Microsoft.UI.Xaml.Thickness(0);
            }

            // 设置背景色（根面板/控件）
            if (element is Microsoft.UI.Xaml.Controls.Panel panel)
            {
                panel.Background = bgBrush;
            }
            else if (element is Microsoft.UI.Xaml.Controls.Control control)
            {
                control.Background = bgBrush;
            }
            else if (element is Microsoft.UI.Xaml.Controls.Border border)
            {
                border.Background = bgBrush;
            }
        }
        catch (Exception ex)
        {
            Log.Debug("App", $"SetRootWindowBackground error: {ex.Message}");
        }
    }

    /// <summary>
    /// 窗口根容器背景递归刷为当前主题背景色（浅色 #F8F7FF / 深色 #080B1A，与 UpdateWindowsTheme 一致）：
    /// MAUI/WinUI 内容根默认白色 → 启动瞬间标题栏占位与页面背景同色，视觉无缝无白条闪烁。
    /// </summary>
    private static void SetRootBackgroundTheme(Microsoft.UI.Xaml.DependencyObject? element, int depth = 0)
    {
        bool isDark = Application.Current?.RequestedTheme == Microsoft.Maui.ApplicationModel.AppTheme.Dark;
        var color = isDark
            ? global::Windows.UI.Color.FromArgb(0xFF, 0x08, 0x0B, 0x1A)
            : global::Windows.UI.Color.FromArgb(0xFF, 0xF8, 0xF7, 0xFF);
        SetRootBackgroundColor(element, color, depth);
    }

    /// <summary>
    /// 窗口根容器背景递归改色：覆盖 Panel/Border/ContentControl/ContentPresenter。
    /// </summary>
    private static void SetRootBackgroundColor(Microsoft.UI.Xaml.DependencyObject? element, global::Windows.UI.Color color, int depth = 0)
    {
        if (element == null || depth > 6) return;
        try
        {
            var brush = new Microsoft.UI.Xaml.Media.SolidColorBrush(color);
            if (element is Microsoft.UI.Xaml.Controls.Panel panel)
            {
                panel.Background = brush;
            }
            else if (element is Microsoft.UI.Xaml.Controls.Border border)
            {
                border.Background = brush;
            }
            else if (element is Microsoft.UI.Xaml.Controls.ContentControl cc)
            {
                // MAUI 标题栏宿主（32px ContentControl）也刷同色
                cc.Background = brush;
            }
            else if (element is Microsoft.UI.Xaml.Controls.ContentPresenter cp)
            {
                cp.Background = brush;
            }
            // 递归子元素
            var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < count; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element, i);
                if (child is Microsoft.UI.Xaml.Controls.Panel ||
                    child is Microsoft.UI.Xaml.Controls.Border ||
                    child is Microsoft.UI.Xaml.Controls.ContentControl ||
                    child is Microsoft.UI.Xaml.Controls.ContentPresenter)
                {
                    SetRootBackgroundColor(child, color, depth + 1);
                }
            }
        }
        catch { /* 刷色失败不影响显示 */ }
    }

    /// <summary>
    /// 窗口根容器背景递归改透明（旧方案保留备用）。
    /// </summary>
    private static void SetRootBackgroundTransparent(Microsoft.UI.Xaml.DependencyObject? element, int depth = 0)
    {
        SetRootBackgroundColor(element, global::Windows.UI.Color.FromArgb(0, 0, 0, 0), depth);
    }

    /// <summary>
    /// 清除容器元素 Padding（WinUI Panel 基类无 Padding 属性，需按具体类型设置）。
    /// </summary>
    private static void ClearContainerPadding(Microsoft.UI.Xaml.DependencyObject element)
    {
        switch (element)
        {
            case Microsoft.UI.Xaml.Controls.Grid grid:
                grid.Padding = new Microsoft.UI.Xaml.Thickness(0);
                break;
            case Microsoft.UI.Xaml.Controls.StackPanel stack:
                stack.Padding = new Microsoft.UI.Xaml.Thickness(0);
                break;
            case Microsoft.UI.Xaml.Controls.RelativePanel rel:
                rel.Padding = new Microsoft.UI.Xaml.Thickness(0);
                break;
        }
    }

    /// <summary>
    /// 官方 workaround（dotnet/maui issue #36040，已验证）：反射调用 MAUI 内部
    /// NavigationRootManager.SetTitleBarVisibility(false)——一次性折叠 32px 标题栏宿主容器、
    /// 清零 NavigationViewContentMargin、清除非客户区输入区域，根治顶部白条。
    /// 该类型为 internal，只能反射访问。
    /// </summary>
    private static void InvokeMauiSetTitleBarVisibility(Microsoft.Maui.Controls.Window mauiWindow)
    {
        try
        {
            var mauiContext = mauiWindow.Handler?.MauiContext;
            if (mauiContext == null) return;

            var navManager = mauiContext.Services.GetService(
                typeof(Microsoft.Maui.Platform.NavigationRootManager));
            if (navManager == null)
            {
                // 若 DI 未注册，尝试从平台根视图容器反查（WindowRootViewContainer → RootNavigationManager）
                navManager = FindNavigationRootManagerFromWindow(mauiWindow);
            }
            if (navManager == null) return;

            var method = navManager.GetType().GetMethod("SetTitleBarVisibility",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            if (method == null) return;
            method.Invoke(navManager, new object[] { false });
            WinWndLog("InvokeMauiSetTitleBarVisibility(false) OK");
        }
        catch (Exception ex)
        {
            WinWndLog($"InvokeMauiSetTitleBarVisibility failed: {ex.Message}");
        }
    }

    /// <summary>通过原生窗口内容树找 NavigationRootManager（internal，用平台视图的私有字段反查）。</summary>
    private static object? FindNavigationRootManagerFromWindow(Microsoft.Maui.Controls.Window mauiWindow)
    {
        try
        {
            var nativeWindow = mauiWindow.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
            if (nativeWindow == null) return null;

            // 遍历视觉树找 WindowRootView（Microsoft.Maui.Platform.WindowRootView），
            // 其 DataContext/内部字段持有 NavigationRootManager
            var root = nativeWindow.Content;
            var windowRootView = FindVisualNode(root, "WindowRootView");
            if (windowRootView == null) return null;

            // 反射读取 _navigationRootManager 字段（MAUI 内部字段名，见 GetNavigationRootManager）
            var field = windowRootView.GetType().GetField("_navigationRootManager",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return field?.GetValue(windowRootView);
        }
        catch { return null; }
    }

    private static Microsoft.UI.Xaml.DependencyObject? FindVisualNode(Microsoft.UI.Xaml.DependencyObject? el, string typeName, int depth = 0)
    {
        if (el == null || depth > 8) return null;
        if (el.GetType().Name == typeName) return el;
        try
        {
            var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(el);
            for (int i = 0; i < count; i++)
            {
                var hit = FindVisualNode(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(el, i), typeName, depth + 1);
                if (hit != null) return hit;
            }
        }
        catch { }
        return null;
    }

    /// <summary>输出窗口原生内容树（类型/行号/高度/可见性/背景色），用于定位顶部白条来源。</summary>
    private static void DumpVisualTree(Microsoft.UI.Xaml.DependencyObject? el, int depth, int maxDepth)
    {
        if (el == null || depth > maxDepth) return;
        try
        {
            var indent = new string(' ', depth * 2);
            var extra = "";
            if (el is Microsoft.UI.Xaml.FrameworkElement fe)
                extra += $" h={fe.ActualHeight:F0} vis={fe.Visibility}";
            if (el is Microsoft.UI.Xaml.Controls.Panel panel && panel.Background is Microsoft.UI.Xaml.Media.SolidColorBrush pb)
                extra += $" bg=#{pb.Color.A:X2}{pb.Color.R:X2}{pb.Color.G:X2}{pb.Color.B:X2}";
            if (el is Microsoft.UI.Xaml.Controls.Border border && border.Background is Microsoft.UI.Xaml.Media.SolidColorBrush bb)
                extra += $" bg=#{bb.Color.A:X2}{bb.Color.R:X2}{bb.Color.G:X2}{bb.Color.B:X2}";
            if (el is Microsoft.UI.Xaml.Controls.ContentPresenter cp && cp.Content != null)
                extra += $" content={cp.Content.GetType().Name}";
            WinWndLog($"{indent}{el.GetType().Name}{extra}");

            if (el is Microsoft.UI.Xaml.Controls.Grid grid)
            {
                foreach (var child in grid.Children)
                {
                    if (child is Microsoft.UI.Xaml.FrameworkElement cfe)
                        WinWndLog($"{indent}  row={Microsoft.UI.Xaml.Controls.Grid.GetRow(cfe)} {cfe.GetType().Name} h={cfe.ActualHeight:F0} vis={cfe.Visibility}");
                }
            }

            var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(el);
            for (int i = 0; i < count; i++)
                DumpVisualTree(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(el, i), depth + 1, maxDepth);
        }
        catch { }
    }

    /// <summary>消除顶部白条：折叠 MAUI 标题栏宿主（叠在内容上方的 ~32px ContentControl），
    /// 不触碰内容行，让蓝色画布 100% 铺满窗口。</summary>
    private static void KillTopWhiteBar(Microsoft.UI.Xaml.DependencyObject? el, int depth = 0)
    {
        if (el == null || depth > 5) return;
        try
        {
            if (el is Microsoft.UI.Xaml.Controls.ContentControl cc
                && cc.ActualHeight > 12 && cc.ActualHeight <= 48)
            {
                cc.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                WinWndLog($"KillTopWhiteBar: collapsed TitleBar host {el.GetType().Name} h={cc.ActualHeight:F0}");
            }
        }
        catch { }

        var n = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(el);
        for (int i = 0; i < n; i++)
            KillTopWhiteBar(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(el, i), depth + 1);
    }

    private static void WinWndLog(string msg)
    {
        try
        {
            File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "catclaw_startup.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] WINWND: {msg}\n");
        }
        catch { }
    }

    /// <summary>
    /// 深度设置窗口背景：遍历前 10 层容器，设置所有 Panel/Border/Control 的背景色，
    /// 清除 Margin 与 Padding，但不深入到具体 UI 控件内部（避免破坏样式）。
    /// </summary>
    private static void SetRootWindowBackgroundDeep(Microsoft.UI.Xaml.DependencyObject? element, Microsoft.UI.Xaml.Media.Brush bgBrush, int depth = 0)
    {
        if (element == null || depth > 10) return;

        try
        {
            // 清除 Margin
            if (element is Microsoft.UI.Xaml.FrameworkElement fe)
            {
                fe.Margin = new Microsoft.UI.Xaml.Thickness(0);
            }

            // 设置背景色
            if (element is Microsoft.UI.Xaml.Controls.Panel panel)
            {
                panel.Background = bgBrush;
                // Panel 具体类型的 Padding 也要清（MAUI 窗口根 Grid 可能带标题栏预留 Padding）
                ClearContainerPadding(panel);
                // 递归处理子元素（仅限容器层）
                foreach (var child in panel.Children)
                {
                    // 只深入处理明显是容器的元素
                    if (child is Microsoft.UI.Xaml.Controls.Panel ||
                        child is Microsoft.UI.Xaml.Controls.Border ||
                        child is Microsoft.UI.Xaml.Controls.ContentControl ||
                        child is Microsoft.UI.Xaml.Controls.ContentPresenter ||
                        child is Microsoft.UI.Xaml.Controls.ScrollViewer)
                    {
                        SetRootWindowBackgroundDeep(child, bgBrush, depth + 1);
                    }
                }
            }
            else if (element is Microsoft.UI.Xaml.Controls.Border border)
            {
                border.Background = bgBrush;
                border.Padding = new Microsoft.UI.Xaml.Thickness(0);
                if (border.Child is Microsoft.UI.Xaml.DependencyObject borderChild)
                {
                    SetRootWindowBackgroundDeep(borderChild, bgBrush, depth + 1);
                }
            }
            else if (element is Microsoft.UI.Xaml.Controls.ContentControl contentControl)
            {
                contentControl.Background = bgBrush;
                contentControl.Padding = new Microsoft.UI.Xaml.Thickness(0);
                if (contentControl.Content is Microsoft.UI.Xaml.DependencyObject contentChild)
                {
                    SetRootWindowBackgroundDeep(contentChild, bgBrush, depth + 1);
                }
            }
            else if (element is Microsoft.UI.Xaml.Controls.ContentPresenter contentPresenter)
            {
                contentPresenter.Padding = new Microsoft.UI.Xaml.Thickness(0);
                if (contentPresenter.Content is Microsoft.UI.Xaml.DependencyObject cpChild)
                {
                    SetRootWindowBackgroundDeep(cpChild, bgBrush, depth + 1);
                }
            }
            else if (element is Microsoft.UI.Xaml.Controls.ScrollViewer scrollViewer)
            {
                scrollViewer.Background = bgBrush;
                scrollViewer.Padding = new Microsoft.UI.Xaml.Thickness(0);
                scrollViewer.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
                if (scrollViewer.Content is Microsoft.UI.Xaml.DependencyObject svChild)
                {
                    SetRootWindowBackgroundDeep(svChild, bgBrush, depth + 1);
                }
            }
            else if (element is Microsoft.UI.Xaml.Controls.Control control)
            {
                control.Background = bgBrush;
                control.Padding = new Microsoft.UI.Xaml.Thickness(0);
                control.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            }
        }
        catch (Exception ex)
        {
            Log.Debug("App", $"SetRootWindowBackgroundDeep error at depth {depth}: {ex.Message}");
        }
    }

    /// <summary>
    /// 构建极简自定义标题栏（MAUI 10 官方 TitleBar 控件，仅 Windows；文档：
    /// learn.microsoft.com/dotnet/maui/user-interface/controls/titlebar）。
    /// 不设 LeadingContent/Content/TrailingContent → 标题栏仅显示右上系统 caption 按钮，
    /// 左侧被贯穿的侧边栏覆盖（DesktopMainPage SidebarBorder Row0 RowSpan3）。
    /// 深浅色配色由 UpdateWindowsTheme 统一更新。
    /// </summary>
    private static void SetupWindowTitleBar(Microsoft.Maui.Controls.Window window)
    {
#if WINDOWS
        try
        {
            var themeService = MauiProgram.Services.GetService<IThemeService>();
            var isDark = ResolveIsDark(themeService);

            var titleBar = new Microsoft.Maui.Controls.TitleBar
            {
                HeightRequest = 44,
                Title = "",
                BackgroundColor = Colors.Transparent,
                ForegroundColor = isDark ? Color.FromArgb("#EEF0FB") : Color.FromArgb("#1A1F3A")
            };
            window.TitleBar = titleBar;
            _mauiTitleBar = titleBar;
        }
        catch (Exception ex)
        {
            Log.Debug("App", $"SetupWindowTitleBar failed: {ex.Message}");
        }
#endif
    }

    /// <summary>
    /// Windows 专属：更新 DWM 标题栏/边框颜色、标题栏按钮颜色及窗口根背景，
    /// 使其跟随应用的深/浅主题切换。在主题变化时由 ThemeService 调用。
    /// </summary>
    public static void UpdateWindowsTheme(bool isDark)
    {
#if WINDOWS
        try
        {
            if (_appHwnd == IntPtr.Zero || CurrentAppWindow == null) return;

            // 1. DWM 深色模式开关
            int darkMode = isDark ? 1 : 0;
            DwmSetWindowAttribute(_appHwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode,
                (uint)System.Runtime.InteropServices.Marshal.SizeOf<int>());

            // 2. DWM 边框和标题栏颜色（ABGR 格式：0xAABBGGRR）
            // 沉浸式（ExtendsContentIntoTitleBar=true）下，caption 已被内容覆盖，
            // 不能再给 DWM 边框/标题栏设固定色——否则会在窗口最外圈那一圈像素出现
            // 与内容色不一致的"白边/深边"色差。应设 CLR_NONE（-1 / 0xFFFFFFFF）让 DWM
            // 完全不绘制边框色，由 MAUI 内容自然贴边铺满。
            int colorRef = isDark ? unchecked((int)0xFFFFFFFF) : unchecked((int)0xFFFFFFFF); // CLR_NONE：深浅色下都不画边框
            DwmSetWindowAttribute(_appHwnd, DWMWA_BORDER_COLOR, ref colorRef,
                (uint)System.Runtime.InteropServices.Marshal.SizeOf<int>());
            DwmSetWindowAttribute(_appHwnd, DWMWA_CAPTION_COLOR, ref colorRef,
                (uint)System.Runtime.InteropServices.Marshal.SizeOf<int>());

            // 3. 标题栏按钮颜色（前景 + 背景）
            var titleBar = CurrentAppWindow.TitleBar;
            var transparent = global::Windows.UI.Color.FromArgb(0x00, 0x00, 0x00, 0x00);

            titleBar.BackgroundColor = transparent;
            titleBar.InactiveBackgroundColor = transparent;
            titleBar.ButtonBackgroundColor = transparent;
            titleBar.ButtonInactiveBackgroundColor = transparent;

            if (isDark)
            {
                titleBar.ButtonForegroundColor = global::Windows.UI.Color.FromArgb(0xFF, 0x8D, 0x93, 0xB7);
                titleBar.ButtonHoverForegroundColor = global::Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
                titleBar.ButtonPressedForegroundColor = global::Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
                titleBar.ButtonInactiveForegroundColor = global::Windows.UI.Color.FromArgb(0xFF, 0x5E, 0x67, 0x88);
                titleBar.ForegroundColor = global::Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
                titleBar.InactiveForegroundColor = global::Windows.UI.Color.FromArgb(0xFF, 0x8D, 0x93, 0xB7);
                titleBar.ButtonHoverBackgroundColor = global::Windows.UI.Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF);
                titleBar.ButtonPressedBackgroundColor = global::Windows.UI.Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF);
            }
            else
            {
                titleBar.ButtonForegroundColor = global::Windows.UI.Color.FromArgb(0xFF, 0x4A, 0x52, 0x78);
                titleBar.ButtonHoverForegroundColor = global::Windows.UI.Color.FromArgb(0xFF, 0x1A, 0x1F, 0x3A);
                titleBar.ButtonPressedForegroundColor = global::Windows.UI.Color.FromArgb(0xFF, 0x1A, 0x1F, 0x3A);
                titleBar.ButtonInactiveForegroundColor = global::Windows.UI.Color.FromArgb(0xFF, 0x9A, 0xA0, 0xB4);
                titleBar.ForegroundColor = global::Windows.UI.Color.FromArgb(0xFF, 0x1A, 0x1F, 0x3A);
                titleBar.InactiveForegroundColor = global::Windows.UI.Color.FromArgb(0xFF, 0x6B, 0x73, 0x99);
                titleBar.ButtonHoverBackgroundColor = global::Windows.UI.Color.FromArgb(0x1A, 0x00, 0x00, 0x00);
                titleBar.ButtonPressedBackgroundColor = global::Windows.UI.Color.FromArgb(0x30, 0x00, 0x00, 0x00);
            }

            // 4. 窗口根背景色
            // 桌面重建阶段：页面自己管背景（Debug 蓝色画布），窗口层不再递归刷主题色——
            // 否则会把页面里的蓝色 Grid 覆盖成白色/深色（重建结束后再启用）。
            if (!_desktopReconstruction)
            {
                var bgColor = isDark
                    ? global::Windows.UI.Color.FromArgb(0xFF, 0x08, 0x0B, 0x1A)
                    : global::Windows.UI.Color.FromArgb(0xFF, 0xF8, 0xF7, 0xFF);
                var bgBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(bgColor);

                if (CurrentNativeWindow?.Content != null)
                {
                    SetRootWindowBackground(CurrentNativeWindow.Content, bgBrush);
                    SetRootWindowBackgroundDeep(CurrentNativeWindow.Content, bgBrush);
                }
            }

            // 5. MAUI TitleBar 控件配色跟随主题（仅 caption 按钮前景色，背景已透明）
            if (_mauiTitleBar != null)
            {
                _mauiTitleBar.ForegroundColor = isDark
                    ? Microsoft.Maui.Graphics.Color.FromArgb("#EEF0FB")
                    : Microsoft.Maui.Graphics.Color.FromArgb("#1A1F3A");
            }
        }
        catch (Exception ex)
        {
            Log.Debug("App", $"UpdateWindowsTheme failed: {ex.Message}");
        }
#endif
    }

    private static bool _chromeSubscribed; // 防止重复订阅 RequestedThemeChanged

    /// <summary>桌面端重建阶段标志：页面自管背景（Debug 蓝色画布），窗口层不递归刷主题色</summary>
    private static readonly bool _desktopReconstruction = true;

    /// <summary>
    /// Windows 专属：测量系统任务栏（底部 dock 栏）高度，并在窗口延伸到任务栏下方或最大化时，
    /// 为底部 UI（播放栏等）预留安全区内边距，避免被任务栏遮挡。
    /// 通过系统 API（MonitorFromWindow + GetMonitorInfo + GetDpiForWindow）查询当前窗口所在显示器的
    /// 「屏幕全屏矩形」与「工作区矩形」，二者底部之差即任务栏在底部占用的高度（effective px == MAUI dp）。
    /// </summary>
    private static void UpdateWindowsSafeArea(Microsoft.UI.Windowing.AppWindow appWindow)
    {
        try
        {
            if (_appHwnd == IntPtr.Zero) return;

            // 取得窗口所在显示器的全屏矩形与工作区（不含任务栏）矩形，均为物理像素
            var hmonitor = MonitorFromWindow(_appHwnd, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(hmonitor, ref mi)) return;

            // 物理像素 → effective px（= MAUI dp），按窗口 DPI 换算
            int dpi = (int)GetDpiForWindow(_appHwnd);
            if (dpi <= 0) dpi = 96;
            double scale = 96.0 / dpi;

            // 工作区底部（不含任务栏）与屏幕全屏底部（含任务栏）
            double workBottomDp = mi.rcWork.bottom * scale;
            double screenBottomDp = mi.rcMonitor.bottom * scale;

            // 任务栏在底部占用的高度（dp）—— 即「测量 dock 栏高度」
            double dockHeight = Math.Max(0, screenBottomDp - workBottomDp);

            // 窗口底部相对工作区底部的溢出量：窗口实际延伸到任务栏下方时为 > 0
            double winBottomDp = appWindow.Position.Y + appWindow.Size.Height;
            double overlap = Math.Max(0, winBottomDp - workBottomDp);

            bool maximized = appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter
                && presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized;

            // 最大化时始终预留任务栏高度（即使窗口被约束在工作区内，也避免自动隐藏的任务栏弹出时遮挡）；
            // 非最大化时仅在实际溢出（延伸到任务栏下方）才预留，避免无谓留白。
            double bottomInset = maximized ? dockHeight : Math.Min(overlap, dockHeight);

            // TopInset 在 Windows 桌面端恒为 0（窗口带边框，不延伸到顶部系统区）
            SafeAreaHelper.UpdateInsets(SafeAreaHelper.TopInset, bottomInset);
        }
        catch (Exception ex) { Log.Debug("App", $"更新 Windows 安全区失败: {ex.Message}"); }
    }
#endif
}
