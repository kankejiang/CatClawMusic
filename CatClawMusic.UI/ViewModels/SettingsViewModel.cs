using System;
using System.Threading.Tasks;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Data;
using CatClawMusic.Core.Models;
using CoreConnectionProfile = CatClawMusic.Core.Models.ConnectionProfile;

namespace CatClawMusic.UI.ViewModels;

public class SettingsViewModel : BindableObject
{
    private readonly INetworkFileService? _networkService;
    private readonly MusicDatabase? _database;

    private string _host = "";
    private string _port = "5005";
    private string _userName = "";
    private string _password = "";
    private string _basePath = "/";
    private double _cacheSizeGB = 1;
    private bool _onlyWiFiCache = true;

    public string Host
    {
        get => _host;
        set { _host = value; OnPropertyChanged(); }
    }

    public string Port
    {
        get => _port;
        set { _port = value; OnPropertyChanged(); }
    }

    public string UserName
    {
        get => _userName;
        set { _userName = value; OnPropertyChanged(); }
    }

    public string Password
    {
        get => _password;
        set { _password = value; OnPropertyChanged(); }
    }

    public string BasePath
    {
        get => _basePath;
        set { _basePath = value; OnPropertyChanged(); }
    }

    public double CacheSizeGB
    {
        get => _cacheSizeGB;
        set
        {
            _cacheSizeGB = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CacheSizeText));
        }
    }

    public string CacheSizeText => $"{CacheSizeGB:F0} GB";

    public bool OnlyWiFiCache
    {
        get => _onlyWiFiCache;
        set { _onlyWiFiCache = value; OnPropertyChanged(); }
    }

    private string _musicFolder = Preferences.Get("music_folder", "");
    public string MusicFolder
    {
        get => _musicFolder;
        set { _musicFolder = value; OnPropertyChanged(); Preferences.Set("music_folder", value); }
    }

    private bool _isTesting;
    public bool IsTesting { get => _isTesting; set { _isTesting = value; OnPropertyChanged(); } }

    private string _statusText = "";
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }

    public Command TestConnectionCommand { get; }
    public Command SaveConnectionCommand { get; }
    public Command ClearCacheCommand { get; }

    public SettingsViewModel() : this(null, null) { }

    public SettingsViewModel(INetworkFileService? networkService, MusicDatabase? database)
    {
        _networkService = networkService;
        _database = database;

        TestConnectionCommand = new Command(async () => await TestConnectionAsync());
        SaveConnectionCommand = new Command(async () => await SaveConnectionAsync());
        ClearCacheCommand = new Command(async () => await ClearCacheAsync());
    }

    private async Task TestConnectionAsync()
    {
        if (_networkService == null)
        {
            await Shell.Current.DisplayAlert("错误", "网络服务未初始化", "确定");
            return;
        }

        if (string.IsNullOrWhiteSpace(Host))
        {
            await Shell.Current.DisplayAlert("提示", "请输入主机地址", "确定");
            return;
        }

        IsTesting = true;
        StatusText = "正在测试连接...";

        try
        {
            var profile = BuildProfile();
            var (success, message) = await _networkService.TestConnectionAsync(profile);
            StatusText = message;
            await Shell.Current.DisplayAlert(success ? "成功" : "失败", message, "确定");
        }
        catch (Exception ex)
        {
            StatusText = "连接失败";
            await Shell.Current.DisplayAlert("错误", $"连接测试失败: {ex.Message}", "确定");
        }
        finally
        {
            IsTesting = false;
        }
    }

    private async Task SaveConnectionAsync()
    {
        if (_database == null)
        {
            await Shell.Current.DisplayAlert("错误", "数据库未初始化", "确定");
            return;
        }

        try
        {
            var profile = BuildProfile();
            await _database.EnsureInitializedAsync();
            await _database.SaveConnectionProfileAsync(profile);
            StatusText = "配置已保存";

            await Shell.Current.DisplayAlert("成功", "连接配置已保存", "确定");
        }
        catch (Exception ex)
        {
            StatusText = "保存失败";
            await Shell.Current.DisplayAlert("错误", $"保存失败: {ex.Message}", "确定");
        }
    }

    private async Task ClearCacheAsync()
    {
        try
        {
            var cacheDir = Path.Combine(FileSystem.CacheDirectory, "music_cache");
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, true);
            }
            StatusText = "缓存已清除";
            await Shell.Current.DisplayAlert("成功", "缓存已清除", "确定");
        }
        catch (Exception ex)
        {
            StatusText = "清除失败";
            await Shell.Current.DisplayAlert("错误", $"清除缓存失败: {ex.Message}", "确定");
        }
    }

    private CoreConnectionProfile BuildProfile()
    {
        return new CoreConnectionProfile
        {
            Protocol = ProtocolType.WebDAV,
            Host = Host.Trim(),
            Port = int.TryParse(Port, out var p) ? p : 5005,
            UserName = UserName,
            Password = Password,
            BasePath = BasePath,
            IsEnabled = true
        };
    }
}
