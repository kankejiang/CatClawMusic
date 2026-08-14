using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services;
using CatClawMusic.Core.Services.AI;
using CatClawMusic.Data;
using CatClawMusic.Maui.Services;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace CatClawMusic.Maui;

public static class MauiProgram
{
    public static IServiceProvider Services { get; private set; } = null!;

    private static readonly HttpClient _sharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public static MauiApp CreateMauiApp()
    {
        // 写固定路径，确保能找到日志
        var logPath = Path.Combine(Path.GetTempPath(), "catclaw_startup.log");
        try { File.Delete(logPath); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"清理启动日志失败: {ex.Message}"); }
        void StartupLog(string msg)
        {
            Log.Debug("MauiProgram", $"[STARTUP] {msg}");
            try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"启动日志写入失败: {ex.Message}"); }
        }

        StartupLog("Step 0: CreateMauiApp entry");
#if WINDOWS
        StartupLog("Step 0b: WINDOWS symbol IS defined");
#else
        StartupLog("Step 0b: WINDOWS symbol is NOT defined");
#endif
#if ANDROID
        StartupLog("Step 0c: ANDROID symbol IS defined");
#else
        StartupLog("Step 0c: ANDROID symbol is NOT defined");
#endif
        var builder = MauiApp.CreateBuilder();
        StartupLog("Step 2: UseMauiApp");
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
                handlers.AddHandler(typeof(CatClawMusic.Maui.Controls.KaraokeLabel),
                    typeof(CatClawMusic.Maui.Platforms.Android.KaraokeLabelHandler));
                // 原生 ViewPager2 分页容器（承载 5 个 MAUI 页，GPU 合成水平滑动）
                handlers.AddHandler(typeof(CatClawMusic.Maui.Controls.NativeTabPager),
                    typeof(CatClawMusic.Maui.Platforms.Android.NativeTabPagerHandler));
                // 栈式原生 ViewPager2 导航容器（给 Shell 子页的进出也套上原生水平滑动转场）
                handlers.AddHandler(typeof(CatClawMusic.Maui.Controls.PagerNavigator),
                    typeof(CatClawMusic.Maui.Platforms.Android.PagerNavigatorHandler));
#endif
#if WINDOWS
                handlers.AddHandler(typeof(CatClawMusic.Maui.Controls.FrostedBackground),
                    typeof(CatClawMusic.Maui.Platforms.Windows.FrostedBackgroundHandler));
                handlers.AddHandler(typeof(CatClawMusic.Maui.Controls.KaraokeLabel),
                    typeof(CatClawMusic.Maui.Platforms.Windows.KaraokeLabelHandler));
                handlers.AddHandler(typeof(CatClawMusic.Maui.Controls.SwapChainHost),
                    typeof(CatClawMusic.Maui.Platforms.Windows.SwapChainHostHandler));
#endif
            })
            .ConfigureEffects(effects =>
            {
#if WINDOWS
                effects.Add<CatClawMusic.Maui.Effects.LyricBlurEffect,
                    CatClawMusic.Maui.Platforms.Windows.Effects.LyricBlurPlatformEffect>();
#endif
            })
            .ConfigureImageSources(images =>
            {
#if ANDROID
                // 注册自定义 FileImageSource 服务：使用内存缓存避免 CollectionView 滑动时反复解码封面图片
                images.AddService<Microsoft.Maui.Controls.FileImageSource, CatClawMusic.Maui.Platforms.Android.CachingFileImageSourceService>();
                // 注册自定义 UriImageSource 服务：为 Navidrome 等 http 封面 URL 提供 Bitmap 内存缓存 + 磁盘缓存，
                // 避免滑动列表时每次都下载图片造成 LOS 堆 GC 风暴
                images.AddService<Microsoft.Maui.Controls.UriImageSource, CatClawMusic.Maui.Platforms.Android.CachingUriImageSourceService>();
                // 全局兜底：StreamImageSource 默认被 MAUI 原分辨率解码，超大图会触发
                // "Canvas: trying to draw too large" 崩溃。本服务对一切 FromStream 图片做降采样解码。
                images.AddService<Microsoft.Maui.Controls.StreamImageSource, CatClawMusic.Maui.Platforms.Android.CachingStreamImageSourceService>();
#endif
            });

        var services = builder.Services;

        // ═══════════════════════════════════════════════════
        // Startup coordinator (冷启动协调器：启动页等待关键服务就绪后才进入主界面)
        // ═══════════════════════════════════════════════════
        var startupCoordinator = new Services.StartupCoordinator();
        services.AddSingleton(startupCoordinator);

        // ═══════════════════════════════════════════════════
        // Database (singleton — one SQLite connection)
        // ═══════════════════════════════════════════════════
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "catclaw.db");
        var db = new MusicDatabase(dbPath);
        _ = Task.Run(async () =>
        {
            try { await db.EnsureInitializedAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"数据库初始化失败: {ex.Message}"); }
            finally { startupCoordinator.MarkDatabaseReady(); }
        });
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

        // 远程 URL 流打开器：下载 http(s):// 文件头部到 MemoryStream（供内嵌歌词读取）
        // 注意：Navidrome 歌曲在 LyricsService 中已跳过此路径（走 API），此处仅 WebDAV/SMB 直链会触发。
        // 使用 Range 请求仅下载文件前 2MB：FLAC/MP3(ID3v2)/M4A 标签均在文件头部，足以提取内嵌歌词。
        // WebDAV URL 形如 http://user:pass@host/path，HttpClient 不解析 URL userinfo，需手动提取并添加 Basic Auth 头。
        LyricsService.RemoteUrlStreamOpener = url =>
        {
            try
            {
                var urlPreview = url?[..Math.Min(60, url?.Length ?? 0)] ?? "";
                Log.Debug("MauiProgram", $"[Lyrics] RemoteUrlStreamOpener 入口: {urlPreview}...");
                const int headSize = 2 * 1024 * 1024; // 2MB 足以覆盖绝大多数音频标签头
                var httpClient = _sharedHttpClient;

                // 从 URL userinfo 提取 Basic Auth 凭证（WebDAV 播放 URL 带 user:pass@）
                string? authToken = null;
                string cleanUrl = url;
                try
                {
                    var uri = new Uri(url);
                    if (!string.IsNullOrEmpty(uri.UserInfo))
                    {
                        var userInfo = uri.UserInfo;
                        var colonIdx = userInfo.IndexOf(':');
                        if (colonIdx >= 0 && colonIdx < userInfo.Length - 1)
                        {
                            var user = Uri.UnescapeDataString(userInfo[..colonIdx]);
                            var pass = Uri.UnescapeDataString(userInfo[(colonIdx + 1)..]);
                            authToken = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{user}:{pass}"));
                        }
                        // 构造不含 userinfo 的 URL，避免某些服务器/代理对 URL userinfo 的异常处理
                        cleanUrl = new UriBuilder(uri.Scheme, uri.Host, uri.Port, uri.AbsolutePath, uri.Query).ToString();
                    }
                }
                catch { /* URL 解析失败则使用原始 URL */ }

                var cleanPreview = cleanUrl[..Math.Min(60, cleanUrl.Length)];
                Log.Debug("MauiProgram", $"[Lyrics] RemoteUrlStreamOpener cleanUrl={cleanPreview}..., authToken={(authToken != null ? "有" : "无")}");
                // 使用 Range 请求仅下载文件头部
                var reqMsg = new HttpRequestMessage(HttpMethod.Get, cleanUrl);
                reqMsg.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, headSize - 1);
                if (authToken != null)
                    reqMsg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authToken);
                Log.Debug("MauiProgram", "[Lyrics] RemoteUrlStreamOpener 发送 HTTP 请求...");
                // 用 Task.Run 包裹避免在 UI 线程同步等待（RemoteUrlStreamOpener 为库代码要求的同步签名）
                var resp = Task.Run(() => httpClient.SendAsync(reqMsg)).GetAwaiter().GetResult();
                Log.Debug("MauiProgram", $"[Lyrics] RemoteUrlStreamOpener HTTP 响应: {(int)resp.StatusCode} {resp.ReasonPhrase}");
                if (!resp.IsSuccessStatusCode)
                {
                    return null;
                }
                using var ms = new MemoryStream();
                resp.Content.ReadAsStream().CopyTo(ms);
                var bytes = ms.ToArray();
                Log.Debug("MauiProgram", $"[Lyrics] RemoteUrlStreamOpener 下载完成: {bytes.Length / 1024}KB");
                if (bytes.Length == 0) return null;
                return new MemoryStream(bytes);
            }
            catch (Exception ex)
            {
                Log.Debug("MauiProgram", $"[Lyrics] RemoteUrlStreamOpener 异常: {ex.Message}");
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
        services.AddSingleton<SleepTimerService>();
#if ANDROID
        services.AddSingleton<Services.FFmpegService>();
#endif

        // ═══════════════════════════════════════════════════
        // Data services
        // ═══════════════════════════════════════════════════
        services.AddSingleton<IMusicLibraryService, MusicLibraryService>();
        // 旧的多源硬编码搜索服务已迁移为内置 IOnlineMusicPlugin（见 Data/OnlineMusic），保留注册以兼容潜在引用
        services.AddSingleton<MultiSourceSearchService>();

        // ═══════════════════════════════════════════════════
        // Online music (empty shell)
        // ═══════════════════════════════════════════════════
        // 宿主为"空壳"：不内置任何音源插件。音源以独立 .dll（CatClawMusic.Plugins.OnlineMusic）
        // 通过「插件管理 → 安装」导入后自动被 PluginManager 收集，OnlineMusicAggregator 统一聚合。
        services.AddSingleton<Core.Services.OnlineMusicAggregator>();

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
        services.AddSingleton<IAgentTool, FetchWebPageTool>();
        services.AddSingleton<IAgentTool, BrowserOpenTool>();
        // Agent 浏览器：browser_open 工具经桥接控制内置浏览器页
        var agentBrowserCoordinator = new Services.AgentBrowser.AgentBrowserCoordinator();
        services.AddSingleton(agentBrowserCoordinator);
        CatClawMusic.Core.Services.AI.AgentBrowserBridge.Navigator = (url, ct) =>
            agentBrowserCoordinator.NavigateAndExtractAsync(url, ct);
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
                artistCoversDir,
                System.IO.Path.Combine(FileSystem.AppDataDirectory, "ai_memory.md")));
        services.AddSingleton<IUpdateService, UpdateService>();

        // ═══════════════════════════════════════════════════
        // CatClaw Server
        // ═══════════════════════════════════════════════════
        services.AddSingleton<ICatClawServerService>(sp =>
            new CatClawServerClient(sp.GetRequiredService<MusicDatabase>()));


        // ═══════════════════════════════════════════════════
        // Platform services
        // ═══════════════════════════════════════════════════
        services.AddSingleton<IPermissionService, PermissionService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<IMainThreadDispatcher, MainThreadDispatcher>();
        services.AddSingleton<ILogService, LogService>();

        // 桌面歌词服务（Android 使用 WindowManager 悬浮窗；Windows 使用独立悬浮歌词窗口；其他平台空实现）
#if ANDROID
        services.AddSingleton<Core.Interfaces.IDesktopLyricService, Platforms.Android.DesktopLyricService>();
#elif WINDOWS
        services.AddSingleton<Core.Interfaces.IDesktopLyricService, Services.WindowsDesktopLyricServiceV2>();
#else
        services.AddSingleton<Core.Interfaces.IDesktopLyricService, Services.EmptyDesktopLyricService>();
#endif
        services.AddSingleton<Services.DesktopLyricManager>();

        // ═══════════════════════════════════════════════════
        // Infrastructure services
        // ═══════════════════════════════════════════════════
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();

        // ═══════════════════════════════════════════════════
        // Local scan service
        // ═══════════════════════════════════════════════════
        services.AddSingleton<Services.LocalScanService>();
        services.AddSingleton<Services.IInteractionStateService, Services.InteractionStateService>();

        // ═══════════════════════════════════════════════════
        // Download manager（下载管理：任务队列/进度/持久化/下载路径）
        // ═══════════════════════════════════════════════════
        services.AddSingleton<Services.DownloadManager>();

        // ═══════════════════════════════════════════════════
        // Music library snapshot & chat memory
        // ═══════════════════════════════════════════════════
        services.AddSingleton<Services.MusicLibrarySnapshotService>();
        services.AddSingleton<Services.ChatMemoryService>();

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
        services.AddSingleton<SearchViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AlbumDetailViewModel>();
        services.AddTransient<ArtistDetailViewModel>();
        services.AddTransient<AllSongsViewModel>();
        services.AddTransient<Pages.AllSongsPage>();
        services.AddTransient<SongDetailViewModel>();
        services.AddTransient<AlbumsViewModel>();
        services.AddTransient<ArtistsViewModel>();
        services.AddTransient<PlaylistDetailViewModel>();
        services.AddTransient<AppearanceSettingsViewModel>();
        services.AddTransient<GeneralSettingsViewModel>();
        services.AddTransient<BackupRestoreViewModel>();
        services.AddTransient<AboutViewModel>();
        services.AddTransient<LogViewModel>();
        services.AddTransient<LocalMusicSettingsViewModel>();
        services.AddTransient<MusicFolderSettingsViewModel>();
        services.AddTransient<AiSettingsViewModel>();
        services.AddTransient<PermissionManagementViewModel>();
        services.AddTransient<RemoteMusicSettingsViewModel>();
        services.AddTransient<PluginManagementViewModel>();
        services.AddTransient<FolderBrowserViewModel>();
        services.AddTransient<ListeningStatsViewModel>();
        services.AddTransient<DownloadsViewModel>();
        services.AddTransient<Pages.DownloadsPage>();

        // ═══════════════════════════════════════════════════
        // App Shell
        // ═══════════════════════════════════════════════════
        services.AddSingleton<AppShell>();

        // ═══════════════════════════════════════════════════
        // Pages
        // ═══════════════════════════════════════════════════
        // MainPage/DesktopMainPage 为 Singleton：横竖屏切换时复用同一实例，
        // 避免每次重建 ViewPager2 及 5 个子页面（含歌词/播放页）带来的性能开销。
        // 子页面（NowPlayingPage 等）仍为 Transient，但 MainPage 构造只执行一次，
        // 故 ViewPager2 内的子页面实例也只创建一次。
        services.AddSingleton<Pages.MainPage>();
        services.AddSingleton<Pages.DesktopMainPage>();
        services.AddSingleton<Pages.DesktopBlankPage>(); // 桌面端重建主窗口页
        services.AddTransient<Pages.NowPlayingPage>();
        services.AddTransient<Pages.LibraryPage>();
        services.AddTransient<Pages.SearchPage>();
        services.AddTransient<Pages.WebViewLoginPage>();
        services.AddTransient<ViewModels.WebViewLoginViewModel>();
        services.AddTransient<Pages.DesktopDiscoverPage>();
        services.AddTransient<Pages.SettingsPage>();
        services.AddTransient<Pages.DesktopSettingsPage>();
        services.AddTransient<Pages.DesktopLibraryPage>();
        services.AddTransient<Pages.DesktopPlaylistPage>();
        services.AddTransient<Pages.DesktopArtistsPage>();
        services.AddTransient<Pages.DesktopAlbumsPage>();
        services.AddTransient<Pages.DesktopAllSongsPage>();
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
        services.AddTransient<Pages.LogPage>();
        services.AddTransient<Pages.FullLyricsPage>();
        services.AddTransient<Pages.FolderBrowserPage>();
        services.AddTransient<Pages.ArtistMatchPage>();
        services.AddTransient<Pages.ArtistMatchDetailPage>();
        services.AddTransient<Pages.DesktopLyricPage>();
        services.AddTransient<ViewModels.DesktopLyricViewModel>();
        services.AddTransient<Pages.ModelManagerPage>();
        services.AddTransient<Pages.ModelEditPage>();
        services.AddTransient<Pages.SplashSettingsPage>();
        services.AddTransient<Pages.ServerSettingsPage>();
        services.AddTransient<Pages.SongDetailPage>();
        services.AddTransient<Controls.ListeningStatsView>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        // 崩溃前日志轨迹记录（内存环形缓冲，崩溃时由 CrashReporter 落盘，无 adb 也能定位）
        builder.Logging.AddProvider(new CatClawMusic.Maui.Services.FileLoggerProvider());

        StartupLog("Step 50: Build");
        MauiApp app;
        try
        {
            app = builder.Build();
        }
        catch (Exception ex)
        {
            StartupLog($"Step 50 FAILED: {ex}");
            throw;
        }
        Services = app.Services;
        StartupLog("Step 51: Services set");

        AgentService.LibrarySnapshotProvider = () => MusicLibrarySnapshotService.LoadSnapshot();
        var chatMemoryService = Services.GetRequiredService<Services.ChatMemoryService>();
        AgentService.MemoryProvider = () => chatMemoryService.LoadMemory();
        // Yuki 人格词库知识（SQLite 词库按需查询），已配置模型时注入让模型模仿语气
        AgentService.PersonalityKnowledgeProvider = () => CatClawMusic.Maui.Services.YukiWordLibrary.Instance.GetKnowledgePromptAsync();

        // 后台迁移旧版未下采样的封面缓存，避免 UI 加载大图卡顿。
        // 延迟 5s 启动：冷启动时主线程忙于程序集加载/首帧布局，延迟非关键 I/O
        // 减少与封面解码/数据库初始化的竞争（Debug+FastDev 下效果更明显）。
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(5000); await CoverHelper.MigrateLegacyCoversAsync(); }
            catch (Exception ex) { Log.Debug("MauiProgram", $"[STARTUP] 封面迁移失败: {ex.Message}"); }
        });

        _ = Task.Run(async () =>
        {
            try
            {
                // 延迟 5s：非关键任务，避开冷启动 I/O 高峰
                await Task.Delay(5000);
                if (!File.Exists(MusicLibrarySnapshotService.SnapshotPath))
                {
                    var db = Services.GetRequiredService<MusicDatabase>();
                    await db.EnsureInitializedAsync();
                    var snapshotService = Services.GetRequiredService<Services.MusicLibrarySnapshotService>();
                    await snapshotService.GenerateSnapshotAsync(db);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("MauiProgram", $"[STARTUP] 初始快照生成失败: {ex.Message}");
            }
        });

        // 一次性校准旧版放大过的播放次数（PlayHistory.PlayCount 与 PlaySession 逐次日志）。
        // 旧版本每 30 秒 flush 都给计数 +1，导致发现页「最多播放」/统计页「总播放次数」虚高。
        // 用 Preferences 标记保证仅执行一次；对修复后的干净数据幂等。
        _ = Task.Run(async () =>
        {
            try
            {
                // 延迟 5s：非关键任务，避开冷启动 I/O 高峰
                await Task.Delay(5000);
                if (!Preferences.Default.Get("playcount_recalibrated_v1", false))
                {
                    var db = Services.GetRequiredService<MusicDatabase>();
                    var changed = await db.RecalibratePlayCountsAsync();
                    Preferences.Default.Set("playcount_recalibrated_v1", true);
                    Log.Debug("MauiProgram", $"[STARTUP] 播放次数校准完成，修正歌曲数={changed}");
                }
            }
            catch (Exception ex)
            {
                Log.Debug("MauiProgram", $"[STARTUP] 播放次数校准失败: {ex.Message}");
            }
        });

#if ANDROID
        _ = Task.Run(async () =>
        {
            try
            {
                var ffmpeg = Services.GetRequiredService<Services.FFmpegService>();
                await ffmpeg.InitializeAsync();
                var audio = Services.GetRequiredService<AudioPlayerService>();
                audio.SetFFmpegService(ffmpeg);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"FFmpeg 初始化失败: {ex.Message}"); }
            finally { startupCoordinator.MarkFFmpegReady(); }
        });
#endif

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
                    Log.Debug("MauiProgram", $"[AsyncUrlResolver] WebDAV URL 解析失败: {ex.Message}");
                }
            }
            return null;
        };

        // 扩展 RemoteUrlStreamOpener 支持 smb:// URL（用于读取内嵌歌词）和 WebDAV URL 修复
        var prevStreamOpener = LyricsService.RemoteUrlStreamOpener;
        var webDavHttpClient = new HttpClient(new SocketsHttpHandler
        {
            // 证书策略统一走 WebDavService 全局开关：有效证书直接通过；无效证书在
            // "忽略证书错误"开启时接受（记告警）、关闭时拒绝。勿在此无条件放行。
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback =
                    CatClawMusic.Data.WebDavCertPolicy.CreateCertValidationCallback("LyricsStreamOpener")
            },
            AllowAutoRedirect = true
        })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        webDavHttpClient.DefaultRequestHeaders.Add("User-Agent", "CatClawMusic/1.0");

        LyricsService.RemoteUrlStreamOpener = url =>
        {
            if (url.StartsWith("smb://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var proxyUrl = smbProxy.ToProxyUrl(url);
                    if (proxyUrl == null) return null;
                    // 使用 Range 请求仅下载文件头部（FLAC/MP3/M4A 歌词标签均在头部），
                    // 避免下载整个文件（30-100MB FLAC）导致 HttpClient 超时
                    const int lyricsHeadSize = 512 * 1024;
                    var reqMsg = new HttpRequestMessage(HttpMethod.Get, proxyUrl);
                    reqMsg.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, lyricsHeadSize - 1);
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    Log.Debug("MauiProgram", $"[Lyrics] SMB Range 请求: {proxyUrl[..Math.Min(80, proxyUrl.Length)]}...");
                    // 用 Task.Run 包裹避免 SyncContext 死锁（委托为库代码要求的同步签名）
                    var resp = Task.Run(() => _sharedHttpClient.SendAsync(reqMsg, cts.Token)).GetAwaiter().GetResult();
                    Log.Debug("MauiProgram", $"[Lyrics] SMB Range 响应: {(int)resp.StatusCode} {resp.ReasonPhrase}");
                    if (!resp.IsSuccessStatusCode) return null;
                    using var ms = new MemoryStream();
                    resp.Content.ReadAsStream().CopyTo(ms);
                    var bytes = ms.ToArray();
                    Log.Debug("MauiProgram", $"[Lyrics] SMB Range 下载完成: {bytes.Length / 1024}KB");
                    if (bytes.Length == 0) return null;
                    return new MemoryStream(bytes);
                }
                catch (Exception ex)
                {
                    Log.Debug("MauiProgram", $"[Lyrics] SMB 流打开异常: {ex.Message}");
                    return null;
                }
            }

            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Log.Debug("MauiProgram", $"[Lyrics] Android RemoteUrlStreamOpener 入口: {url[..Math.Min(60, url.Length)]}...");
                    // 用 Task.Run 包裹避免 SyncContext 死锁（委托为库代码要求的同步签名）
                    var resolvedUrl = Task.Run(() => networkMusic.ResolveWebDavPlaybackUrlAsync(url)).GetAwaiter().GetResult();
                    var downloadUrl = string.IsNullOrEmpty(resolvedUrl) ? url : resolvedUrl;
                    Log.Debug("MauiProgram", $"[Lyrics] Android RemoteUrlStreamOpener downloadUrl: {downloadUrl[..Math.Min(60, downloadUrl.Length)]}...");

                    // 从 URL userinfo 提取 Basic Auth 凭证（WebDAV 播放 URL 带 user:pass@）
                    string? authToken = null;
                    string cleanUrl = downloadUrl;
                    try
                    {
                        var uri = new Uri(downloadUrl);
                        if (!string.IsNullOrEmpty(uri.UserInfo))
                        {
                            var userInfo = uri.UserInfo;
                            var colonIdx = userInfo.IndexOf(':');
                            if (colonIdx >= 0 && colonIdx < userInfo.Length - 1)
                            {
                                var user = Uri.UnescapeDataString(userInfo[..colonIdx]);
                                var pass = Uri.UnescapeDataString(userInfo[(colonIdx + 1)..]);
                                authToken = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{user}:{pass}"));
                            }
                            cleanUrl = new UriBuilder(uri.Scheme, uri.Host, uri.Port, uri.AbsolutePath, uri.Query).ToString();
                        }
                    }
                    catch { /* URL 解析失败则使用原始 URL */ }

                    // 使用 Range 请求仅下载文件前 2MB（FLAC/MP3/M4A 标签均在头部）
                    const int headSize = 2 * 1024 * 1024;
                    var reqMsg = new HttpRequestMessage(HttpMethod.Get, cleanUrl);
                    reqMsg.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, headSize - 1);
                    if (authToken != null)
                        reqMsg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authToken);
                    Log.Debug("MauiProgram", "[Lyrics] Android RemoteUrlStreamOpener 发送 Range 请求...");
                    // 用 Task.Run 包裹避免 SyncContext 死锁（委托为库代码要求的同步签名）
                    var resp = Task.Run(() => webDavHttpClient.SendAsync(reqMsg)).GetAwaiter().GetResult();
                    Log.Debug("MauiProgram", $"[Lyrics] Android RemoteUrlStreamOpener HTTP 响应: {(int)resp.StatusCode} {resp.ReasonPhrase}");
                    if (!resp.IsSuccessStatusCode)
                        return null;
                    using var ms = new MemoryStream();
                    resp.Content.ReadAsStream().CopyTo(ms);
                    var bytes = ms.ToArray();
                    Log.Debug("MauiProgram", $"[Lyrics] Android RemoteUrlStreamOpener 下载完成: {bytes.Length / 1024}KB");
                    if (bytes.Length == 0) return null;
                    return new MemoryStream(bytes);
                }
                catch (Exception ex)
                {
                    Log.Debug("MauiProgram", $"[Lyrics] WebDAV/HTTP 流打开异常: {ex.Message}");
                    return null;
                }
            }

            return prevStreamOpener?.Invoke(url);
        };

        StartupLog("Step 99: Build done, returning");
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
        // 直接采用系统回调的真实 insets 值：
        // - 横屏状态栏隐藏 → top=0 是正确值，不应被旧值覆盖
        // - 车机/竖屏状态栏可见 → top=实际高度
        // - 手势导航 → bottom=0 是正确值
        // 旧逻辑会"防 0 覆盖"，导致横屏下 TopInset 永远锁在竖屏的 24dp，造成内容被抬高。
        bool changed = Math.Abs(topDp - TopInset) > 0.5 || Math.Abs(bottomDp - BottomInset) > 0.5;
        TopInset = topDp;
        BottomInset = bottomDp;

        if (changed)
            SafeAreaChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>系统栏高度变化时触发（页面订阅此事件以更新 padding）</summary>
    public static event EventHandler? SafeAreaChanged;
}
