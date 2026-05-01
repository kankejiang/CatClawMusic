using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoreModels = CatClawMusic.Core.Models;

namespace CatClawMusic.UI.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly IMusicLibraryService _musicLibrary;
    private readonly INetworkMusicService? _networkMusic;
    private readonly IPermissionService? _permission;
    private readonly MusicDatabase? _database;
    private readonly IMainThreadDispatcher _dispatcher;
    private string _currentTab = "Local";

    public ObservableCollection<CoreModels.Song> Songs { get; } = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _showPermissionPrompt;
    [ObservableProperty] private string _permissionPromptText = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _localTabColor = "#9B7ED8";
    [ObservableProperty] private string _networkTabColor = "#C0B8CA";
    [ObservableProperty] private int _scanProgress;
    [ObservableProperty] private string _scanStatus = "";
    [ObservableProperty] private bool _isScanning;

    private bool _hasLoadedLocal;

    public LibraryViewModel(IMusicLibraryService musicLibrary, INetworkMusicService? networkMusic = null,
        IPermissionService? permission = null, IMainThreadDispatcher? dispatcher = null, MusicDatabase? database = null)
    {
        _musicLibrary = musicLibrary;
        _networkMusic = networkMusic;
        _permission = permission;
        _database = database;
        _dispatcher = dispatcher!;
    }

    [RelayCommand]
    private void SwitchTab(string tab)
    {
        _currentTab = tab;
        LocalTabColor = tab == "Local" ? "#9B7ED8" : "#C0B8CA";
        NetworkTabColor = tab == "Network" ? "#9B7ED8" : "#C0B8CA";
        Songs.Clear(); // 切换 tab 时清空列表，让 load 方法重新填充
        if (tab == "Local")
            _ = LoadLocalAsync();
        else if (tab == "Network")
            _ = LoadNetworkAsync();
    }

    [RelayCommand]
    private async Task Refresh() => await (_currentTab == "Local" ? LoadLocalAsync(forceReload: true) : LoadNetworkAsync(forceRefresh: true));

    /// <summary>通过 SAF 系统文件管理器选择音乐文件夹</summary>
    [RelayCommand]
    private async Task PickMusicFolder()
    {
#if ANDROID
        var uri = await CatClawMusic.UI.Platforms.Android.FolderPicker.PickFolderAsync();
        if (!string.IsNullOrEmpty(uri))
        {
            _hasLoadedLocal = false;
            await LoadLocalAsync();
        }
#endif
    }

    public async Task LoadLocalAsync(bool forceReload = false)
    {
        if (!forceReload && _hasLoadedLocal && Songs.Count > 0)
        {
            System.Diagnostics.Debug.WriteLine("[CatClaw] 跳过重复扫描");
            return;
        }

        ShowPermissionPrompt = false; IsLoading = true;
        StatusText = "正在加载...";

        try
        {
            // 只从 SQLite 缓存加载，不自动扫描
            if (!forceReload)
            {
                var cachedSongs = await _musicLibrary.GetAllSongsAsync();
                if (cachedSongs.Count > 0)
                {
                    foreach (var s in cachedSongs) Songs.Add(s);
                    _hasLoadedLocal = true;
                    StatusText = $"🐱 共 {cachedSongs.Count} 首歌曲（下拉刷新）";
                    IsLoading = false;
                    return;
                }
            }

            // 首次启动且无缓存 → 提示用户选择文件夹后刷新
            if (!forceReload && !_hasLoadedLocal)
            {
                var savedUri = CatClawMusic.UI.Platforms.Android.FolderPicker.GetSavedFolderUri();
                if (string.IsNullOrEmpty(savedUri))
                {
                    PermissionPromptText = "点击下方按钮，选择手机上的音乐文件夹\n\n（使用系统文件管理器，无需额外权限）";
                    StatusText = "未选择音乐文件夹";
                }
                else
                {
                    StatusText = "下拉刷新扫描音乐";
                }
                ShowPermissionPrompt = true;
                IsLoading = false;
                return;
            }

            // 强制刷新时才扫描
            if (forceReload)
            {
                _ = BackgroundScanAsync(forceReload);
            }
        }
        catch (Exception ex) { StatusText = $"加载出错: {ex.Message}"; IsLoading = false; }
    }

    private async Task BackgroundScanAsync(bool forceReload)
    {
        try
        {
            if (Songs.Count > 0)
                _dispatcher.Post(() => StatusText = $"🐱 共 {Songs.Count} 首歌曲（刷新中...）");
            else
                _dispatcher.Post(() => { StatusText = "正在准备扫描..."; IsScanning = true; ScanProgress = 0; ScanStatus = "遍历文件夹..."; });

            // 1️⃣ SAF 文件发现（主线程限制）—— 0% → 40%
            ReportProgress(5, "查找音频文件...");
            var scannedSongs = await Task.Run(() =>
                CatClawMusic.UI.Services.AndroidLocalScanner.ScanAsync(GetCustomFolders()));
            ReportProgress(40, $"找到 {scannedSongs.Count} 首，正在入库...");

            if (scannedSongs.Count == 0)
            {
                _dispatcher.Post(() => { IsScanning = false; StatusText = "未扫描到歌曲"; });
                return;
            }

            // 2️⃣ 逐首入库 + 更新进度 —— 40% → 90%
            int interval = Math.Max(1, scannedSongs.Count / 50); // 每 2% 更新一次
            int imported = 0;
            var distinct = scannedSongs.GroupBy(s => s.FilePath).Select(g => g.First()).ToList();

            foreach (var song in distinct)
            {
                try
                {
                    song.ArtistId = await _musicLibrary.EnsureArtistAsync(song.Artist);
                    song.AlbumId = await _musicLibrary.EnsureAlbumAsync(song.Album, song.ArtistId);
                    song.DateAdded = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    await _musicLibrary.SaveSongAsync(song);
                }
                catch { }
                imported++;

                if (imported % interval == 0 || imported == distinct.Count)
                {
                    int pct = 40 + (int)(50.0 * imported / distinct.Count);
                    ReportProgress(pct, $"入库中 ({imported}/{distinct.Count})");
                }
            }

            // 3️⃣ 合并到 UI 集合 —— 90% → 100%
            _dispatcher.Post(() =>
            {
                int added = 0;
                foreach (var s in distinct)
                {
                    if (!Songs.Any(existing => existing.FilePath == s.FilePath))
                    {
                        Songs.Add(s);
                        added++;
                        if (added % 20 == 0 || added == distinct.Count)
                        {
                            int pct = 90 + (int)(10.0 * added / distinct.Count);
                            ScanProgress = pct;
                            ScanStatus = $"刷新中 ({added}/{distinct.Count})";
                        }
                    }
                }
                ScanProgress = 100;
                ScanStatus = "扫描完成";
                IsScanning = false;
                StatusText = $"🐱 共 {Songs.Count} 首歌曲";
                _hasLoadedLocal = true;
            });
        }
        catch (Exception ex)
        {
            _dispatcher.Post(() => { IsScanning = false; StatusText = $"扫描出错: {ex.Message}"; });
        }
    }

    private void ReportProgress(int pct, string status)
    {
        _dispatcher.Post(() => { ScanProgress = pct; ScanStatus = status; });
    }

    public async Task LoadNetworkAsync(bool forceRefresh = false)
    {
        ShowPermissionPrompt = false; IsLoading = true;
        StatusText = "正在加载..."; Songs.Clear();

        try
        {
            // 优先从本地缓存加载
            if (_database != null && !forceRefresh)
            {
                await _database.EnsureInitializedAsync();
                var cached = await _database.GetCachedNetworkSongsAsync();
                if (cached.Count > 0)
                {
                    foreach (var s in cached) Songs.Add(s);
                    StatusText = $"☁️ 共 {cached.Count} 首网络歌曲（缓存）";
                    IsLoading = false;
                    System.Diagnostics.Debug.WriteLine($"[CatClaw] 从缓存加载了 {cached.Count} 首网络歌曲");
                    return;
                }
            }

            // 缓存为空或强制刷新：从服务器加载
            if (_networkMusic == null) { StatusText = "网络服务未就绪"; IsLoading = false; return; }
            var enabled = (await _networkMusic.GetProfilesAsync()).Where(p => p.IsEnabled).ToList();
            if (enabled.Count == 0) { StatusText = "请先在设置中配置网络连接"; IsLoading = false; return; }
            var all = new List<CoreModels.Song>();
            foreach (var p in enabled)
            {
                StatusText = $"正在连接 {p.Name}...";
                try { all.AddRange(await _networkMusic.ScanAsync(p)); } catch { }
            }
            // ScanAsync 内部已保存到本地缓存
            foreach (var s in all) Songs.Add(s);
            StatusText = Songs.Count > 0 ? $"☁️ 共 {Songs.Count} 首网络歌曲" : "连接成功但未找到歌曲";
        }
        catch (Exception ex) { StatusText = $"连接失败: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    private List<string>? GetCustomFolders()
    {
        return CatClawMusic.UI.Platforms.Android.FolderPicker.GetSavedFolderUris();
    }
}
