using System.Collections.ObjectModel;
using Android.Content;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Data;
using CatClawMusic.UI.Platforms.Android;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoreModels = CatClawMusic.Core.Models;

namespace CatClawMusic.UI.ViewModels;

/// <summary>
/// 音乐库ViewModel，管理本地和网络歌曲的加载、扫描和协议切换
/// </summary>
public partial class LibraryViewModel : ObservableObject
{
    private readonly IMusicLibraryService _musicLibrary;
    private readonly INetworkMusicService? _networkMusic;
    private readonly IPermissionService? _permission;
    private readonly MusicDatabase? _database;
    private readonly IMainThreadDispatcher _dispatcher;
    [ObservableProperty] private string _currentTab = "Local";

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
    [ObservableProperty] private int _selectedProtocolIndex = 0;
    [ObservableProperty] private string _searchQuery = "";

    public ObservableCollection<string> ProtocolOptions { get; } = new();
    public List<CoreModels.ProtocolType> ProtocolTypes { get; } = new();

    /// <summary>
    /// 根据搜索框关键字过滤后的歌曲列表，空关键字时返回全部
    /// </summary>
    public List<CoreModels.Song> FilteredSongs => string.IsNullOrWhiteSpace(SearchQuery)
        ? Songs.ToList()
        : Songs.Where(s =>
            (s.Title?.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) == true) ||
            (s.Artist?.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) == true) ||
            (s.Album?.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) == true)
        ).ToList();

    private bool _hasLoadedLocal;
    private bool _suppressCollectionChanged;
    /// <summary>SharedPreferences 键名，用于持久化音乐库界面状态</summary>
    private const string PrefKey = "library_state";
    /// <summary>协议选择索引（0=WebDAV, 1=Navidrome）</summary>
    private const string PrefProtocolIndex = "protocol_index";
    /// <summary>当前标签页（Local / Network）</summary>
    private const string PrefCurrentTab = "current_tab";

    /// <summary>
    /// 初始化音乐库ViewModel，设置协议选项并恢复上次保存的协议偏好
    /// </summary>
    public LibraryViewModel(IMusicLibraryService musicLibrary, INetworkMusicService? networkMusic = null,
        IPermissionService? permission = null, IMainThreadDispatcher? dispatcher = null, MusicDatabase? database = null)
    {
        _musicLibrary = musicLibrary;
        _networkMusic = networkMusic;
        _permission = permission;
        _database = database;
        _dispatcher = dispatcher!;

        ProtocolOptions.Add("WebDAV");
        ProtocolTypes.Add(CoreModels.ProtocolType.WebDAV);
        ProtocolOptions.Add("Navidrome");
        ProtocolTypes.Add(CoreModels.ProtocolType.Navidrome);

        // 从 SharedPreferences 恢复上次选择的协议，避免每次启动重置为 WebDAV
#if ANDROID
        try
        {
            var ctx = global::Android.App.Application.Context;
            var prefs = ctx.GetSharedPreferences(PrefKey, FileCreationMode.Private);
            _selectedProtocolIndex = prefs.GetInt(PrefProtocolIndex, 0);
            if (_selectedProtocolIndex >= ProtocolTypes.Count) _selectedProtocolIndex = 0;
            var savedTab = prefs.GetString(PrefCurrentTab, "Local");
            if (savedTab == "Network") _currentTab = "Network";
        }
        catch { }
#endif
    }

    /// <summary>批量添加歌曲到 Songs，减少 CollectionChanged 触发次数</summary>
    private void AddSongsBatch(IEnumerable<CoreModels.Song> songs)
    {
        foreach (var s in songs)
            Songs.Add(s);
    }

    /// <summary>
    /// 切换本地/网络标签页
    /// </summary>
    [RelayCommand]
    private void SwitchTab(string tab)
    {
        CurrentTab = tab;
        LocalTabColor = tab == "Local" ? "#9B7ED8" : "#C0B8CA";
        NetworkTabColor = tab == "Network" ? "#9B7ED8" : "#C0B8CA";
        Songs.Clear();
        if (tab == "Local")
            _ = LoadLocalAsync();
        else if (tab == "Network")
            _ = LoadNetworkAsync();
#if ANDROID
        try
        {
            var ctx = global::Android.App.Application.Context;
            var prefs = ctx.GetSharedPreferences(PrefKey, FileCreationMode.Private);
            prefs.Edit().PutString(PrefCurrentTab, tab).Apply();
        }
        catch { }
#endif
    }

    /// <summary>
    /// 强制刷新当前标签页的歌曲列表
    /// </summary>
    [RelayCommand]
    private async Task Refresh()
    {
        if (CurrentTab == "Local")
            await LoadLocalAsync(forceReload: true);
        else
            await LoadNetworkAsync(forceRefresh: true);
    }

    /// <summary>
    /// 打开系统文件夹选择器选择音乐目录
    /// </summary>
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

    /// <summary>
    /// 加载本地音乐，支持缓存读取和增量扫描
    /// </summary>
    public async Task LoadLocalAsync(bool forceReload = false)
    {
        var validFolders = FolderPicker.ValidateSavedFolders();
        if (validFolders == 0 && FolderPicker.GetSavedFolderUris().Count > 0)
        {
            if (_database != null)
            {
                try { await _database.EnsureInitializedAsync(); await _database.ClearLocalSongsAsync(); } catch { }
            }
            Songs.Clear();
            StatusText = "未选择音乐文件夹";
            ShowPermissionPrompt = true;
            PermissionPromptText = "存储权限已过期，请重新选择音乐文件夹\n\n（使用系统文件管理器，无需额外权限）";
            IsLoading = false;
            return;
        }

        if (!forceReload && _hasLoadedLocal && Songs.Count > 0)
        {
            return;
        }

        ShowPermissionPrompt = false; IsLoading = true;
        StatusText = "正在加载...";

        try
        {
            if (!forceReload)
            {
                var cachedSongs = await _musicLibrary.GetAllSongsAsync();
                if (cachedSongs.Count > 0)
                {
                    // 增量式加载：每 50 首一批，减少 UI 通知次数
                    IsScanning = true; ScanProgress = 0;
                    int total = cachedSongs.Count;
                    int batchSize = 50;
                    for (int i = 0; i < total; i += batchSize)
                    {
                        var batch = cachedSongs.Skip(i).Take(batchSize).ToList();
                        int loaded = Math.Min(i + batchSize, total);
                        int pct = (int)(100.0 * loaded / total);
                        _dispatcher.Post(() =>
                        {
                            AddSongsBatch(batch);
                            ScanProgress = pct;
                            ScanStatus = $"加载中 ({loaded}/{total})";
                            StatusText = $"🐱 加载中... ({Songs.Count}/{total})";
                        });
                        await Task.Delay(30); // 给主线程喘息
                    }
                    _hasLoadedLocal = true;
                    _dispatcher.Post(() =>
                    {
                        IsScanning = false;
                        StatusText = $"🐱 共 {Songs.Count} 首歌曲（下拉刷新）";
                    });
                    IsLoading = false;
                    return;
                }
            }

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

            if (forceReload)
            {
                _ = BackgroundScanAsync(forceReload);
            }
        }
        catch (Exception ex) { StatusText = $"加载出错: {ex.Message}"; IsLoading = false; }
    }

    /// <summary>
    /// 后台扫描音乐文件夹，增量入库并更新UI
    /// </summary>
    private async Task BackgroundScanAsync(bool forceReload)
    {
        try
        {
            _dispatcher.Post(() => { StatusText = "正在准备扫描..."; IsScanning = true; ScanProgress = 0; ScanStatus = "遍历文件夹..."; });

            // 清除旧的本地歌曲缓存（重新扫描）
            if (_database != null)
            {
                try { await _database.EnsureInitializedAsync(); await _database.ClearLocalSongsAsync(); } catch { }
            }

            // 增量扫描：每发现一批歌曲立即入库 + 更新 UI
            var existingPaths = new HashSet<string>();

            var progress = new Progress<(int done, int total, string status)>(p =>
            {
                int pct = p.total > 0 ? (int)(90.0 * p.done / p.total) : 0;
                ReportProgress(pct, p.status);
            });

            await CatClawMusic.UI.Services.AndroidLocalScanner.ScanAsync(
                GetCustomFolders(), progress, async (batch) =>
                {
                    // 去重
                    var newSongs = batch.Where(s => existingPaths.Add(s.FilePath)).ToList();
                    if (newSongs.Count == 0) return;

                    // 逐首入库（后台线程）
                    foreach (var song in newSongs)
                    {
                        try
                        {
                            song.ArtistId = await _musicLibrary.EnsureArtistAsync(song.Artist);
                            song.AlbumId = await _musicLibrary.EnsureAlbumAsync(song.Album, song.ArtistId);
                            song.DateAdded = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                            await _musicLibrary.SaveSongAsync(song);
                        }
                        catch { }
                    }

                    // 增量更新 UI（一次 post 一批，减少主线程压力）
                    var songsToAdd = newSongs
                        .Where(s => !Songs.Any(existing => existing.FilePath == s.FilePath))
                        .ToList();
                    if (songsToAdd.Count > 0)
                    {
                        _dispatcher.Post(() =>
                        {
                            AddSongsBatch(songsToAdd);
                            StatusText = $"🐱 已扫描 {Songs.Count} 首歌曲...";
                        });
                    }
                });

            _dispatcher.Post(() =>
            {
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

    /// <summary>
    /// 在主线程更新扫描进度
    /// </summary>
    private void ReportProgress(int pct, string status)
    {
        _dispatcher.Post(() => { ScanProgress = pct; ScanStatus = status; });
    }

    /// <summary>
    /// 加载网络歌曲，支持缓存读取和强制刷新
    /// </summary>
    public async Task LoadNetworkAsync(bool forceRefresh = false)
    {
        ShowPermissionPrompt = false; IsLoading = true;
        StatusText = "正在加载...";

        try
        {
            if (_database != null && !forceRefresh)
            {
                await _database.EnsureInitializedAsync();
                var cached = await _database.GetCachedNetworkSongsAsync();
                if (cached.Count > 0)
                {
                    Songs.Clear();
                    var filtered = FilterSongsByProtocol(cached);
                    AddSongsBatch(filtered);
                    StatusText = $"☁️ 共 {filtered.Count} 首网络歌曲（缓存）";
                    IsLoading = false;
                    return;
                }
            }

            if (_networkMusic == null) { StatusText = "网络服务未就绪"; IsLoading = false; return; }
            var enabled = (await _networkMusic.GetProfilesAsync()).Where(p => p.IsEnabled).ToList();
            
            // 根据选择的协议过滤
            if (_selectedProtocolIndex < ProtocolTypes.Count)
            {
                var selectedProtocol = ProtocolTypes[_selectedProtocolIndex];
                enabled = enabled.Where(p => p.Protocol == selectedProtocol).ToList();
            }
            
            if (enabled.Count == 0) { StatusText = "请先在设置中配置网络连接"; IsLoading = false; return; }

            // 无论是强制刷新还是没有缓存，开始扫描前都要清空列表，避免重复
            if (forceRefresh || !Songs.Any())
            {
                if (forceRefresh) IsScanning = true;
                ScanProgress = 0;
                Songs.Clear();
            }

            var all = new List<CoreModels.Song>();
            foreach (var p in enabled)
            {
                var idx = enabled.IndexOf(p);
                ReportProgress(5 + idx * 20, $"连接 {p.Name}...");
                StatusText = $"正在连接 {p.Name}...";
                try
                {
                    var progress = new Progress<(int done, int total, string status)>(p =>
                    {
                        int pct = p.total > 0 ? 10 + (int)(80.0 * p.done / p.total) : 10;
                        ReportProgress(pct, p.status);
                    });

                    var scanned = await _networkMusic.ScanAsync(p, progress, (batch) =>
                    {
                        _dispatcher.Post(() =>
                        {
                            AddSongsBatch(batch);
                            StatusText = $"☁️ 已拉取 {Songs.Count} 首网络歌曲...";
                        });
                    });

                    all.AddRange(scanned);
                }
                catch { }
            }

            StatusText = Songs.Count > 0 ? $"☁️ 共 {Songs.Count} 首网络歌曲" : "连接成功但未找到歌曲";

            if (forceRefresh)
            {
                ReportProgress(100, "扫描完成");
                _dispatcher.Post(async () => { await Task.Delay(1500); IsScanning = false; });
            }
        }
        catch (Exception ex) { StatusText = $"连接失败: {ex.Message}"; }
        finally { IsLoading = false; if (!forceRefresh) IsScanning = false; }
    }

    /// <summary>
    /// 根据当前选择的协议类型过滤歌曲列表
    /// </summary>
    private List<CoreModels.Song> FilterSongsByProtocol(List<CoreModels.Song> songs)
    {
        if (_selectedProtocolIndex >= ProtocolTypes.Count)
            return songs;

        var selectedProtocol = ProtocolTypes[_selectedProtocolIndex];
        return songs.Where(s => s.Protocol == selectedProtocol).ToList();
    }

    /// <summary>
    /// 获取用户自定义的音乐文件夹列表
    /// </summary>
    private List<string>? GetCustomFolders()
    {
        return CatClawMusic.UI.Platforms.Android.FolderPicker.GetSavedFolderUris();
    }

    /// <summary>
    /// 协议选择变化时自动持久化到 SharedPreferences，确保下次启动恢复选择
    /// </summary>
    partial void OnSelectedProtocolIndexChanged(int value)
    {
#if ANDROID
        try
        {
            var ctx = global::Android.App.Application.Context;
            var prefs = ctx.GetSharedPreferences(PrefKey, FileCreationMode.Private);
            prefs.Edit().PutInt(PrefProtocolIndex, value).Apply();
        }
        catch { }
#endif
    }

    /// <summary>
    /// 搜索关键字变化时通知 UI 刷新过滤后的歌曲列表
    /// </summary>
    partial void OnSearchQueryChanged(string value) => OnPropertyChanged(nameof(FilteredSongs));
}
