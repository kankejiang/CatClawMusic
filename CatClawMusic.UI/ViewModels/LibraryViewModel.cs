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

    private bool _hasError;
    public bool HasError { get => _hasError; set { _hasError = value; OnPropertyChanged(); } }

    private string _errorMessage = "";
    public string ErrorMessage { get => _errorMessage; set { _errorMessage = value; OnPropertyChanged(); } }

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

        if (tab == "Local")
            _ = LoadLocalAsync();
        else
            _ = LoadNetworkAsync();
    }

    public async Task LoadLocalAsync()
    {
        if (_permission != null)
        {
            var granted = await _permission.CheckStoragePermissionAsync();
            if (!granted)
            {
                ShowPermissionRequest = true;
                PermissionText = _permission.GetPermissionStatus();
                return;
            }
        }
        ShowPermissionRequest = false;
        await ScanAndLoadAsync();
    }

    private async Task RequestAndScanAsync()
    {
        if (_permission != null)
        {
            var granted = await _permission.RequestStoragePermissionAsync();
            if (!granted) { StatusText = "权限被拒绝"; return; }
        }
        ShowPermissionRequest = false;
        await ScanAndLoadAsync();
    }

    private async Task ScanAndLoadAsync()
    {
        IsLoading = true; HasError = false;
        StatusText = "正在扫描本地音乐...";
        Songs.Clear();
        try
        {
            var songs = await _musicLibrary.ScanLocalAsync();
            foreach (var s in songs) Songs.Add(s);
            StatusText = Songs.Count > 0 ? $"🐱 共 {Songs.Count} 首歌曲" : "未找到音乐，放入 Music 文件夹后下拉刷新";
        }
        catch (Exception ex)
        {
            HasError = true; ErrorMessage = ex.Message;
            StatusText = "扫描出错，下拉刷新重试";
        }
        finally { IsLoading = false; }
    }

    public async Task LoadNetworkAsync()
    {
        ShowPermissionRequest = false;
        IsLoading = true; HasError = false;
        StatusText = "正在加载网络配置...";
        Songs.Clear();

        try
        {
            if (_networkMusic == null)
            {
                StatusText = "网络服务未就绪";
                return;
            }

            var profiles = await _networkMusic.GetProfilesAsync();
            if (profiles.Count == 0)
            {
                StatusText = "请先在设置中配置网络连接";
                return;
            }

            var allSongs = new List<CoreModels.Song>();
            foreach (var profile in profiles.Where(p => p.IsEnabled))
            {
                StatusText = $"正在连接 {profile.Name}...";
                try
                {
                    var songs = await _networkMusic.ScanAsync(profile);
                    allSongs.AddRange(songs);
                }
                catch { }
            }

            foreach (var s in allSongs) Songs.Add(s);
            StatusText = Songs.Count > 0 ? $"☁️ 共 {Songs.Count} 首网络歌曲" : "未找到网络歌曲";
        }
        catch (Exception ex)
        {
            HasError = true; ErrorMessage = ex.Message;
            StatusText = "连接失败，请检查设置";
        }
        finally { IsLoading = false; }
    }

    private async Task RefreshAsync()
    {
        if (_currentTab == "Local") await LoadLocalAsync();
        else await LoadNetworkAsync();
    }
}
