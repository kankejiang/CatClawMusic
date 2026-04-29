using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoreProfile = CatClawMusic.Core.Models.ConnectionProfile;

namespace CatClawMusic.UI.ViewModels;

public partial class WebDavSettingsViewModel : ObservableObject
{
    private readonly INetworkFileService? _networkService;
    private readonly MusicDatabase? _database;
    private readonly IDialogService _dialogService;

    [ObservableProperty] private string _host = "";
    [ObservableProperty] private string _port = "5005";
    [ObservableProperty] private string _userName = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _basePath = "/";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "";

    public WebDavSettingsViewModel() : this(null, null, null!) { }

    public WebDavSettingsViewModel(INetworkFileService? networkService, MusicDatabase? database, IDialogService dialogService)
    {
        _networkService = networkService;
        _database = database;
        _dialogService = dialogService;
    }

    [RelayCommand]
    private async Task TestAsync()
    {
        if (string.IsNullOrWhiteSpace(Host)) { await _dialogService.ShowAlertAsync("提示", "请输入主机地址"); return; }
        IsBusy = true;
        StatusText = "正在测试...";
        try
        {
            if (_networkService != null)
            {
                var result = await _networkService.TestConnectionAsync(new CoreProfile
                {
                    Protocol = ProtocolType.WebDAV, Host = Host.Trim(),
                    Port = int.TryParse(Port, out var p) ? p : 5005,
                    UserName = UserName, Password = Password, BasePath = BasePath, IsEnabled = true
                });
                StatusText = result.Message;
                await _dialogService.ShowAlertAsync(result.Success ? "成功" : "失败", result.Message);
            }
        }
        catch (Exception ex) { StatusText = "失败"; await _dialogService.ShowAlertAsync("错误", ex.Message); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_database == null) return;
        IsBusy = true;
        try
        {
            await _database.EnsureInitializedAsync();
            await _database.SaveConnectionProfileAsync(new CoreProfile
            {
                Protocol = ProtocolType.WebDAV, Host = Host.Trim(),
                Port = int.TryParse(Port, out var p) ? p : 5005,
                UserName = UserName, Password = Password, BasePath = BasePath, IsEnabled = true
            });
            StatusText = "已保存";
            await _dialogService.ShowAlertAsync("成功", "配置已保存");
        }
        catch (Exception ex) { StatusText = "失败"; await _dialogService.ShowAlertAsync("错误", ex.Message); }
        finally { IsBusy = false; }
    }
}
