using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using CoreProfile = CatClawMusic.Core.Models.ConnectionProfile;

namespace CatClawMusic.UI.ViewModels;

public partial class WebDavSettingsViewModel : ObservableObject
{
    private readonly INetworkFileService? _networkService;
    private readonly MusicDatabase? _database;
    private readonly IDialogService _dialogService;

    [ObservableProperty] private string _name = "我的 WebDAV";
    [ObservableProperty] private string _host = "";
    [ObservableProperty] private string _port = "5005";
    [ObservableProperty] private string _userName = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _basePath = "/";
    [ObservableProperty] private bool _useHttps;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "";

    private CoreProfile? _loadedProfile; // 已保存的配置（有 Id）

    // 无参构造函数给 DI 兜底
    public WebDavSettingsViewModel()
        : this(
            MainApplication.Services.GetService(typeof(INetworkFileService)) as INetworkFileService,
            MainApplication.Services.GetService(typeof(MusicDatabase)) as MusicDatabase,
            MainApplication.Services.GetRequiredService<IDialogService>())
    { }

    public WebDavSettingsViewModel(INetworkFileService? networkService, MusicDatabase? database, IDialogService dialogService)
    {
        _networkService = networkService;
        _database = database;
        _dialogService = dialogService;
    }

    /// <summary>从数据库加载上次保存的配置</summary>
    public async Task LoadAsync()
    {
        try
        {
            if (_database == null) return;
            await _database.EnsureInitializedAsync();
            var profiles = await _database.GetConnectionProfilesAsync();
            var webdav = profiles.Where(p => p.Protocol == ProtocolType.WebDAV)
                .OrderByDescending(p => p.Id).FirstOrDefault();
            if (webdav != null)
            {
                _loadedProfile = webdav;
                Name = webdav.Name;
                Host = webdav.Host;
                Port = webdav.Port.ToString();
                UserName = webdav.UserName;
                Password = webdav.Password;
                BasePath = webdav.BasePath;
                UseHttps = webdav.UseHttps;
                StatusText = "已加载保存的配置";
            }
        }
        catch { }
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
        if (_database == null) return;
        IsBusy = true;
        try
        {
            await _database.EnsureInitializedAsync();
            var profile = BuildProfile();
            // 更新已有配置，避免重复插入
            if (_loadedProfile != null)
                profile.Id = _loadedProfile.Id;
            await _database.SaveConnectionProfileAsync(profile);
            _loadedProfile = profile;
            StatusText = "已保存";
            await _dialogService.ShowAlertAsync("成功", "WebDAV 配置已保存");
        }
        catch (Exception ex) { StatusText = "失败"; await _dialogService.ShowAlertAsync("错误", ex.Message); }
        finally { IsBusy = false; }
    }

    private CoreProfile BuildProfile() => new()
    {
        Name = Name, Protocol = ProtocolType.WebDAV, Host = Host.Trim(),
        Port = int.TryParse(Port, out var p) ? p : 5005,
        UserName = UserName, Password = Password, BasePath = BasePath, UseHttps = UseHttps, IsEnabled = true
    };
}
