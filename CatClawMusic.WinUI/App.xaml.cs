using CatClawMusic.Core.Interfaces;
using CatClawMusic.Data;
using CatClawMusic.WinUI.Infrastructure;
using CatClawMusic.WinUI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace CatClawMusic.WinUI;

/// <summary>
/// 应用入口：组合根在 <see cref="ServiceRegistration"/>，这里只负责日志、全局异常与主窗口。
/// </summary>
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private Window? _window;

    public App()
    {
        InitializeComponent();

        UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        Services = ServiceRegistration.Build();

        // 先实例化日志服务：构造时把自己注册为 Core 的静态 Log 提供者，
        // 之后 Core/Data 的日志（含插件加载）都能落盘。
        _ = Services.GetRequiredService<ILogService>();
        Log.Info("app", "WinUI 启动");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();

        _ = InitializeDatabaseAsync();
    }

    private static async Task InitializeDatabaseAsync()
    {
        try
        {
            var db = Services.GetRequiredService<MusicDatabase>();
            await db.EnsureInitializedAsync();
            Log.Info("db", $"数据库就绪：{AppPaths.DatabasePath}");

            // 恢复上次的队列与当前曲（不自动开播）。
            await Services.GetRequiredService<PlaybackService>().RestoreAsync();

            // 插件初始化可能联网，放后台跑，不阻塞首屏。
            _ = Services.GetRequiredService<PluginService>().InitializeAsync();
        }
        catch (Exception ex)
        {
            Log.Error("db", $"数据库初始化失败：{ex}");
        }
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.Error("app", $"未处理异常：{e.Exception}");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error("app", $"未观察的任务异常：{e.Exception}");
    }
}
