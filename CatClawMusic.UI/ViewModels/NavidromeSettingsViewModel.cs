using CatClawMusic.Data;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using CoreProfile = CatClawMusic.Core.Models.ConnectionProfile;

namespace CatClawMusic.UI.ViewModels;

/// <summary>
/// Navidrome连接设置ViewModel，管理Navidrome/Subsonic服务器配置的加载、测试和保存
/// </summary>
public partial class NavidromeSettingsViewModel : ObservableObject
{
    private readonly ISubsonicService _subsonic;
    private readonly MusicDatabase _database;
    private readonly IDialogService _dialogService;

    /// <summary>
    /// 连接名称
    /// </summary>
    [ObservableProperty] private string _name = "我的 Navidrome";
    /// <summary>
    /// Navidrome服务器主机地址
    /// </summary>
    [ObservableProperty] private string _host = "";
    /// <summary>
    /// Navidrome服务器端口
    /// </summary>
    [ObservableProperty] private string _port = "4533";
    /// <summary>
    /// 用户名
    /// </summary>
    [ObservableProperty] private string _userName = "";
    /// <summary>
    /// 密码
    /// </summary>
    [ObservableProperty] private string _password = "";
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
    public NavidromeSettingsViewModel()
        : this(
            MainApplication.Services.GetService(typeof(ISubsonicService)) as ISubsonicService
                ?? new SubsonicService(),
            (MainApplication.Services.GetService(typeof(MusicDatabase)) as MusicDatabase)!,
            MainApplication.Services.GetRequiredService<IDialogService>())
    { }

    /// <summary>
    /// 初始化Navidrome设置ViewModel
    /// </summary>
    /// <param name="subsonic">Subsonic服务</param>
    /// <param name="database">音乐数据库</param>
    /// <param name="dialogService">对话框服务</param>
    public NavidromeSettingsViewModel(ISubsonicService subsonic, MusicDatabase database, IDialogService dialogService)
    {
        _subsonic = subsonic;
        _database = database;
        _dialogService = dialogService;
    }

    /// <summary>从数据库加载上次保存的配置</summary>
    public async Task LoadAsync()
    {
        try
        {
            await _database.EnsureInitializedAsync();
            var profiles = await _database.GetConnectionProfilesAsync();
            var navi = profiles.Where(p => p.Protocol == ProtocolType.Navidrome)
                .OrderByDescending(p => p.Id).FirstOrDefault();
            if (navi != null)
            {
                _loadedProfile = navi;
                Name = navi.Name;
                Host = navi.Host;
                Port = navi.Port.ToString();
                UserName = navi.UserName;
                Password = navi.Password;
                UseHttps = navi.UseHttps;
                StatusText = "已加载保存的配置";
            }
        }
        catch { }
    }

    /// <summary>
    /// 测试Navidrome服务器连接
    /// </summary>
    [RelayCommand]
    private async Task TestAsync()
    {
        if (string.IsNullOrWhiteSpace(Host)) { await _dialogService.ShowAlertAsync("提示", "请输入服务器地址"); return; }
        IsBusy = true; StatusText = "正在测试...";
        try
        {
            var (ok, msg) = await _subsonic.PingAsync(BuildProfile());
            StatusText = msg;
            await _dialogService.ShowAlertAsync(ok ? "成功" : "失败", msg);
        }
        catch (Exception ex) { StatusText = "失败"; await _dialogService.ShowAlertAsync("错误", ex.Message); }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// 保存Navidrome配置到数据库
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Host)) { await _dialogService.ShowAlertAsync("提示", "请输入服务器地址"); return; }
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
            await _dialogService.ShowAlertAsync("成功", "Navidrome 配置已保存");
        }
        catch (Exception ex) { StatusText = "失败"; await _dialogService.ShowAlertAsync("错误", ex.Message); }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// 根据当前输入构建Navidrome连接配置对象
    /// </summary>
    /// <returns>Navidrome连接配置</returns>
    private CoreProfile BuildProfile() => new()
    {
        Name = Name, Protocol = ProtocolType.Navidrome, Host = Host.Trim(),
        Port = int.TryParse(Port, out var p) ? p : 4533,
        UserName = UserName, Password = Password, UseHttps = UseHttps, IsEnabled = true
    };
}
