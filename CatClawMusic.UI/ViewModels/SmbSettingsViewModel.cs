using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using CoreProfile = CatClawMusic.Core.Models.ConnectionProfile;

namespace CatClawMusic.UI.ViewModels;

/// <summary>
/// SMB连接设置ViewModel，管理SMB共享配置的加载和保存
/// </summary>
public partial class SmbSettingsViewModel : ObservableObject
{
    private readonly MusicDatabase? _database;
    private readonly IDialogService _dialogService;

    /// <summary>
    /// 连接名称
    /// </summary>
    [ObservableProperty] private string _name = "我的 SMB";
    /// <summary>
    /// SMB服务器主机地址
    /// </summary>
    [ObservableProperty] private string _host = "";
    /// <summary>
    /// SMB服务器端口
    /// </summary>
    [ObservableProperty] private string _port = "445";
    /// <summary>
    /// 用户名
    /// </summary>
    [ObservableProperty] private string _userName = "";
    /// <summary>
    /// 密码
    /// </summary>
    [ObservableProperty] private string _password = "";
    /// <summary>
    /// 域名
    /// </summary>
    [ObservableProperty] private string _domainName = "";
    /// <summary>
    /// 共享名
    /// </summary>
    [ObservableProperty] private string _shareName = "";
    /// <summary>
    /// SMB基础路径
    /// </summary>
    [ObservableProperty] private string _basePath = "\\";
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
    public SmbSettingsViewModel()
        : this(
            MainApplication.Services.GetService(typeof(MusicDatabase)) as MusicDatabase,
            MainApplication.Services.GetRequiredService<IDialogService>())
    { }

    /// <summary>
    /// 初始化SMB设置ViewModel
    /// </summary>
    /// <param name="database">音乐数据库</param>
    /// <param name="dialogService">对话框服务</param>
    public SmbSettingsViewModel(MusicDatabase? database, IDialogService dialogService)
    {
        _database = database;
        _dialogService = dialogService;
    }

    /// <summary>
    /// 从数据库加载上次保存的SMB配置
    /// </summary>
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

    /// <summary>
    /// 保存SMB配置到数据库
    /// </summary>
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

    /// <summary>
    /// 根据当前输入构建SMB连接配置对象
    /// </summary>
    /// <returns>SMB连接配置</returns>
    private CoreProfile BuildProfile() => new()
    {
        Name = Name, Protocol = ProtocolType.SMB, Host = Host.Trim(),
        Port = int.TryParse(Port, out var p) ? p : 445,
        UserName = UserName, Password = Password,
        DomainName = DomainName, ShareName = ShareName,
        BasePath = BasePath, IsEnabled = true
    };
}
