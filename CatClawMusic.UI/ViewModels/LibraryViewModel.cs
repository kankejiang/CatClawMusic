using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;
using CoreModels = CatClawMusic.Core.Models;

namespace CatClawMusic.UI.ViewModels;

public class LibraryViewModel : BindableObject
{
    private readonly IMusicLibraryService _musicLibrary;
    private readonly IPermissionService? _permission;
    private string _currentTab = "Local";

    public ObservableCollection<CoreModels.Song> Songs { get; set; } = new();

    public string LocalTabColor => _currentTab == "Local" ? "#FF7BAC" : "#D4C5C9";
    public string NetworkTabColor => _currentTab == "Network" ? "#FF7BAC" : "#D4C5C9";

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    private bool _showPermissionRequest;
    public bool ShowPermissionRequest
    {
        get => _showPermissionRequest;
        set { _showPermissionRequest = value; OnPropertyChanged(); }
    }

    private string _permissionText = "";
    public string PermissionText
    {
        get => _permissionText;
        set { _permissionText = value; OnPropertyChanged(); }
    }

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    private bool _hasError;
    public bool HasError
    {
        get => _hasError;
        set { _hasError = value; OnPropertyChanged(); }
    }

    private string _errorMessage = "";
    public string ErrorMessage
    {
        get => _errorMessage;
        set { _errorMessage = value; OnPropertyChanged(); }
    }

    private int _scanProgress;
    public int ScanProgress
    {
        get => _scanProgress;
        set { _scanProgress = value; OnPropertyChanged(); }
    }

    public Command<string> SwitchTabCommand { get; }
    public Command RefreshCommand { get; }
    public Command RequestPermissionCommand { get; }

    public LibraryViewModel(IMusicLibraryService musicLibrary, IPermissionService? permission = null)
    {
        _musicLibrary = musicLibrary;
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
            LoadNetworkSongs();
    }

    /// <summary>首次尝试加载，检查权限</summary>
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
            if (!granted)
            {
                StatusText = "权限被拒绝，无法扫描本地音乐";
                return;
            }
        }

        ShowPermissionRequest = false;
        await ScanAndLoadAsync();
    }

    private async Task ScanAndLoadAsync()
    {
        IsLoading = true;
        HasError = false;
        StatusText = "正在扫描本地音乐...";
        Songs.Clear();

        try
        {
            var songs = await _musicLibrary.ScanLocalAsync();
            foreach (var song in songs)
                Songs.Add(song);

            StatusText = Songs.Count > 0
                ? $"共 {Songs.Count} 首歌曲"
                : "未找到音乐，请放入 Music 文件夹后下拉刷新";
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            StatusText = "扫描出错，下拉刷新重试";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async void LoadNetworkSongs()
    {
        ShowPermissionRequest = false;
        IsLoading = true;
        HasError = false;
        StatusText = "正在连接...";
        Songs.Clear();

        try
        {
            var songs = await _musicLibrary.ScanNetworkAsync(new CoreModels.ConnectionProfile());
            foreach (var song in songs)
                Songs.Add(song);

            StatusText = Songs.Count > 0
                ? $"共 {Songs.Count} 首网络歌曲"
                : "请先在设置中配置网络连接";
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            StatusText = "连接失败，请检查设置";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RefreshAsync()
    {
        if (_currentTab == "Local")
            await LoadLocalAsync();
        else
            LoadNetworkSongs();
    }
}
