using Android.App;
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

[Application(Theme = "@style/CatClawTheme")]
public class MainApplication : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public MainApplication(IntPtr handle, JniHandleOwnership transfer) : base(handle, transfer) { }

    public override void OnCreate()
    {
        base.OnCreate();

        var services = new ServiceCollection();

        // Database（v4：新增 RemoteId 列用于网络歌曲去重）
        string dbPath = Path.Combine(CacheDir!.AbsolutePath, "catclaw.db");
        if (!File.Exists(Path.Combine(CacheDir!.AbsolutePath, "catclaw_v4.marker")))
        {
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
            File.WriteAllText(Path.Combine(CacheDir!.AbsolutePath, "catclaw_v4.marker"), "1");
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
            var pluginsDir = System.IO.Path.Combine(
                global::Android.App.Application.Context.FilesDir!.AbsolutePath, "plugins");
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
    }
}
