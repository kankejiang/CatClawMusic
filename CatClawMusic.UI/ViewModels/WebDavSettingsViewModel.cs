using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using CoreProfile = CatClawMusic.Core.Models.ConnectionProfile;

namespace CatClawMusic.UI.ViewModels;

/// <summary>
/// WebDAV连接设置ViewModel，管理WebDAV配置的加载和保存
/// </summary>
public partial class WebDavSettingsViewModel : ObservableObject
{
    private readonly MusicDatabase? _database;
    private readonly IDialogService _dialogService;

    /// <summary>
    /// 连接名称
    /// </summary>
    [ObservableProperty] private string _name = "我的 WebDAV";
    /// <summary>
    /// WebDAV服务器主机地址
    /// </summary>
    [ObservableProperty] private string _host = "";
    /// <summary>
    /// WebDAV服务器端口
    /// </summary>
    [ObservableProperty] private string _port = "5005";
    /// <summary>
    /// 用户名
    /// </summary>
    [ObservableProperty] private string _userName = "";
    /// <summary>
    /// 密码
    /// </summary>
    [ObservableProperty] private string _password = "";
    /// <summary>
    /// WebDAV基础路径
    /// </summary>
    [ObservableProperty] private string _basePath = "/";
    /// <summary>
    /// 是否使用HTTPS
    /// </summary>
    [ObservableProperty] private bool _useHttps;
    /// <summary>
    /// 是否正在执行操作
    /// </summary>
    [ObservableProperty] private bool _isBusy;
    /// <summary>
    /// 状态文本
    /// </summary>
    [ObservableProperty] private string _statusText = "";

    private CoreProfile? _loadedProfile;

    /// <summary>
    /// 无参构造函数，从DI容器获取依赖
    /// </summary>
    public WebDavSettingsViewModel()
        : this(
            MainApplication.Services.GetService(typeof(MusicDatabase)) as MusicDatabase,
            MainApplication.Services.GetRequiredService<IDialogService>())
    { }

    /// <summary>
    /// 初始化WebDAV设置ViewModel
    /// </summary>
    /// <param name="database">音乐数据库</param>
    /// <param name="dialogService">对话框服务</param>
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
        UserName = UserName, Password = Password, BasePath = BasePath, UseHttps = UseHttps,
        IsEnabled = true
    };
}
