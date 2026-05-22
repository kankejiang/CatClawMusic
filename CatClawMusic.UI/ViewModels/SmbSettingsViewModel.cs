using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using CoreProfile = CatClawMusic.Core.Models.ConnectionProfile;

namespace CatClawMusic.UI.ViewModels;

public partial class SmbSettingsViewModel : ObservableObject
{
    private readonly INetworkFileService? _networkService;
    private readonly MusicDatabase? _database;
    private readonly IDialogService _dialogService;

    [ObservableProperty] private string _name = "我的 SMB";
    [ObservableProperty] private string _host = "";
    [ObservableProperty] private string _port = "445";
    [ObservableProperty] private string _userName = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _domainName = "";
    [ObservableProperty] private string _shareName = "";
    [ObservableProperty] private string _basePath = "\\";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "";

    private CoreProfile? _loadedProfile;

    public SmbSettingsViewModel()
        : this(
            MainApplication.Services.GetService(typeof(INetworkFileService)) as INetworkFileService,
            MainApplication.Services.GetService(typeof(MusicDatabase)) as MusicDatabase,
            MainApplication.Services.GetRequiredService<IDialogService>())
    { }

    public SmbSettingsViewModel(INetworkFileService? networkService, MusicDatabase? database, IDialogService dialogService)
    {
        _networkService = networkService;
        _database = database;
        _dialogService = dialogService;
    }

    public async Task LoadAsync()
    {
        try
        {
            if (_database == null) return;
            await _database.EnsureInitializedAsync();
            var profiles = await _database.GetConnectionProfilesAsync();
            var smb = profiles.Where(p => p.Protocol == ProtocolType.SMB)
                .OrderByDescending(p => p.Id).FirstOrDefault();
            if (smb != null)
            {
                _loadedProfile = smb;
                Name = smb.Name;
                Host = smb.Host;
                Port = smb.Port.ToString();
                UserName = smb.UserName;
                Password = smb.Password;
                DomainName = smb.DomainName;
                ShareName = smb.ShareName;
                BasePath = smb.BasePath;
                StatusText = "已加载保存的配置";
            }
        }
        catch { }
    }

    [RelayCommand]
    private async Task TestAsync()
    {
        if (string.IsNullOrWhiteSpace(Host)) { await _dialogService.ShowAlertAsync("提示", "请输入主机地址"); return; }
        if (string.IsNullOrWhiteSpace(ShareName)) { await _dialogService.ShowAlertAsync("提示", "请输入共享名"); return; }
        IsBusy = true;
        StatusText = "正在测试...";
        try
        {
            if (_networkService != null)
            {
                var result = await _networkService.TestConnectionAsync(BuildProfile());
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
        if (string.IsNullOrWhiteSpace(Host)) { await _dialogService.ShowAlertAsync("提示", "请输入主机地址"); return; }
        if (string.IsNullOrWhiteSpace(ShareName)) { await _dialogService.ShowAlertAsync("提示", "请输入共享名"); return; }
        if (_database == null) return;
        IsBusy = true;
        try
        {
            await _database.EnsureInitializedAsync();
            var profile = BuildProfile();
            if (_loadedProfile != null)
                profile.Id = _loadedProfile.Id;
            await _database.SaveConnectionProfileAsync(profile);
            _loadedProfile = profile;
            StatusText = "已保存";
            await _dialogService.ShowAlertAsync("成功", "SMB 配置已保存");
        }
        catch (Exception ex) { StatusText = "失败"; await _dialogService.ShowAlertAsync("错误", ex.Message); }
        finally { IsBusy = false; }
    }

    private CoreProfile BuildProfile() => new()
    {
        Name = Name, Protocol = ProtocolType.SMB, Host = Host.Trim(),
        Port = int.TryParse(Port, out var p) ? p : 445,
        UserName = UserName, Password = Password,
        DomainName = DomainName, ShareName = ShareName,
        BasePath = BasePath, IsEnabled = true
    };
}
