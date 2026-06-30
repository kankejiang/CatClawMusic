using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services;
using CatClawMusic.Core.Services.AI;
using CatClawMusic.Data;
using CatClawMusic.Maui.Services;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CatClawMusic.Maui;

public static class MauiProgram
{
    public static IServiceProvider Services { get; private set; } = null!;

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        var services = builder.Services;

        // ═══════════════════════════════════════════════════
        // Database (singleton — one SQLite connection)
        // ═══════════════════════════════════════════════════
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "catclaw.db");
        var db = new MusicDatabase(dbPath);
        _ = Task.Run(async () => { try { await db.EnsureInitializedAsync(); } catch { } });
        services.AddSingleton(db);

        // ═══════════════════════════════════════════════════
        // Core services
        // ═══════════════════════════════════════════════════
        services.AddSingleton<PlayQueue>();
        services.AddSingleton<ILyricsService, LyricsService>();
        services.AddSingleton<LyricsService>(sp => (LyricsService)sp.GetRequiredService<ILyricsService>());

        // LyricsService 回调 — 跨平台实现（Android content:// 等由平台条件编译补充）
        LyricsService.AndroidFileStreamOpener = filePath =>
        {
            try { return File.OpenRead(filePath); } catch { return null; }
        };
        LyricsService.FileBytesReaderAsync = async filePath =>
        {
            try { return await File.ReadAllBytesAsync(filePath); } catch { return null; }
        };
#if ANDROID
        LyricsService.ContentUriReader = async uri =>
        {
            try
            {
                var ctx = global::Android.App.Application.Context;
                using var stream = ctx.ContentResolver?.OpenInputStream(global::Android.Net.Uri.Parse(uri));
                if (stream == null) return null;
                using var reader = new StreamReader(stream);
                return await reader.ReadToEndAsync();
            }
            catch { return null; }
        };
#endif

        // ═══════════════════════════════════════════════════
        // Network services
        // ═══════════════════════════════════════════════════
        services.AddSingleton<ISubsonicService, SubsonicService>();
        services.AddSingleton<INetworkFileService, WebDavService>();
        services.AddSingleton<INetworkFileService, SmbService>();
        services.AddSingleton<SmbService>(sp =>
            sp.GetServices<INetworkFileService>().FirstOrDefault(s => s is SmbService) as SmbService
            ?? new SmbService());
        services.AddSingleton<INetworkMusicService>(sp =>
        {
            var database = sp.GetRequiredService<MusicDatabase>();
            var subsonic = sp.GetRequiredService<ISubsonicService>();
            var fileSvcs = sp.GetServices<INetworkFileService>().ToList();
            var webDav = fileSvcs.FirstOrDefault(s => s is WebDavService) ?? fileSvcs.FirstOrDefault();
            var smb = fileSvcs.FirstOrDefault(s => s is SmbService) ?? fileSvcs.LastOrDefault();
            return new NetworkMusicService(database, subsonic, webDav!, smb!);
        });

        // ═══════════════════════════════════════════════════
        // Audio (Media3 ExoPlayer + FFmpeg — 跨平台)
        // ═══════════════════════════════════════════════════
        services.AddSingleton<AudioPlayerService>();
        services.AddSingleton<IAudioPlayerService>(sp => sp.GetRequiredService<AudioPlayerService>());
#if ANDROID
        services.AddSingleton<Services.FFmpegService>();

        // 在启动时初始化 FFmpeg 并注入到 AudioPlayerService
        _ = Task.Run(async () =>
        {
            try
            {
                var ffmpeg = services.BuildServiceProvider().GetRequiredService<Services.FFmpegService>();
                await ffmpeg.InitializeAsync();
                var audio = services.BuildServiceProvider().GetRequiredService<AudioPlayerService>();
                audio.SetFFmpegService(ffmpeg);
            }
            catch { }
        });
#endif

        // ═══════════════════════════════════════════════════
        // Data services
        // ═══════════════════════════════════════════════════
        services.AddSingleton<IMusicLibraryService, MusicLibraryService>();
        services.AddSingleton<MultiSourceSearchService>();

        var appDataDir = FileSystem.AppDataDirectory;
        var artistCoversDir = Path.Combine(appDataDir, "artist_covers");
        var albumCoversDir = Path.Combine(appDataDir, "album_covers");
        var metadataDir = Path.Combine(appDataDir, "metadata");
        Directory.CreateDirectory(artistCoversDir);
        Directory.CreateDirectory(albumCoversDir);
        Directory.CreateDirectory(metadataDir);

        services.AddSingleton<ExploreDataService>(sp =>
            new ExploreDataService(sp.GetRequiredService<MusicDatabase>(),
                sp.GetRequiredService<IMusicLibraryService>(),
                Path.Combine(appDataDir, "cache")));

        services.AddSingleton<NetEaseMusicScraper>(sp =>
            new NetEaseMusicScraper(sp.GetRequiredService<MusicDatabase>(), artistCoversDir, albumCoversDir));
        services.AddSingleton<MultiSourcePhotoScraper>(_ =>
            new MultiSourcePhotoScraper(artistCoversDir));
        services.AddSingleton<AiArtistScraper>(_ =>
            new AiArtistScraper(artistCoversDir, () => AgentService.LoadConfig()));

        // IArtistMetadataScraper 实现（优先级：网易云 → AI → 多源照片）
        services.AddSingleton<IArtistMetadataScraper>(sp => sp.GetRequiredService<NetEaseMusicScraper>());
        services.AddSingleton<IArtistMetadataScraper>(sp => sp.GetRequiredService<AiArtistScraper>());
        services.AddSingleton<IArtistMetadataScraper>(sp => sp.GetRequiredService<MultiSourcePhotoScraper>());

        // ═══════════════════════════════════════════════════
        // AI Agent services
        // ═══════════════════════════════════════════════════
        var agentConfigStorage = new AgentConfigStorage();
        AgentService.Initialize(agentConfigStorage);

        services.AddSingleton<IAgentConfigStorage>(agentConfigStorage);
        services.AddSingleton<ILlmClient>(_ =>
            new OpenAiCompatibleLlmClient(
                () => AgentService.LoadConfig(),
                () => AgentService.LoadAllConfigs()));
        services.AddSingleton<IAgentTool, SearchMusicTool>();
        services.AddSingleton<IAgentTool, CreatePlaylistTool>();
        services.AddSingleton<IAgentTool, AddSongToPlaylistTool>();
        services.AddSingleton<IAgentTool, RemoveSongFromPlaylistTool>();
        services.AddSingleton<IAgentTool, ListPlaylistsTool>();
        services.AddSingleton<IAgentTool, GetPlaylistSongsTool>();
        services.AddSingleton<IAgentTool, DeletePlaylistTool>();
        services.AddSingleton<IAgentTool, PlaySongTool>();
        services.AddSingleton<IAgentTool, WebSearchTool>();
        services.AddSingleton<IAgentTool, ControlPlaybackTool>();
        services.AddSingleton<IAgentTool, GetCurrentSongTool>();
        services.AddSingleton<IAgentTool, GetPlayQueueTool>();
        services.AddSingleton<IAgentTool>(sp =>
            new ToggleFavoriteTool((songId, isFav) =>
                sp.GetRequiredService<MusicDatabase>().SetFavoriteAsync(songId, isFav)));
        services.AddSingleton<IAgentTool, GetFavoriteSongsTool>();
        services.AddSingleton<IAgentTool, GetRecentSongsTool>();
        services.AddSingleton<IAgentTool, GetListeningStatsTool>();
        services.AddSingleton<IAgentTool, AddToPlayQueueTool>();
        services.AddSingleton<IAgentTool, ClearPlayQueueTool>();
        services.AddSingleton<IAgentService, AgentService>();

        // ═══════════════════════════════════════════════════
        // Backup & Update
        // ═══════════════════════════════════════════════════
        services.AddSingleton<BackupService>(sp =>
            new BackupService(sp.GetRequiredService<MusicDatabase>(),
                sp.GetRequiredService<IAgentConfigStorage>(),
                artistCoversDir));
        services.AddSingleton<IUpdateService, UpdateService>();

        // ═══════════════════════════════════════════════════
        // CatClaw Server & P2P
        // ═══════════════════════════════════════════════════
        services.AddSingleton<ICatClawServerService>(sp =>
            new CatClawServerClient(sp.GetRequiredService<MusicDatabase>()));
        services.AddSingleton<IP2PService, P2PClientService>();

        // ═══════════════════════════════════════════════════
        // Platform services
        // ═══════════════════════════════════════════════════
        services.AddSingleton<IPermissionService, PermissionService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IMainThreadDispatcher, MainThreadDispatcher>();
        services.AddSingleton<ILogService, LogService>();

        // ═══════════════════════════════════════════════════
        // Infrastructure services
        // ═══════════════════════════════════════════════════
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();

        // ═══════════════════════════════════════════════════
        // Local scan service
        // ═══════════════════════════════════════════════════
        services.AddSingleton<Services.LocalScanService>();

        // ═══════════════════════════════════════════════════
        // Plugin Manager
        // ═══════════════════════════════════════════════════
        services.AddSingleton<IPluginManager>(sp =>
        {
            var allPlugins = sp.GetServices<IPlugin>();
            var pluginsDir = Path.Combine(FileSystem.AppDataDirectory, "Plugin");
            return new PluginManager(
                allPlugins,
                typeId => Preferences.Default.Get($"plugin_enabled_{typeId}", true),
                (typeId, enabled) => Preferences.Default.Set($"plugin_enabled_{typeId}", enabled),
                pluginsDir
            );
        });

        // ═══════════════════════════════════════════════════
        // ViewModels
        // ═══════════════════════════════════════════════════
        services.AddSingleton<NowPlayingViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<PlaylistViewModel>();
        services.AddTransient<SearchViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AlbumDetailViewModel>();
        services.AddTransient<ArtistDetailViewModel>();
        services.AddTransient<PlaylistDetailViewModel>();
        services.AddTransient<AppearanceSettingsViewModel>();
        services.AddTransient<GeneralSettingsViewModel>();
        services.AddTransient<BackupRestoreViewModel>();
        services.AddTransient<AboutViewModel>();
        services.AddTransient<LocalMusicSettingsViewModel>();
        services.AddTransient<MusicFolderSettingsViewModel>();
        services.AddTransient<AiSettingsViewModel>();
        services.AddTransient<PermissionManagementViewModel>();
        services.AddTransient<RemoteMusicSettingsViewModel>();
        services.AddTransient<PluginManagementViewModel>();

        // ═══════════════════════════════════════════════════
        // Pages
        // ═══════════════════════════════════════════════════
        services.AddTransient<Pages.NowPlayingPage>();
        services.AddTransient<Pages.LibraryPage>();
        services.AddTransient<Pages.SearchPage>();
        services.AddTransient<Pages.SettingsPage>();
        services.AddTransient<Pages.AlbumDetailPage>();
        services.AddTransient<Pages.ArtistDetailPage>();
        services.AddTransient<Pages.PlaylistPage>();
        services.AddTransient<Pages.PlaylistDetailPage>();
        services.AddTransient<Pages.AppearanceSettingsPage>();
        services.AddTransient<Pages.GeneralSettingsPage>();
        services.AddTransient<Pages.BackupRestorePage>();
        services.AddTransient<Pages.AboutPage>();
        services.AddTransient<Pages.LocalMusicSettingsPage>();
        services.AddTransient<Pages.MusicFolderSettingsPage>();
        services.AddTransient<Pages.RemoteMusicSettingsPage>();
        services.AddTransient<Pages.PluginManagementPage>();
        services.AddTransient<Pages.AiSettingsPage>();
        services.AddTransient<Pages.PermissionManagementPage>();
        services.AddTransient<Pages.FullLyricsPage>();
        services.AddTransient<Pages.FolderBrowserPage>();
        services.AddTransient<Pages.ArtistMatchPage>();
        services.AddTransient<Pages.ArtistMatchDetailPage>();
        services.AddTransient<Pages.DesktopLyricPage>();
        services.AddTransient<Pages.ModelManagerPage>();
        services.AddTransient<Pages.ModelEditPage>();
        services.AddTransient<Pages.SplashSettingsPage>();
        services.AddTransient<Pages.ServerSettingsPage>();
        services.AddTransient<Pages.P2PSettingsPage>();
        services.AddTransient<Pages.SongDetailPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();
        Services = app.Services;
        return app;
    }
}
