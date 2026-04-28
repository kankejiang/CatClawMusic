using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;
using CoreModels = CatClawMusic.Core.Models;

namespace CatClawMusic.UI.ViewModels;

public class LibraryViewModel : BindableObject
{
    private readonly IMusicLibraryService _musicLibrary;
    private readonly INetworkMusicService? _networkMusic;
    private readonly IPermissionService? _permission;
    private string _currentTab = "Local";
    private bool _hasTriedPermission;

    public ObservableCollection<CoreModels.Song> Songs { get; set; } = new();

    public string LocalTabColor => _currentTab == "Local" ? "#FF7BAC" : "#D4C5C9";
    public string NetworkTabColor => _currentTab == "Network" ? "#FF7BAC" : "#D4C5C9";

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set { _isLoading = value; OnPropertyChanged(); } }

    private bool _showPermissionRequest;
    public bool ShowPermissionRequest { get => _showPermissionRequest; set { _showPermissionRequest = value; OnPropertyChanged(); } }

    private string _permissionText = "";
    public string PermissionText { get => _permissionText; set { _permissionText = value; OnPropertyChanged(); } }

    private string _statusText = "";
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }

    public Command<string> SwitchTabCommand { get; }
    public Command RefreshCommand { get; }
    public Command RequestPermissionCommand { get; }

    public LibraryViewModel(IMusicLibraryService musicLibrary, INetworkMusicService? networkMusic = null, IPermissionService? permission = null)
    {
        _musicLibrary = musicLibrary;
        _networkMusic = networkMusic;
        _permission = permission;
        SwitchTabCommand = new Command<string>(SwitchTab);
        RefreshCommand = new Command(async () => await RefreshAsync());
        RequestPermissionCommand = new Command(async () => await RequestAndScanAsync());
    }

    private void SwitchTab(string tab)
    {
        _currentTab = tab;
        OnPropertyChanged(nameof(LocalTabColor));
        OnPropertyChanged(nameof(NetworkTabColor));
        if (tab == "Local") _ = LoadLocalAsync();
        else _ = LoadNetworkAsync();
    }

    /// <summary>加载本地音乐：MediaStore 优先（无需权限），权限用于自定义文件夹</summary>
    public async Task LoadLocalAsync()
    {
        ShowPermissionRequest = false;
        IsLoading = true;
        StatusText = "正在扫描本地音乐...";
        Songs.Clear();

        try
        {
            // 读取用户自定义文件夹
            var customPath = Preferences.Get("music_folder", "");
            var customFolders = !string.IsNullOrWhiteSpace(customPath)
                ? new List<string> { customPath }
                : null;

            var songs = await _musicLibrary.ScanLocalAsync(customFolders);
            foreach (var s in songs) Songs.Add(s);

            StatusText = Songs.Count > 0
                ? $"🐱 共 {Songs.Count} 首歌曲"
                : "未找到音乐，请下拉刷新或授予权限";

            // 如果结果很少且未尝试过权限，提示用户
            if (Songs.Count == 0 && !_hasTriedPermission && _permission != null)
            {
                var granted = await _permission.CheckStoragePermissionAsync();
                if (!granted)
                {
                    ShowPermissionRequest = true;
                    PermissionText = _permission.GetPermissionStatus();
                }
            }
        }
        catch (Exception ex)
        {
            StatusText = $"扫描出错: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    private async Task RequestAndScanAsync()
    {
        if (_permission != null)
        {
            _hasTriedPermission = true;
            var granted = await _permission.RequestStoragePermissionAsync();
            if (!granted)
            {
                PermissionText = "权限被拒绝，请在系统设置中手动开启";
                OnPropertyChanged(nameof(PermissionText));
                return;
            }
        }
        ShowPermissionRequest = false;
        await LoadLocalAsync();
    }

    public async Task LoadNetworkAsync()
    {
        ShowPermissionRequest = false;
        IsLoading = true;
        StatusText = "正在加载网络配置...";
        Songs.Clear();

        try
        {
            if (_networkMusic == null)
            {
                StatusText = "网络服务未就绪"; return;
            }
            var profiles = await _networkMusic.GetProfilesAsync();
            var enabled = profiles.Where(p => p.IsEnabled).ToList();
            if (enabled.Count == 0)
            {
                StatusText = "请先在设置中配置网络连接（WebDAV 或 Navidrome）";
                return;
            }
            var all = new List<CoreModels.Song>();
            foreach (var p in enabled)
            {
                StatusText = $"正在连接 {p.Name}...";
                try { all.AddRange(await _networkMusic.ScanAsync(p)); } catch { }
            }
            foreach (var s in all) Songs.Add(s);
            StatusText = Songs.Count > 0 ? $"☁️ 共 {Songs.Count} 首网络歌曲" : "连接成功但未找到歌曲";
        }
        catch (Exception ex)
        {
            StatusText = $"连接失败: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    private async Task RefreshAsync()
    {
        if (_currentTab == "Local") await LoadLocalAsync();
        else await LoadNetworkAsync();
    }
}
