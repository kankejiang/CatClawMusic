using CatClawMusic.Data;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using CoreProfile = CatClawMusic.Core.Models.ConnectionProfile;

namespace CatClawMusic.UI.ViewModels;

public partial class NavidromeSettingsViewModel : ObservableObject
{
    private readonly ISubsonicService _subsonic;
    private readonly MusicDatabase _database;
    private readonly IDialogService _dialogService;

    [ObservableProperty] private string _name = "我的 Navidrome";
    [ObservableProperty] private string _host = "";
    [ObservableProperty] private string _port = "4533";
    [ObservableProperty] private string _userName = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private bool _useHttps;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "";

    private CoreProfile? _loadedProfile; // 已保存的配置（有 Id）

    // 无参构造函数给 DI 兜底
    public NavidromeSettingsViewModel()
        : this(
            MainApplication.Services.GetService(typeof(ISubsonicService)) as ISubsonicService
                ?? new SubsonicService(),
            (MainApplication.Services.GetService(typeof(MusicDatabase)) as MusicDatabase)!,
            MainApplication.Services.GetRequiredService<IDialogService>())
    { }

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

    private CoreProfile BuildProfile() => new()
    {
        Name = Name, Protocol = ProtocolType.Navidrome, Host = Host.Trim(),
        Port = int.TryParse(Port, out var p) ? p : 4533,
        UserName = UserName, Password = Password, UseHttps = UseHttps, IsEnabled = true
    };
}
