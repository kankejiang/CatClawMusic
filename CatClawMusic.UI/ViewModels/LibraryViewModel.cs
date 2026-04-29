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
    private bool _hasRequestedPermission;

    public ObservableCollection<CoreModels.Song> Songs { get; } = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _showPermissionRequest;
    [ObservableProperty] private string _permissionText = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _localTabColor = "#FF7BAC";
    [ObservableProperty] private string _networkTabColor = "#D4C5C9";

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
        LocalTabColor = tab == "Local" ? "#FF7BAC" : "#D4C5C9";
        NetworkTabColor = tab == "Network" ? "#FF7BAC" : "#D4C5C9";
        if (tab == "Local") _ = LoadLocalAsync();
        else _ = LoadNetworkAsync();
    }

    [RelayCommand]
    private async Task Refresh() => await (_currentTab == "Local" ? LoadLocalAsync() : LoadNetworkAsync());

    [RelayCommand]
    private async Task RequestPermission() => await OnRequestPermission();

    public async Task LoadLocalAsync()
    {
        ShowPermissionRequest = false; IsLoading = true;
        StatusText = "正在扫描本地音乐..."; Songs.Clear();
        try
        {
            var songs = await _musicLibrary.ScanLocalAsync(GetCustomFolders());
            foreach (var s in songs) Songs.Add(s);
            if (Songs.Count > 0) { StatusText = $"🐱 共 {Songs.Count} 首歌曲"; return; }
            StatusText = "未找到音乐";
            if (_permission != null && !_hasRequestedPermission)
            {
                if (!await _permission.CheckStoragePermissionAsync())
                {
                    ShowPermissionRequest = true;
                    PermissionText = _permission.GetPermissionStatus();
                }
            }
        }
        catch (Exception ex)
        {
            StatusText = $"扫描出错: {ex.Message}";
            if (_permission != null && !_hasRequestedPermission && !await _permission.CheckStoragePermissionAsync())
            {
                ShowPermissionRequest = true;
                PermissionText = _permission.GetPermissionStatus();
            }
        }
        finally { IsLoading = false; }
    }

    public async Task LoadNetworkAsync()
    {
        ShowPermissionRequest = false; IsLoading = true;
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

    private async Task OnRequestPermission()
    {
        if (_permission == null) return;
        _hasRequestedPermission = true;
        StatusText = "正在请求权限...";
        if (await _permission.RequestStoragePermissionAsync())
        {
            ShowPermissionRequest = false;
            await LoadLocalAsync();
        }
        else
        {
            PermissionText = "权限被拒绝，请在系统设置中手动授权";
            StatusText = "权限被拒绝";
        }
    }

    private List<string>? GetCustomFolders()
    {
        var ctx = global::Android.App.Application.Context;
        var prefs = ctx.GetSharedPreferences("catclaw_prefs", global::Android.Content.FileCreationMode.Private)!;
        var path = prefs.GetString("music_folder", "");
        return !string.IsNullOrWhiteSpace(path) ? new List<string> { path } : null;
    }

    public async Task DirectRequestPermissionAsync() => await OnRequestPermission();
}
