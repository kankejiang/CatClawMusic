using System;
using System.Threading.Tasks;
using CatClawMusic.Maui.ViewModels;
using CatClawMusic.Maui.Services;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Pages;

/// <summary>横屏桌面模式专用设置页：双列紧凑布局，子页面经 Shell 导航。</summary>
public partial class DesktopSettingsPage : ContentPage
{
    private readonly SettingsViewModel _vm;

    public DesktopSettingsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            if (DiagnosticLogSwitch != null)
                DiagnosticLogSwitch.IsToggled = LogService.Instance?.IsEnabled ?? false;
        }
        catch { }
        try
        {
            await _vm.LoadStatusCommand.ExecuteAsync(null);
            _vm.CheckForUpdates();
        }
        catch { }
    }

    private void OnDarkModeToggleClicked(object? sender, EventArgs e)
        => _vm.ToggleDarkModeCommand.Execute(null);

    private async void OnAppearanceSettingsClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("settings/appearancesettings");

    private async void OnDesktopLyricClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("desktoplyric");

    private async void OnLocalMusicClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("settings/localmusicsettings");

    private async void OnRemoteMusicClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("settings/remotemusicsettings");

    private async void OnPluginManagementClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("settings/pluginmanagement");

    private async void OnAiSettingsClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("settings/aisettings");

    private async void OnPermissionManagementClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("settings/permissionmanagement");

    private async void OnGeneralSettingsClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("settings/generalsettings");

    private async void OnBackupRestoreClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("settings/backuprestore");

    private async void OnDiagnosticLogClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("settings/diagnosticlog");

    private async void OnAboutClicked(object? sender, EventArgs e)
    {
        _vm.ClearUpdateRedDot();
        await Shell.Current.GoToAsync("settings/about");
    }

    private void OnDiagnosticLogToggled(object? sender, ToggledEventArgs e)
    {
        try
        {
            if (LogService.Instance is not { } log) return;
            log.IsEnabled = e.Value;
            if (e.Value)
            {
                log.Info("Settings", "诊断日志已开启");
                log.Flush();
            }
        }
        catch { }
    }
}
