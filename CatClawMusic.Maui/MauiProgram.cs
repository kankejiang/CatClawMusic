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
        // 写固定路径，确保能找到日志
        var logPath = Path.Combine(Path.GetTempPath(), "catclaw_startup.log");
        try { File.Delete(logPath); } catch { }
        void Log(string msg)
        {
            System.Diagnostics.Debug.WriteLine($"[STARTUP] {msg}");
            try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); } catch { }
        }

        Log("Step 0: CreateMauiApp entry");
#if WINDOWS
        Log("Step 0b: WINDOWS symbol IS defined");
#else
        Log("Step 0b: WINDOWS symbol is NOT defined");
#endif
#if ANDROID
        Log("Step 0c: ANDROID symbol IS defined");
#else
        Log("Step 0c: ANDROID symbol is NOT defined");
#endif
        var builder = MauiApp.CreateBuilder();
        Log("Step 2: UseMauiApp");
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            })
            .ConfigureMauiHandlers(handlers =>
            {
#if ANDROID
                handlers.AddHandler(typeof(CatClawMusic.Maui.Controls.FrostedBackground),
                    typeof(CatClawMusic.Maui.Platforms.Android.FrostedBackgroundHandler));
#endif
#if WINDOWS
                handlers.AddHandler(typeof(CatClawMusic.Maui.Controls.FrostedBackground),
                    typeof(CatClawMusic.Maui.Platforms.Windows.FrostedBackgroundHandler));
#endif
            })
            .ConfigureImageSources(images =>
            {
#if ANDROID
                // 注册自定义 FileImageSource 服务：使用内存缓存避免 CollectionView 滑动时反复解码封面图片
                images.AddService<Microsoft.Maui.Controls.FileImageSource, CatClawMusic.Maui.Platforms.Android.CachingFileImageSourceService>();
#endif
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

        // 远程 URL 流打开器：下载 http(s):// 文件到 MemoryStream（供内嵌歌词读取），限制大小避免下载超大文件
        LyricsService.RemoteUrlStreamOpener = url =>
        {
            try
            {
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(20);
                // 先请求 HEAD 获取文件大小，超过 50MB 则跳过
                try
                {
                    var headReq = new HttpRequestMessage(HttpMethod.Head, url);
                    var headResp = httpClient.Send(headReq);
                    if (headResp.IsSuccessStatusCode && headResp.Content.Headers.ContentLength.HasValue)
                    {
                        var size = headResp.Content.Headers.ContentLength.Value;
                        if (size > 50 * 1024 * 1024)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Lyrics] 远程文件过大 ({size / 1024 / 1024}MB)，跳过内嵌歌词读取");
                            return null;
                        }
                    }
                }
                catch { /* HEAD 失败则继续 GET */ }

                var bytes = httpClient.GetByteArrayAsync(url).GetAwaiter().GetResult();
                if (bytes.Length == 0) return null;
                if (bytes.Length > 50 * 1024 * 1024) return null;
                return new MemoryStream(bytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Lyrics] RemoteUrlStreamOpener 异常: {ex.Message}");
                return null;
            }
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

        // SMB 本地 HTTP 代理（将 smb:// URL 桥接为 http://127.0.0.1:port 供 ExoPlayer 播放）
        services.AddSingleton<SmbStreamProxy>(sp =>
        {
            var smbSvc = sp.GetRequiredService<SmbService>();
            var proxy = new SmbStreamProxy(smbSvc);
            SmbStreamProxy.Current = proxy;
            return proxy;
        });

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
        services.AddTransient<AlbumsViewModel>();
        services.AddTransient<ArtistsViewModel>();
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
        services.AddTransient<FolderBrowserViewModel>();

        // ═══════════════════════════════════════════════════
        // App Shell
        // ═══════════════════════════════════════════════════
        services.AddSingleton<AppShell>();

        // ═══════════════════════════════════════════════════
        // Pages
        // ═══════════════════════════════════════════════════
        services.AddSingleton<Pages.MainPage>();
        services.AddSingleton<Pages.DesktopMainPage>();
        services.AddTransient<Pages.NowPlayingPage>();
        services.AddTransient<Pages.LibraryPage>();
        services.AddTransient<Pages.SearchPage>();
        services.AddTransient<Pages.SettingsPage>();
        services.AddTransient<Pages.AlbumDetailPage>();
        services.AddTransient<Pages.ArtistDetailPage>();
        services.AddTransient<Pages.AlbumsPage>();
        services.AddTransient<Pages.ArtistsPage>();
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

        Log("Step 50: Build");
        var app = builder.Build();
        Services = app.Services;
        Log("Step 51: Services set");

        // 初始化 SMB 代理并配置播放器 URL 转换器
        var smbProxy = Services.GetRequiredService<SmbStreamProxy>();
        var networkMusic = Services.GetRequiredService<INetworkMusicService>();
        AudioPlayerService.UrlTransformer = url =>
        {
            if (url.StartsWith("smb://", StringComparison.OrdinalIgnoreCase))
                return smbProxy.ToProxyUrl(url);
            return null;
        };

        // 异步 URL 解析器：修复 WebDAV/OpenList URL（添加 /dav 前缀或获取签名 raw_url）
        AudioPlayerService.AsyncUrlResolver = async url =>
        {
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return await networkMusic.ResolveWebDavPlaybackUrlAsync(url);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AsyncUrlResolver] WebDAV URL 解析失败: {ex.Message}");
                }
            }
            return null;
        };

        // 扩展 RemoteUrlStreamOpener 支持 smb:// URL（用于读取内嵌歌词）和 WebDAV URL 修复
        var prevStreamOpener = LyricsService.RemoteUrlStreamOpener;
        LyricsService.RemoteUrlStreamOpener = url =>
        {
            if (url.StartsWith("smb://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var proxyUrl = smbProxy.ToProxyUrl(url);
                    if (proxyUrl == null) return null;
                    using var httpClient = new HttpClient();
                    httpClient.Timeout = TimeSpan.FromSeconds(20);
                    var bytes = httpClient.GetByteArrayAsync(proxyUrl).GetAwaiter().GetResult();
                    if (bytes.Length == 0 || bytes.Length > 50 * 1024 * 1024) return null;
                    return new MemoryStream(bytes);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Lyrics] SMB 流打开异常: {ex.Message}");
                    return null;
                }
            }

            // WebDAV HTTP URL：先解析正确的URL（修复/dav前缀或获取raw_url）
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var resolvedUrl = networkMusic.ResolveWebDavPlaybackUrlAsync(url).GetAwaiter().GetResult();
                    var downloadUrl = string.IsNullOrEmpty(resolvedUrl) ? url : resolvedUrl;

                    var handler = new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
                        AllowAutoRedirect = true
                    };
                    using var httpClient = new HttpClient(handler);
                    httpClient.Timeout = TimeSpan.FromSeconds(20);
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "CatClawMusic/1.0");

                    try
                    {
                        var headReq = new HttpRequestMessage(HttpMethod.Head, downloadUrl);
                        var headResp = httpClient.Send(headReq);
                        if (headResp.IsSuccessStatusCode && headResp.Content.Headers.ContentLength.HasValue)
                        {
                            var size = headResp.Content.Headers.ContentLength.Value;
                            if (size > 50 * 1024 * 1024)
                            {
                                System.Diagnostics.Debug.WriteLine($"[Lyrics] 远程文件过大 ({size / 1024 / 1024}MB)，跳过内嵌歌词读取");
                                return null;
                            }
                        }
                    }
                    catch { /* HEAD 失败则继续 GET */ }

                    var bytes = httpClient.GetByteArrayAsync(downloadUrl).GetAwaiter().GetResult();
                    if (bytes.Length == 0) return null;
                    if (bytes.Length > 50 * 1024 * 1024) return null;
                    return new MemoryStream(bytes);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Lyrics] WebDAV/HTTP 流打开异常: {ex.Message}");
                    return null;
                }
            }

            return prevStreamOpener?.Invoke(url);
        };

        Log("Step 99: Build done, returning");
        return app;
    }
}

/// <summary>
/// 跨平台 SafeArea 辅助：提供系统栏高度（dp）并通知页面更新 padding。
/// Android 平台由 EdgeToEdgeInsets 调用 UpdateInsets；Windows 平台默认为 0。
/// </summary>
public static class SafeAreaHelper
{
    /// <summary>系统栏顶部高度（状态栏），单位 dp</summary>
    public static double TopInset { get; private set; }
    /// <summary>系统栏底部高度（导航栏），单位 dp</summary>
    public static double BottomInset { get; private set; }

    /// <summary>更新系统栏高度并触发事件（由平台代码调用）</summary>
    /// <param name="topDp">状态栏高度（dp）</param>
    /// <param name="bottomDp">导航栏高度（dp）</param>
    public static void UpdateInsets(double topDp, double bottomDp)
    {
        bool changed = Math.Abs(topDp - TopInset) > 0.5 || Math.Abs(bottomDp - BottomInset) > 0.5;
        TopInset = topDp;
        BottomInset = bottomDp;

        if (changed)
            SafeAreaChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>系统栏高度变化时触发（页面订阅此事件以更新 padding）</summary>
    public static event EventHandler? SafeAreaChanged;
}
