using CatClawMusic.Data;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoreProfile = CatClawMusic.Core.Models.ConnectionProfile;

namespace CatClawMusic.UI.ViewModels;

public partial class NavidromeSettingsViewModel : ObservableObject
{
    private readonly ISubsonicService? _subsonic;
    private readonly MusicDatabase? _database;
    private readonly IDialogService _dialogService;

    [ObservableProperty] private string _name = "我的 Navidrome";
    [ObservableProperty] private string _host = "";
    [ObservableProperty] private string _port = "4533";
    [ObservableProperty] private string _userName = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private bool _useHttps;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "";

    public NavidromeSettingsViewModel() : this(null, null, null!) { }

    public NavidromeSettingsViewModel(ISubsonicService? subsonic, MusicDatabase? database, IDialogService dialogService)
    {
        _subsonic = subsonic;
        _database = database;
        _dialogService = dialogService;
    }

    [RelayCommand]
    private async Task TestAsync()
    {
        if (string.IsNullOrWhiteSpace(Host)) { await _dialogService.ShowAlertAsync("提示", "请输入服务器地址"); return; }
        IsBusy = true; StatusText = "正在测试...";
        try
        {
            if (_subsonic != null)
            {
                var (ok, msg) = await _subsonic.PingAsync(BuildProfile());
                StatusText = msg;
                await _dialogService.ShowAlertAsync(ok ? "成功" : "失败", msg);
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
            await _database.SaveConnectionProfileAsync(BuildProfile());
            StatusText = "已保存";
            await _dialogService.ShowAlertAsync("成功", "Navidrome 配置已保存");
        }
        catch (Exception ex) { StatusText = "失败"; await _dialogService.ShowAlertAsync("错误", ex.Message); }
        finally { IsBusy = false; }
    }

    private CoreProfile BuildProfile() => new()
    {
        Name = Name, Protocol = ProtocolType.Navidrome, Host = Host.Trim(),
        Port = int.TryParse(Port, out var p) ? p : 4533,
        UserName = UserName, Password = Password, UseHttps = UseHttps, IsEnabled = true
    };
}
