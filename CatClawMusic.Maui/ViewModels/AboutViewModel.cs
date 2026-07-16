using CatClawMusic.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 关于页 ViewModel：展示应用版本、版权信息，提供许可证查看、交流群加入、
/// GitHub 仓库跳转以及版本更新检查等功能。
/// </summary>
public partial class AboutViewModel : ObservableObject
{
    private readonly IUpdateService? _updateService;
    private readonly IDialogService? _dialogService;

    /// <summary>应用版本号（从 AppInfo 同步，格式 v{主.次.修订}</summary>
    [ObservableProperty]
    private string _version = GetAppVersionString();

    /// <summary>获取应用版本号字符串（带 v 前缀），失败时回退到 1.0.0</summary>
    private static string GetAppVersionString()
    {
        try
        {
            var v = AppInfo.Current?.VersionString;
            return string.IsNullOrEmpty(v) ? "v1.0.0" : $"v{v}";
        }
        catch
        {
            return "v1.0.0";
        }
    }

    /// <summary>版权声明文本</summary>
    [ObservableProperty]
    private string _copyright = "© 2024-2026 CatClawMusic. All rights reserved.";

    /// <summary>是否正在检查更新</summary>
    [ObservableProperty]
    private bool _isCheckingUpdate = false;

    /// <summary>更新检查状态文本</summary>
    [ObservableProperty]
    private string _updateStatus = "";

    /// <summary>
    /// 初始化 <see cref="AboutViewModel"/> 实例。
    /// </summary>
    /// <param name="updateService">更新服务，可为空（设计时支持）</param>
    /// <param name="dialogService">对话框服务，可为空（设计时支持）</param>
    public AboutViewModel(IUpdateService? updateService = null, IDialogService? dialogService = null)
    {
        _updateService = updateService;
        _dialogService = dialogService;
    }

    /// <summary>查看开源许可证：在系统浏览器中打开 GitHub 上的 LICENSE 文件</summary>
    [RelayCommand]
    public async Task ViewLicenseAsync()
    {
        try
        {
            await Launcher.OpenAsync(new Uri("https://github.com/kankejiang/CatClawMusic/blob/main/LICENSE.txt"));
        }
        catch { }
    }

    /// <summary>加入 QQ 交流群：尝试通过链接跳转，失败时弹窗提示群号</summary>
    [RelayCommand]
    public async Task JoinGroupAsync()
    {
        try
        {
            await Launcher.OpenAsync(new Uri("https://qm.qq.com/q/Fhu3IEzqa4"));
        }
        catch
        {
            if (_dialogService != null)
                await _dialogService.ShowAlertAsync("交流群", "QQ交流群：855383639\n\n可通过QQ搜索群号加入");
        }
    }

    /// <summary>打开 GitHub 仓库页面</summary>
    [RelayCommand]
    public async Task OpenGitHubAsync()
    {
        try
        {
            await Launcher.OpenAsync(new Uri("https://github.com/kankejiang/CatClawMusic"));
        }
        catch { }
    }

    /// <summary>检查应用更新：调用更新服务获取最新版本号并与当前版本比较，弹窗提示用户</summary>
    [RelayCommand]
    public async Task CheckUpdateAsync()
    {
        if (_updateService == null) return;
        IsCheckingUpdate = true;
        UpdateStatus = "正在检查更新...";
        try
        {
            var latestVersion = await _updateService.CheckUpdateAsync();
            if (!string.IsNullOrEmpty(latestVersion) && latestVersion != Version.TrimStart('v'))
            {
                UpdateStatus = $"发现新版本 {latestVersion}";
                if (_dialogService != null)
                    await _dialogService.ShowAlertAsync("发现新版本",
                        $"新版本 {latestVersion} 已发布！\n\n请前往 GitHub 下载最新版本。");
            }
            else
            {
                UpdateStatus = "已是最新版本";
                if (_dialogService != null)
                    await _dialogService.ShowAlertAsync("检查更新", "已是最新版本");
            }
        }
        catch (Exception ex)
        {
            UpdateStatus = "检查失败";
            System.Diagnostics.Debug.WriteLine($"[AboutVM] CheckUpdate failed: {ex}");
        }
        finally { IsCheckingUpdate = false; }
    }
}
