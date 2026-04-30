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

        // Database
        string dbPath = Path.Combine(CacheDir!.AbsolutePath, "catclaw.db");
        var database = new MusicDatabase(dbPath);
        services.AddSingleton(database);

        // Core services
        services.AddSingleton<ISubsonicService, SubsonicService>();
        services.AddSingleton<INetworkFileService, WebDavService>();
        services.AddSingleton<INetworkMusicService, NetworkMusicService>();
        services.AddSingleton<IAudioPlayerService, AudioPlayerService>();
        services.AddSingleton<ILyricsService, LyricsService>();
        services.AddSingleton<IMusicLibraryService, MusicLibraryService>();
        services.AddSingleton<IPermissionService, PermissionService>();
        services.AddSingleton<PlayQueue>();

        // Android platform services
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IMainThreadDispatcher, MainThreadDispatcher>();

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
        services.AddTransient<LibraryFragment>();
        services.AddTransient<NowPlayingFragment>();
        services.AddTransient<PlaylistFragment>();
        services.AddTransient<SearchFragment>();
        services.AddTransient<SettingsFragment>();
        services.AddTransient<PlaylistDetailFragment>();
        services.AddTransient<WebDavSettingsFragment>();
        services.AddTransient<NavidromeSettingsFragment>();
        services.AddTransient<MusicFolderSettingsFragment>();

        // Adapters
        services.AddTransient<SongAdapter>();
        services.AddTransient<PlaylistAdapter>();
        services.AddTransient<UpcomingSongAdapter>();

        Services = services.BuildServiceProvider();
    }
}
