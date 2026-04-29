using CatClawMusic.Core.Interfaces;
using CatClawMusic.Data;
using CatClawMusic.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoreConnectionProfile = CatClawMusic.Core.Models.ConnectionProfile;

namespace CatClawMusic.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly INetworkFileService? _networkService;
    private readonly MusicDatabase? _database;
    private readonly IDialogService _dialogService;

    [ObservableProperty] private string _host = "";
    [ObservableProperty] private string _port = "5005";
    [ObservableProperty] private string _userName = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _basePath = "/";
    [ObservableProperty] private double _cacheSizeGB = 1;
    [ObservableProperty] private bool _onlyWiFiCache = true;
    [ObservableProperty] private string _musicFolder;
    [ObservableProperty] private bool _isTesting;
    [ObservableProperty] private string _statusText = "";

    public string CacheSizeText => $"{CacheSizeGB:F0} GB";

    partial void OnCacheSizeGBChanged(double value) => OnPropertyChanged(nameof(CacheSizeText));

    public SettingsViewModel() : this(null, null, null!) { }

    public SettingsViewModel(INetworkFileService? networkService, MusicDatabase? database, IDialogService dialogService)
    {
        _networkService = networkService;
        _database = database;
        _dialogService = dialogService;

        var ctx = global::Android.App.Application.Context;
        var prefs = ctx.GetSharedPreferences("catclaw_prefs", global::Android.Content.FileCreationMode.Private)!;
        _musicFolder = prefs.GetString("music_folder", "") ?? "";
    }

    partial void OnMusicFolderChanged(string value)
    {
        var ctx = global::Android.App.Application.Context;
        var prefs = ctx.GetSharedPreferences("catclaw_prefs", global::Android.Content.FileCreationMode.Private)!;
        prefs.Edit()!.PutString("music_folder", value)!.Apply();
    }

    [RelayCommand]
    private async Task TestConnection()
    {
        if (_networkService == null) { await _dialogService.ShowAlertAsync("错误", "网络服务未初始化"); return; }
        if (string.IsNullOrWhiteSpace(Host)) { await _dialogService.ShowAlertAsync("提示", "请输入主机地址"); return; }
        IsTesting = true; StatusText = "正在测试连接...";
        try
        {
            var (success, message) = await _networkService.TestConnectionAsync(BuildProfile());
            StatusText = message;
            await _dialogService.ShowAlertAsync(success ? "成功" : "失败", message);
        }
        catch (Exception ex) { StatusText = "连接失败"; await _dialogService.ShowAlertAsync("错误", $"连接测试失败: {ex.Message}"); }
        finally { IsTesting = false; }
    }

    [RelayCommand]
    private async Task SaveConnection()
    {
        if (_database == null) { await _dialogService.ShowAlertAsync("错误", "数据库未初始化"); return; }
        try
        {
            await _database.EnsureInitializedAsync();
            await _database.SaveConnectionProfileAsync(BuildProfile());
            StatusText = "配置已保存";
            await _dialogService.ShowAlertAsync("成功", "连接配置已保存");
        }
        catch (Exception ex) { StatusText = "保存失败"; await _dialogService.ShowAlertAsync("错误", $"保存失败: {ex.Message}"); }
    }

    [RelayCommand]
    private async Task ClearCache()
    {
        try
        {
            var cacheDir = Path.Combine(global::Android.App.Application.Context.CacheDir!.AbsolutePath, "music_cache");
            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true);
            StatusText = "缓存已清除";
            await _dialogService.ShowAlertAsync("成功", "缓存已清除");
        }
        catch (Exception ex) { StatusText = "清除失败"; await _dialogService.ShowAlertAsync("错误", $"清除缓存失败: {ex.Message}"); }
    }

    private CoreConnectionProfile BuildProfile() => new()
    {
        Protocol = ProtocolType.WebDAV, Host = Host.Trim(),
        Port = int.TryParse(Port, out var p) ? p : 5005,
        UserName = UserName, Password = Password, BasePath = BasePath, IsEnabled = true
    };
}
