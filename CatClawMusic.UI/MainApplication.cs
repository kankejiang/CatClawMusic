using Android.App;
using Android.Content;
using Android.Runtime;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.UI.Adapters;
using CatClawMusic.UI.Fragments;
using CatClawMusic.UI.Helpers;
using CatClawMusic.UI.Platforms.Android;
using CatClawMusic.UI.Services;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI;

/// <summary>Android Application 入口，配置依赖注入容器并初始化所有服务</summary>
[Application(Theme = "@style/CatClawTheme")]
public class MainApplication : Application
{
    /// <summary>全局依赖注入服务提供器</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    public MainApplication(IntPtr handle, JniHandleOwnership transfer) : base(handle, transfer) { }

    /// <summary>应用启动时注册所有服务到 DI 容器，初始化数据库、播放器、歌词服务和插件管理器</summary>
    public override void OnCreate()
    {
        base.OnCreate();

        var services = new ServiceCollection();

        // Database（v5：迁移到 ExternalFilesDir 规范路径）
        var externalDir = global::Android.App.Application.Context.GetExternalFilesDir(null)!.AbsolutePath;
        string dbPath = Path.Combine(externalDir, "catclaw.db");
        string oldDbPath = Path.Combine(CacheDir!.AbsolutePath, "catclaw.db");

        if (!File.Exists(Path.Combine(externalDir, "catclaw_v5.marker")))
        {
            if (File.Exists(oldDbPath))
            {
                try { File.Copy(oldDbPath, dbPath, overwrite: true); } catch { }
                try { File.Delete(oldDbPath); } catch { }
                Android.Util.Log.Info("CatClaw", $"Database migrated: {oldDbPath} -> {dbPath}");
            }
            File.WriteAllText(Path.Combine(externalDir, "catclaw_v5.marker"), "1");
        }
        var database = new MusicDatabase(dbPath);
        services.AddSingleton(database);

        // Core services
        services.AddSingleton<ISubsonicService, SubsonicService>();
        services.AddSingleton<INetworkFileService, WebDavService>();
        services.AddSingleton<INetworkMusicService, NetworkMusicService>();
        services.AddSingleton<IAudioPlayerService, AudioPlayerService>();
        services.AddSingleton<ILyricsService, LyricsService>();
        services.AddSingleton<IPluginManager>(sp =>
        {
            var allPlugins = sp.GetServices<IPlugin>();
            var prefs = global::Android.App.Application.Context.GetSharedPreferences(
                "catclaw_plugins", global::Android.Content.FileCreationMode.Private);
            var externalFilesDir = global::Android.App.Application.Context.GetExternalFilesDir(null)!.AbsolutePath;
            var pluginsDir = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(externalFilesDir)!, "Plugin");
            return new PluginManager(
                allPlugins,
                typeId => prefs.GetBoolean($"plugin_enabled_{typeId}", true),
                (typeId, enabled) => prefs.Edit().PutBoolean($"plugin_enabled_{typeId}", enabled).Apply(),
                pluginsDir
            );
        });
        LyricsService.ContentUriReader = async uri =>
        {
            try
            {
                using var stream = global::Android.App.Application.Context.ContentResolver!.OpenInputStream(global::Android.Net.Uri.Parse(uri)!);
                if (stream == null) return null;
                using var reader = new System.IO.StreamReader(stream);
                return await reader.ReadToEndAsync();
            }
            catch { return null; }
        };
        services.AddSingleton<IMusicLibraryService, MusicLibraryService>();
        services.AddSingleton<IPermissionService, PermissionService>();
        services.AddSingleton<PlayQueue>();

        // Android platform services
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IMainThreadDispatcher, MainThreadDispatcher>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<ILogService, LogService>();

        // ViewModels
        services.AddSingleton<LibraryViewModel>();       // 单例——Fragment 重建时不丢缓存
        services.AddSingleton<NowPlayingViewModel>();    // 单例——迷你播放器和全屏播放器共享状态
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SearchViewModel>();
        services.AddTransient<PlaylistViewModel>();
        services.AddTransient<WebDavSettingsViewModel>();
        services.AddTransient<NavidromeSettingsViewModel>();
        services.AddTransient<PlaylistDetailViewModel>();

        // Fragments (transient)
        services.AddTransient<FullLyricsFragment>();
        services.AddTransient<LibraryFragment>();
        services.AddTransient<NowPlayingFragment>();
        services.AddTransient<PlaylistFragment>();
        services.AddTransient<SearchFragment>();
        services.AddTransient<SettingsFragment>();
        services.AddTransient<PlaylistDetailFragment>();
        services.AddTransient<RemoteMusicFragment>();
        services.AddTransient<WebDavSettingsFragment>();
        services.AddTransient<NavidromeSettingsFragment>();
        services.AddTransient<MusicFolderSettingsFragment>();
        services.AddTransient<GeneralSettingsFragment>();
        services.AddTransient<DesktopLyricFragment>();
        services.AddTransient<PluginManagementFragment>();

        // Adapters
        services.AddTransient<SongAdapter>();
        services.AddTransient<PlaylistAdapter>();
        services.AddTransient<UpcomingSongAdapter>();

        Services = services.BuildServiceProvider();

        // 设置 LyricsService 的 PluginManager（属性注入，避免循环依赖）
        var lyricsService = Services.GetRequiredService<ILyricsService>() as LyricsService;
        if (lyricsService != null)
        {
            lyricsService.PluginManager = Services.GetRequiredService<IPluginManager>();
        }

        // 初始化所有已启用的插件
        _ = Services.GetRequiredService<IPluginManager>().InitializeAllAsync();

        RegisterRescanReceiver();
    }

    /// <summary>注册插件请求的音乐库重新扫描广播接收器</summary>
    private void RegisterRescanReceiver()
    {
        try
        {
            var receiver = new RescanLibraryReceiver();
            var filter = new global::Android.Content.IntentFilter("catclawmusic.action.RESCAN_LIBRARY");
            RegisterReceiver(receiver, filter);
            Android.Util.Log.Info("CatClaw", "RescanLibraryReceiver registered ✅");
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("CatClaw", $"RegisterRescanReceiver failed: {ex.Message}");
        }
    }
}

/// <summary>接收插件发送的音乐库重新扫描请求广播，触发数据库+UI完整刷新</summary>
public class RescanLibraryReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        try
        {
            var source = intent?.GetStringExtra("source") ?? "unknown";
            Android.Util.Log.Info("CatClaw", $"RescanLibraryReceiver: received from {source}, triggering full library reload...");

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var libVm = MainApplication.Services?.GetService<LibraryViewModel>();
                    if (libVm != null)
                    {
                        var loadMethod = libVm.GetType().GetMethod("LoadLocalAsync",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (loadMethod != null)
                        {
                            var task = loadMethod.Invoke(libVm, new object[] { true }) as System.Threading.Tasks.Task;
                            if (task != null) await task;
                            Android.Util.Log.Info("CatClaw", $"RescanLibraryReceiver: LoadLocalAsync(forceReload=true) completed ✅");
                        }
                        else
                        {
                            var refreshMethod = libVm.GetType().GetMethod("Refresh",
                                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            refreshMethod?.Invoke(libVm, Array.Empty<object>());
                            Android.Util.Log.Info("CatClaw", $"RescanLibraryReceiver: Refresh() completed ✅");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Android.Util.Log.Error("CatClaw", $"RescanLibraryReceiver refresh failed: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error("CatClaw", $"RescanLibraryReceiver.OnReceive error: {ex.Message}");
        }
    }
}
