using System;
using System.Threading.Tasks;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CoreProfile = CatClawMusic.Core.Models.ConnectionProfile;

namespace CatClawMusic.UI.ViewModels;

public class WebDavSettingsViewModel : BindableObject
{
    private readonly INetworkFileService? _networkService;
    private readonly MusicDatabase? _database;

    private string _host = "";
    private string _port = "5005";
    private string _userName = "";
    private string _password = "";
    private string _basePath = "/";

    public string Host { get => _host; set { _host = value; OnPropertyChanged(); } }
    public string Port { get => _port; set { _port = value; OnPropertyChanged(); } }
    public string UserName { get => _userName; set { _userName = value; OnPropertyChanged(); } }
    public string Password { get => _password; set { _password = value; OnPropertyChanged(); } }
    public string BasePath { get => _basePath; set { _basePath = value; OnPropertyChanged(); } }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set { _isBusy = value; OnPropertyChanged(); } }

    private string _statusText = "";
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }

    public Command TestCommand { get; }
    public Command SaveCommand { get; }

    public WebDavSettingsViewModel() : this(null, null) { }

    public WebDavSettingsViewModel(INetworkFileService? networkService, MusicDatabase? database)
    {
        _networkService = networkService;
        _database = database;
        TestCommand = new Command(async () => await TestAsync());
        SaveCommand = new Command(async () => await SaveAsync());
    }

    private async Task TestAsync()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            await Shell.Current.DisplayAlert("提示", "请输入主机地址", "确定");
            return;
        }

        IsBusy = true;
        StatusText = "正在测试...";
        try
        {
            if (_networkService != null)
            {
                var result = await _networkService.TestConnectionAsync(new CoreProfile
                {
                    Protocol = ProtocolType.WebDAV,
                    Host = Host.Trim(),
                    Port = int.TryParse(Port, out var p) ? p : 5005,
                    UserName = UserName,
                    Password = Password,
                    BasePath = BasePath,
                    IsEnabled = true
                });
                StatusText = result.Message;
                await Shell.Current.DisplayAlert(result.Success ? "成功" : "失败", result.Message, "确定");
            }
        }
        catch (Exception ex)
        {
            StatusText = "失败";
            await Shell.Current.DisplayAlert("错误", ex.Message, "确定");
        }
        finally { IsBusy = false; }
    }

    private async Task SaveAsync()
    {
        if (_database == null) return;
        IsBusy = true;
        try
        {
            await _database.EnsureInitializedAsync();
            await _database.SaveConnectionProfileAsync(new CoreProfile
            {
                Protocol = ProtocolType.WebDAV,
                Host = Host.Trim(),
                Port = int.TryParse(Port, out var p) ? p : 5005,
                UserName = UserName,
                Password = Password,
                BasePath = BasePath,
                IsEnabled = true
            });
            StatusText = "已保存";
            await Shell.Current.DisplayAlert("成功", "配置已保存", "确定");
        }
        catch (Exception ex)
        {
            StatusText = "失败";
            await Shell.Current.DisplayAlert("错误", ex.Message, "确定");
        }
        finally { IsBusy = false; }
    }
}
