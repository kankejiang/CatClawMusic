using CatClawMusic.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Maui.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private readonly IUpdateService? _updateService;
    private readonly IDialogService? _dialogService;

    [ObservableProperty]
    private string _version = "v1.6.0";

    [ObservableProperty]
    private string _copyright = "© 2024-2026 CatClawMusic. All rights reserved.";

    [ObservableProperty]
    private bool _isCheckingUpdate = false;

    [ObservableProperty]
    private string _updateStatus = "";

    public AboutViewModel(IUpdateService? updateService = null, IDialogService? dialogService = null)
    {
        _updateService = updateService;
        _dialogService = dialogService;
    }

    [RelayCommand]
    public async Task ViewLicenseAsync()
    {
        try
        {
            await Launcher.OpenAsync(new Uri("https://github.com/kankejiang/CatClawMusic/blob/main/LICENSE.txt"));
        }
        catch { }
    }

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

    [RelayCommand]
    public async Task OpenGitHubAsync()
    {
        try
        {
            await Launcher.OpenAsync(new Uri("https://github.com/kankejiang/CatClawMusic"));
        }
        catch { }
    }

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
