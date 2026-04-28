using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.UI.ViewModels;

public class SettingsViewModel : BindableObject
{
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
        set
        {
            _host = value;
            OnPropertyChanged();
        }
    }

    public string Port
    {
        get => _port;
        set
        {
            _port = value;
            OnPropertyChanged();
        }
    }

    public string UserName
    {
        get => _userName;
        set
        {
            _userName = value;
            OnPropertyChanged();
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            _password = value;
            OnPropertyChanged();
        }
    }

    public string BasePath
    {
        get => _basePath;
        set
        {
            _basePath = value;
            OnPropertyChanged();
        }
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
        set
        {
            _onlyWiFiCache = value;
            OnPropertyChanged();
        }
    }

    public Command TestConnectionCommand { get; }
    public Command SaveConnectionCommand { get; }
    public Command ClearCacheCommand { get; }

    public SettingsViewModel()
    {
        TestConnectionCommand = new Command(async () => await TestConnectionAsync());
        SaveConnectionCommand = new Command(async () => await SaveConnectionAsync());
        ClearCacheCommand = new Command(async () => await ClearCacheAsync());
    }

    private async Task TestConnectionAsync()
    {
        // TODO: 实现连接测试
        await Application.Current.MainPage.DisplayAlert("测试", "连接测试功能待实现", "确定");
    }

    private async Task SaveConnectionAsync()
    {
        // TODO: 保存连接配置到数据库
        await Application.Current.MainPage.DisplayAlert("保存", "连接配置已保存", "确定");
    }

    private async Task ClearCacheAsync()
    {
        // TODO: 清除缓存
        await Application.Current.MainPage.DisplayAlert("清除", "缓存已清除", "确定");
    }
}
