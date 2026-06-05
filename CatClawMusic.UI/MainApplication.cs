using Android.App;
using Android.Content;
using Android.Runtime;
using AndroidX.AppCompat.App;
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
        var externalDir = global::Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath
            ?? Path.Combine(global::Android.App.Application.Context.FilesDir!.AbsolutePath, "databases");
        string dbPath = Path.Combine(externalDir, "catclaw.db");
        string oldDbPath = Path.Combine(CacheDir?.AbsolutePath ?? Path.Combine(externalDir, "..", "cache"), "catclaw.db");

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
        database.ExtractArtistNameCallback = (filePath) =>
        {
            try
            {
                var retriever = new Android.Media.MediaMetadataRetriever();
                retriever.SetDataSource(filePath);
                var artistName = retriever.ExtractMetadata(Android.Media.MetadataKey.Artist);
                retriever.Release();
                return artistName;
            }
            catch { return null; }
        };
        services.AddSingleton(database);

        // Core services
        services.AddSingleton<ISubsonicService, SubsonicService>();
        services.AddSingleton<INetworkFileService, WebDavService>();
        services.AddSingleton<INetworkFileService, SmbService>();
        services.AddSingleton<SmbService>(sp =>
            sp.GetServices<INetworkFileService>().FirstOrDefault(s => s is SmbService) as SmbService
            ?? new SmbService());
        services.AddSingleton<INetworkMusicService>(sp =>
        {
            var db = sp.GetRequiredService<MusicDatabase>();
            var subsonic = sp.GetRequiredService<ISubsonicService>();
            var fileServices = sp.GetServices<INetworkFileService>().ToList();
            var webDav = fileServices.FirstOrDefault(s => s is WebDavService) ?? fileServices.FirstOrDefault();
            var smb = fileServices.FirstOrDefault(s => s is SmbService) ?? fileServices.LastOrDefault();
            return new NetworkMusicService(db, subsonic, webDav!, smb!);
        });
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
        LyricsService.ContentUriLyricsReader = uri =>
        {
            try
            {
                using var stream = global::Android.App.Application.Context.ContentResolver!.OpenInputStream(global::Android.Net.Uri.Parse(uri)!);
                if (stream == null) return null;
                var lyrics = CatClawMusic.Core.Services.TagReader.ReadEmbeddedLyricsFromStream(stream, System.IO.Path.GetFileName(uri));
                return !string.IsNullOrWhiteSpace(lyrics) ? lyrics : null;
            }
            catch { return null; }
        };
        LyricsService.AndroidFileStreamOpener = filePath =>
        {
            try
            {
                return System.IO.File.OpenRead(filePath);
            }
            catch
            {
                try
                {
                    var ctx = global::Android.App.Application.Context;
                    var baseUri = Android.Provider.MediaStore.Audio.Media.ExternalContentUri;
                    using var cursor = ctx.ContentResolver!.Query(baseUri,
                        new[] { Android.Provider.MediaStore.Audio.Media.InterfaceConsts.Id },
                        $"{Android.Provider.MediaStore.Audio.Media.InterfaceConsts.Data} = ?",
                        new[] { filePath }, null);
                    if (cursor != null && cursor.MoveToFirst())
                    {
                        var id = cursor.GetLong(cursor.GetColumnIndexOrThrow(Android.Provider.MediaStore.Audio.Media.InterfaceConsts.Id));
                        cursor.Close();
                        var contentUri = Android.Content.ContentUris.WithAppendedId(baseUri, id);
                        return ctx.ContentResolver.OpenInputStream(contentUri);
                    }
                    cursor?.Close();
                }
                catch { }
            }
            return null;
        };
        /* 注入 C++ 原生编码检测器到歌词服务，优先使用原生库进行编码检测 */
        LyricsService.NativeEncodingDetector = rawBytes =>
        {
            try { return NativeInterop.DetectAndConvertToUtf8(rawBytes); }
            catch { return null; }
        };
        /* 初始化原生库（CPU 特性检测等） */
        try { NativeInterop.Init(); } catch { }
        services.AddSingleton<IMusicLibraryService, MusicLibraryService>();
        services.AddSingleton<IPermissionService, PermissionService>();
        services.AddSingleton<PlayQueue>();

        // Data services (with Android cache directory injection)
        services.AddSingleton<ExploreDataService>(sp =>
        {
            var db = sp.GetRequiredService<MusicDatabase>();
            var library = sp.GetRequiredService<IMusicLibraryService>();
            var cacheDir = global::Android.App.Application.Context.CacheDir!.AbsolutePath;
            return new ExploreDataService(db, library, cacheDir);
        });
        services.AddSingleton<NetEaseMusicScraper>(sp =>
        {
            var db = sp.GetRequiredService<MusicDatabase>();
            var cacheDir = global::Android.App.Application.Context.CacheDir!.AbsolutePath;
            return new NetEaseMusicScraper(db,
                Path.Combine(cacheDir, "artist_covers"),
                Path.Combine(cacheDir, "album_covers"));
        });
        services.AddSingleton<AiArtistScraper>(sp =>
        {
            var cacheDir = global::Android.App.Application.Context.CacheDir!.AbsolutePath;
            var llmClient = sp.GetRequiredService<CatClawMusic.Core.Interfaces.ILlmClient>();
            var agentService = sp.GetService<CatClawMusic.Core.Interfaces.IAgentService>();
            return new AiArtistScraper(llmClient, Path.Combine(cacheDir, "artist_covers"),
                () => agentService?.IsConfigured ?? false,
                () => sp.GetService<NetEaseMusicScraper>());
        });
        // 注册 IArtistMetadataScraper 实现
        services.AddSingleton<IArtistMetadataScraper>(sp => sp.GetRequiredService<NetEaseMusicScraper>());
        services.AddSingleton<IArtistMetadataScraper>(sp => sp.GetRequiredService<AiArtistScraper>());

        // Android platform services
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IMainThreadDispatcher, MainThreadDispatcher>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<ILogService, LogService>();

        // AI Agent services
        var agentConfigStorage = new AgentConfigStorage();
        CatClawMusic.Core.Services.AI.AgentService.Initialize(agentConfigStorage);

        services.AddSingleton<CatClawMusic.Core.Interfaces.IAgentConfigStorage>(agentConfigStorage);
        services.AddSingleton<CatClawMusic.Core.Interfaces.ILlmClient>(sp =>
            new CatClawMusic.Core.Services.AI.OpenAiCompatibleLlmClient(
                () => CatClawMusic.Core.Services.AI.AgentService.LoadConfig()));
        services.AddSingleton<CatClawMusic.Core.Interfaces.IAgentTool, CatClawMusic.Core.Services.AI.SearchMusicTool>();
        services.AddSingleton<CatClawMusic.Core.Interfaces.IAgentTool, CatClawMusic.Core.Services.AI.CreatePlaylistTool>();
        services.AddSingleton<CatClawMusic.Core.Interfaces.IAgentTool, CatClawMusic.Core.Services.AI.AddSongToPlaylistTool>();
        services.AddSingleton<CatClawMusic.Core.Interfaces.IAgentTool, CatClawMusic.Core.Services.AI.RemoveSongFromPlaylistTool>();
        services.AddSingleton<CatClawMusic.Core.Interfaces.IAgentTool, CatClawMusic.Core.Services.AI.ListPlaylistsTool>();
        services.AddSingleton<CatClawMusic.Core.Interfaces.IAgentTool, CatClawMusic.Core.Services.AI.GetPlaylistSongsTool>();
        services.AddSingleton<CatClawMusic.Core.Interfaces.IAgentTool, CatClawMusic.Core.Services.AI.DeletePlaylistTool>();
        services.AddSingleton<CatClawMusic.Core.Interfaces.IAgentTool, CatClawMusic.Core.Services.AI.PlaySongTool>();
        services.AddSingleton<CatClawMusic.Core.Interfaces.IAgentTool, CatClawMusic.Core.Services.AI.WebSearchTool>();
        services.AddSingleton<CatClawMusic.Core.Interfaces.IAgentTool, CatClawMusic.Core.Services.AI.ControlPlaybackTool>();
        services.AddSingleton<CatClawMusic.Core.Interfaces.IAgentTool, CatClawMusic.Core.Services.AI.GetCurrentSongTool>();
        services.AddSingleton<CatClawMusic.Core.Interfaces.IAgentTool, CatClawMusic.Core.Services.AI.GetPlayQueueTool>();
        services.AddSingleton<CatClawMusic.Core.Interfaces.IAgentTool>(sp =>
            new CatClawMusic.Core.Services.AI.ToggleFavoriteTool((songId, isFav) =>
                sp.GetRequiredService<MusicDatabase>().SetFavoriteAsync(songId, isFav)));
        services.AddSingleton<CatClawMusic.Core.Interfaces.IAgentTool, CatClawMusic.Core.Services.AI.GetFavoriteSongsTool>();
        services.AddSingleton<CatClawMusic.Core.Interfaces.IAgentTool, CatClawMusic.Core.Services.AI.GetRecentSongsTool>();
        services.AddSingleton<CatClawMusic.Core.Interfaces.IAgentTool, CatClawMusic.Core.Services.AI.GetListeningStatsTool>();
        services.AddSingleton<CatClawMusic.Core.Interfaces.IAgentTool, CatClawMusic.Core.Services.AI.AddToPlayQueueTool>();
        services.AddSingleton<CatClawMusic.Core.Interfaces.IAgentTool, CatClawMusic.Core.Services.AI.ClearPlayQueueTool>();
        services.AddSingleton<CatClawMusic.Core.Interfaces.IAgentService, CatClawMusic.Core.Services.AI.AgentService>();

        // Backup service
        services.AddSingleton<BackupService>(sp =>
            new BackupService(sp.GetRequiredService<MusicDatabase>(),
                sp.GetRequiredService<CatClawMusic.Core.Interfaces.IAgentConfigStorage>()));

        // ViewModels
        services.AddSingleton<LibraryViewModel>();       // 单例——Fragment 重建时不丢缓存
        services.AddSingleton<NowPlayingViewModel>();    // 单例——迷你播放器和全屏播放器共享状态
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SearchViewModel>();
        services.AddSingleton<PlaylistViewModel>();
        services.AddTransient<WebDavSettingsViewModel>();
        services.AddTransient<NavidromeSettingsViewModel>();
        services.AddTransient<SmbSettingsViewModel>();
        services.AddTransient<PlaylistDetailViewModel>();

        // Fragments (transient)
        services.AddTransient<FullLyricsFragment>();
        services.AddTransient<LibraryFragment>();
        services.AddTransient<NowPlayingFragment>();
        services.AddTransient<LandscapeNowPlayingFragment>();
        services.AddTransient<PlaylistFragment>();
        services.AddTransient<SearchFragment>();
        services.AddTransient<SettingsFragment>();
        services.AddTransient<PlaylistDetailFragment>();
        services.AddTransient<RemoteMusicFragment>();
        services.AddTransient<WebDavSettingsFragment>();
        services.AddTransient<NavidromeSettingsFragment>();
        services.AddTransient<SmbSettingsFragment>();
        services.AddTransient<MusicFolderSettingsFragment>();
        services.AddTransient<LocalMusicSettingsFragment>();
        services.AddTransient<GeneralSettingsFragment>();
        services.AddTransient<DesktopLyricFragment>();
        services.AddTransient<PluginManagementFragment>();
        services.AddTransient<AiSettingsFragment>();
        services.AddTransient<ModelManagerFragment>();
        services.AddTransient<ModelEditFragment>();
        services.AddTransient<AboutFragment>();
        services.AddTransient<ArtistMatchFragment>();
        services.AddTransient<ArtistMatchDetailFragment>();
        services.AddTransient<ModelAdapter>();

        // Adapters
        services.AddTransient<SongAdapter>();
        services.AddTransient<PlaylistAdapter>();
        services.AddTransient<UpcomingSongAdapter>();

        Services = services.BuildServiceProvider();

        // 初始化主题设置（确保在应用启动时就正确设置）
        try
        {
            var themeService = Services.GetRequiredService<IThemeService>();
            switch (themeService.DarkModeSetting)
            {
                case DarkModeSetting.Light:
                    AppCompatDelegate.DefaultNightMode = AppCompatDelegate.ModeNightNo;
                    break;
                case DarkModeSetting.Dark:
                    AppCompatDelegate.DefaultNightMode = AppCompatDelegate.ModeNightYes;
                    break;
                case DarkModeSetting.FollowSystem:
                    AppCompatDelegate.DefaultNightMode = AppCompatDelegate.ModeNightFollowSystem;
                    break;
            }
        }
        catch { }

        // 设置 LyricsService 的 PluginManager（属性注入，避免循环依赖）
        var lyricsService = Services.GetRequiredService<ILyricsService>() as LyricsService;
        if (lyricsService != null)
        {
            lyricsService.PluginManager = Services.GetRequiredService<IPluginManager>();
        }

        // 初始化所有已启用的插件（fire-and-forget，异常不会导致闪退）
        _ = Task.Run(async () =>
        {
            try { await Services.GetRequiredService<IPluginManager>().InitializeAllAsync(); }
            catch (Exception ex) { Android.Util.Log.Warn("CatClaw", $"PluginManager.InitializeAllAsync failed: {ex.Message}"); }
        });

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
