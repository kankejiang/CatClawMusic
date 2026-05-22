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

    private CoreProfile? _loadedProfile;

    public WebDavSettingsViewModel()
        : this(
            MainApplication.Services.GetService(typeof(MusicDatabase)) as MusicDatabase,
            MainApplication.Services.GetRequiredService<IDialogService>())
    { }

    public WebDavSettingsViewModel(MusicDatabase? database, IDialogService dialogService)
    {
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

    /// <summary>
    /// 保存WebDAV配置到数据库
    /// </summary>
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

    /// <summary>
    /// 根据当前输入构建WebDAV连接配置对象
    /// </summary>
    private CoreProfile BuildProfile() => new()
    {
        Name = Name, Protocol = ProtocolType.WebDAV, Host = Host.Trim(),
        Port = int.TryParse(Port, out var p) ? p : 5005,
        UserName = UserName, Password = Password, BasePath = BasePath, UseHttps = UseHttps, IsEnabled = true
    };
}
