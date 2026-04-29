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
    private string _currentTab = "Local";

    public ObservableCollection<CoreModels.Song> Songs { get; } = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _showPermissionPrompt;
    [ObservableProperty] private string _permissionPromptText = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _localTabColor = "#9B7ED8";
    [ObservableProperty] private string _networkTabColor = "#C0B8CA";

    private bool _hasLoadedSongs;

    public LibraryViewModel(IMusicLibraryService musicLibrary, INetworkMusicService? networkMusic = null, IPermissionService? permission = null)
    {
        _musicLibrary = musicLibrary;
        _networkMusic = networkMusic;
        _permission = permission;
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
        // 已有数据且非强制刷新，跳过扫描
        if (!forceReload && _hasLoadedSongs && Songs.Count > 0)
        {
            System.Diagnostics.Debug.WriteLine("[CatClaw] 跳过重复扫描，使用缓存数据");
            return;
        }

        ShowPermissionPrompt = false; IsLoading = true;
        StatusText = "正在扫描本地音乐..."; Songs.Clear();
        try
        {
            // 三路径扫描：MediaStore → 全文件路径 → SAF Content URI
            var scannedSongs = await CatClawMusic.UI.Services.AndroidLocalScanner.ScanAsync(GetCustomFolders());
            // 入库
            var songs = await _musicLibrary.ImportSongsAsync(scannedSongs);
            foreach (var s in songs) Songs.Add(s);

            if (Songs.Count > 0)
            {
                StatusText = $"🐱 共 {Songs.Count} 首歌曲";
                _hasLoadedSongs = true;
            }
            else
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
            }
        }
        catch (Exception ex)
        {
            StatusText = $"扫描出错: {ex.Message}";
        }
        finally { IsLoading = false; }
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
