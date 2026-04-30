using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoreModels = CatClawMusic.Core.Models;

namespace CatClawMusic.UI.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly IMusicLibraryService _musicLibrary;
    private readonly INetworkMusicService? _networkMusic;
    private readonly IPermissionService? _permission;
    private readonly IMainThreadDispatcher _dispatcher;
    private string _currentTab = "Local";

    public ObservableCollection<CoreModels.Song> Songs { get; } = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _showPermissionPrompt;
    [ObservableProperty] private string _permissionPromptText = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _localTabColor = "#9B7ED8";
    [ObservableProperty] private string _networkTabColor = "#C0B8CA";

    private bool _hasLoadedSongs;

    public LibraryViewModel(IMusicLibraryService musicLibrary, INetworkMusicService? networkMusic = null, IPermissionService? permission = null, IMainThreadDispatcher? dispatcher = null)
    {
        _musicLibrary = musicLibrary;
        _networkMusic = networkMusic;
        _permission = permission;
        _dispatcher = dispatcher!;
    }

    [RelayCommand]
    private void SwitchTab(string tab)
    {
        _currentTab = tab;
        LocalTabColor = tab == "Local" ? "#9B7ED8" : "#C0B8CA";
        NetworkTabColor = tab == "Network" ? "#9B7ED8" : "#C0B8CA";
        if (tab == "Local" && !_hasLoadedSongs)
            _ = LoadLocalAsync();
        else if (tab == "Network" && !_hasLoadedSongs)
            _ = LoadNetworkAsync();
    }

    [RelayCommand]
    private async Task Refresh() => await (_currentTab == "Local" ? LoadLocalAsync(forceReload: true) : LoadNetworkAsync());

    /// <summary>通过 SAF 系统文件管理器选择音乐文件夹</summary>
    [RelayCommand]
    private async Task PickMusicFolder()
    {
#if ANDROID
        var uri = await CatClawMusic.UI.Platforms.Android.FolderPicker.PickFolderAsync();
        if (!string.IsNullOrEmpty(uri))
        {
            _hasLoadedSongs = false;
            await LoadLocalAsync();
        }
#endif
    }

    public async Task LoadLocalAsync(bool forceReload = false)
    {
        if (!forceReload && _hasLoadedSongs && Songs.Count > 0)
        {
            System.Diagnostics.Debug.WriteLine("[CatClaw] 跳过重复扫描");
            return;
        }

        ShowPermissionPrompt = false; IsLoading = true;
        StatusText = "正在加载...";

        try
        {
            // 1️⃣ 先显示 SQLite 缓存（秒出）
            if (!forceReload)
            {
                var cachedSongs = await _musicLibrary.GetAllSongsAsync();
                if (cachedSongs.Count > 0)
                {
                    foreach (var s in cachedSongs) Songs.Add(s);
                    _hasLoadedSongs = true;
                    StatusText = $"🐱 共 {cachedSongs.Count} 首歌曲";
                    IsLoading = false;
                }
            }

            // 2️⃣ 后台扫描（fire-and-forget，不阻塞）
            _ = BackgroundScanAsync(forceReload);
        }
        catch (Exception ex) { StatusText = $"加载出错: {ex.Message}"; IsLoading = false; }
    }

    private async Task BackgroundScanAsync(bool forceReload)
    {
        try
        {
            if (Songs.Count > 0)
                _dispatcher.Post(() => StatusText = $"🐱 共 {Songs.Count} 首歌曲（后台扫描中...）");
            else
                _dispatcher.Post(() => StatusText = "正在扫描本地音乐...");

            // SAF 扫描必须在主线程（ContentResolver 限制）
            var scannedSongs = await Task.Run(() =>
                CatClawMusic.UI.Services.AndroidLocalScanner.ScanAsync(GetCustomFolders()));

            if (scannedSongs.Count == 0) return;

            var imported = await _musicLibrary.ImportSongsAsync(scannedSongs);

            // 合并去重
            int added = 0;
            foreach (var s in imported)
            {
                if (!Songs.Any(existing => existing.FilePath == s.FilePath))
                {
                    Songs.Add(s);
                    added++;
                }
            }

            if (added > 0)
            {
                _dispatcher.Post(() =>
                {
                    StatusText = $"🐱 共 {Songs.Count} 首歌曲";
                    _hasLoadedSongs = true;
                });
            }

            // 无歌曲时提示
            if (Songs.Count == 0 && !_hasLoadedSongs)
            {
                _dispatcher.Post(() =>
                {
                    var savedUri = CatClawMusic.UI.Platforms.Android.FolderPicker.GetSavedFolderUri();
                    if (string.IsNullOrEmpty(savedUri))
                    {
                        PermissionPromptText = "点击下方按钮，选择手机上的音乐文件夹\n\n（使用系统文件管理器，无需额外权限）";
                        StatusText = "未找到本地音乐";
                    }
                    else
                    {
                        PermissionPromptText = "所选文件夹中未找到音乐文件\n可点击下方按钮重新选择";
                        StatusText = "未找到音乐";
                    }
                    ShowPermissionPrompt = true;
                });
            }
        }
        catch { }
    }

    public async Task LoadNetworkAsync()
    {
        ShowPermissionPrompt = false; IsLoading = true;
        StatusText = "正在加载网络配置..."; Songs.Clear();
        try
        {
            if (_networkMusic == null) { StatusText = "网络服务未就绪"; return; }
            var enabled = (await _networkMusic.GetProfilesAsync()).Where(p => p.IsEnabled).ToList();
            if (enabled.Count == 0) { StatusText = "请先在设置中配置网络连接"; return; }
            var all = new List<CoreModels.Song>();
            foreach (var p in enabled)
            {
                StatusText = $"正在连接 {p.Name}...";
                try { all.AddRange(await _networkMusic.ScanAsync(p)); } catch { }
            }
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
