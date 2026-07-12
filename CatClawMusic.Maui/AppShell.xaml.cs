namespace CatClawMusic.Maui;

public partial class AppShell : Shell
{
    public AppShell(IServiceProvider services)
    {
        StartupLog("AppShell.ctor: InitializeComponent start");
        InitializeComponent();
        StartupLog("AppShell.ctor: InitializeComponent done");

        // 从 DI 容器获取 MainPage 实例（Singleton），设置为 ShellContent 的内容
        StartupLog("AppShell.ctor: Getting MainPage");
        MainShellContent.Content = services.GetRequiredService<Pages.MainPage>();
        StartupLog("AppShell.ctor: MainPage set");

        Routing.RegisterRoute("search", typeof(Pages.SearchPage));
        Routing.RegisterRoute("discover", typeof(Pages.SearchPage));

        // ── 设置 Tab 子页面 ──
        Routing.RegisterRoute("settings/appearancesettings", typeof(Pages.AppearanceSettingsPage));
        Routing.RegisterRoute("settings/generalsettings", typeof(Pages.GeneralSettingsPage));
        Routing.RegisterRoute("settings/backuprestore", typeof(Pages.BackupRestorePage));
        Routing.RegisterRoute("settings/about", typeof(Pages.AboutPage));
        Routing.RegisterRoute("settings/localmusicsettings", typeof(Pages.LocalMusicSettingsPage));
        Routing.RegisterRoute("settings/musicfoldersettings", typeof(Pages.MusicFolderSettingsPage));
        Routing.RegisterRoute("settings/remotemusicsettings", typeof(Pages.RemoteMusicSettingsPage));
        Routing.RegisterRoute("settings/pluginmanagement", typeof(Pages.PluginManagementPage));
        Routing.RegisterRoute("settings/aisettings", typeof(Pages.AiSettingsPage));
        Routing.RegisterRoute("settings/clawcirclesettings", typeof(Pages.ClawCircleSettingsPage));
        Routing.RegisterRoute("settings/permissionmanagement", typeof(Pages.PermissionManagementPage));
        Routing.RegisterRoute("settings/splashsettings", typeof(Pages.SplashSettingsPage));
        Routing.RegisterRoute("settings/serversettings", typeof(Pages.ServerSettingsPage));
        Routing.RegisterRoute("settings/p2psettings", typeof(Pages.P2PSettingsPage));

        // ── 歌单 Tab 子页面 ──
        Routing.RegisterRoute("playlists/playlistdetail", typeof(Pages.PlaylistDetailPage));

        // ── 音乐库 Tab 子页面 ──
        Routing.RegisterRoute("library/albums", typeof(Pages.AlbumsPage));
        Routing.RegisterRoute("library/artists", typeof(Pages.ArtistsPage));
        Routing.RegisterRoute("library/albumdetail", typeof(Pages.AlbumDetailPage));
        Routing.RegisterRoute("library/artistdetail", typeof(Pages.ArtistDetailPage));
        Routing.RegisterRoute("library/playlist", typeof(Pages.PlaylistPage));
        Routing.RegisterRoute("library/playlistdetail", typeof(Pages.PlaylistDetailPage));
        Routing.RegisterRoute("library/songdetail", typeof(Pages.SongDetailPage));

        // ── 播放 Tab 子页面 ──
        Routing.RegisterRoute("nowplaying/fullyrics", typeof(Pages.FullLyricsPage));
        // 全局别名：DesktopMainPage 顶栏歌词按钮使用 Shell.Current.GoToAsync("//fullyrics") 直接跳转
        Routing.RegisterRoute("fullyrics", typeof(Pages.FullLyricsPage));
        // 全局别名：DesktopMainPage 底部播放栏点击歌曲信息跳转正在播放页（Windows 桌面端使用）
        Routing.RegisterRoute("nowplaying", typeof(Pages.NowPlayingPage));

        // ── 搜索 Tab 子页面 ──
        Routing.RegisterRoute("search/artistmatch", typeof(Pages.ArtistMatchPage));
        Routing.RegisterRoute("search/artistmatchdetail", typeof(Pages.ArtistMatchDetailPage));
        Routing.RegisterRoute("discover/artistdetail", typeof(Pages.ArtistDetailPage));
        Routing.RegisterRoute("discover/albumdetail", typeof(Pages.AlbumDetailPage));

        // ── 全局子页面（不属于特定 Tab）──
        Routing.RegisterRoute("folderbrowser", typeof(Pages.FolderBrowserPage));
        Routing.RegisterRoute("desktoplyric", typeof(Pages.DesktopLyricPage));
        Routing.RegisterRoute("modelmanager", typeof(Pages.ModelManagerPage));
        Routing.RegisterRoute("modeledit", typeof(Pages.ModelEditPage));

        // 兼容旧路由调用
        Routing.RegisterRoute("appearancesettings", typeof(Pages.AppearanceSettingsPage));
        Routing.RegisterRoute("generalsettings", typeof(Pages.GeneralSettingsPage));
        Routing.RegisterRoute("backuprestore", typeof(Pages.BackupRestorePage));
        Routing.RegisterRoute("about", typeof(Pages.AboutPage));
        Routing.RegisterRoute("localmusicsettings", typeof(Pages.LocalMusicSettingsPage));
        Routing.RegisterRoute("musicfoldersettings", typeof(Pages.MusicFolderSettingsPage));
        Routing.RegisterRoute("remotemusicsettings", typeof(Pages.RemoteMusicSettingsPage));
        Routing.RegisterRoute("pluginmanagement", typeof(Pages.PluginManagementPage));
        Routing.RegisterRoute("aisettings", typeof(Pages.AiSettingsPage));
        Routing.RegisterRoute("clawcirclesettings", typeof(Pages.ClawCircleSettingsPage));
        Routing.RegisterRoute("permissionmanagement", typeof(Pages.PermissionManagementPage));
        Routing.RegisterRoute("albumdetail", typeof(Pages.AlbumDetailPage));
        Routing.RegisterRoute("artistdetail", typeof(Pages.ArtistDetailPage));
        Routing.RegisterRoute("albums", typeof(Pages.AlbumsPage));
        Routing.RegisterRoute("artists", typeof(Pages.ArtistsPage));
        Routing.RegisterRoute("playlistdetail", typeof(Pages.PlaylistDetailPage));
    }

    private static void StartupLog(string msg)
    {
        System.Diagnostics.Debug.WriteLine($"[STARTUP] {msg}");
        try
        {
            var logPath = Path.Combine(Path.GetTempPath(), "catclaw_startup.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] SHELL: {msg}\n");
        }
        catch { }
    }
}
