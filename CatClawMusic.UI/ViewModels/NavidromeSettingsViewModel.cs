using CatClawMusic.Data;
using CatClawMusic.Core.Interfaces;
using CoreProfile = CatClawMusic.Core.Models.ConnectionProfile;
using CatClawMusic.Core.Models;

namespace CatClawMusic.UI.ViewModels;

public class NavidromeSettingsViewModel : BindableObject
{
    private readonly ISubsonicService? _subsonic;
    private readonly MusicDatabase? _database;

    private string _name = "我的 Navidrome";
    private string _host = "";
    private string _port = "4533";
    private string _userName = "";
    private string _password = "";
    private bool _useHttps;

    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
    public string Host { get => _host; set { _host = value; OnPropertyChanged(); } }
    public string Port { get => _port; set { _port = value; OnPropertyChanged(); } }
    public string UserName { get => _userName; set { _userName = value; OnPropertyChanged(); } }
    public string Password { get => _password; set { _password = value; OnPropertyChanged(); } }
    public bool UseHttps { get => _useHttps; set { _useHttps = value; OnPropertyChanged(); } }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set { _isBusy = value; OnPropertyChanged(); } }

    private string _statusText = "";
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }

    public Command TestCommand { get; }
    public Command SaveCommand { get; }

    public NavidromeSettingsViewModel() : this(null, null) { }

    public NavidromeSettingsViewModel(ISubsonicService? subsonic, MusicDatabase? database)
    {
        _subsonic = subsonic;
        _database = database;
        TestCommand = new Command(async () => await TestAsync());
        SaveCommand = new Command(async () => await SaveAsync());
    }

    private async Task TestAsync()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            await Shell.Current.DisplayAlert("提示", "请输入服务器地址", "确定");
            return;
        }
        IsBusy = true; StatusText = "正在测试...";
        try
        {
            if (_subsonic != null)
            {
                var (ok, msg) = await _subsonic.PingAsync(BuildProfile());
                StatusText = msg;
                await Shell.Current.DisplayAlert(ok ? "成功" : "失败", msg, "确定");
            }
        }
        catch (Exception ex) { StatusText = "失败"; await Shell.Current.DisplayAlert("错误", ex.Message, "确定"); }
        finally { IsBusy = false; }
    }

    private async Task SaveAsync()
    {
        if (_database == null) return;
        IsBusy = true;
        try
        {
            await _database.EnsureInitializedAsync();
            await _database.SaveConnectionProfileAsync(BuildProfile());
            StatusText = "已保存";
            await Shell.Current.DisplayAlert("成功", "Navidrome 配置已保存", "确定");
        }
        catch (Exception ex) { StatusText = "失败"; await Shell.Current.DisplayAlert("错误", ex.Message, "确定"); }
        finally { IsBusy = false; }
    }

    private CoreProfile BuildProfile() => new()
    {
        Name = Name,
        Protocol = ProtocolType.Navidrome,
        Host = Host.Trim(),
        Port = int.TryParse(Port, out var p) ? p : 4533,
        UserName = UserName,
        Password = Password,
        UseHttps = UseHttps,
        IsEnabled = true
    };
}
