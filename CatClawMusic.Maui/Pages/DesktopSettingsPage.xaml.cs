using System;
using System.Threading.Tasks;
using CatClawMusic.Maui.ViewModels;
using CatClawMusic.Maui.Services;
using CatClawMusic.Maui.Helpers;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Pages;

/// <summary>横屏桌面模式专用设置页：双列紧凑布局，子页面经 Shell 导航（桌面无 Shell 则嵌入主区域）。</summary>
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

    private void OnAppearanceSettingsClicked(object? sender, EventArgs e)
        => DesktopNavigation.GoOrEmbed("settings/appearancesettings", typeof(AppearanceSettingsPage));

    private void OnDesktopLyricClicked(object? sender, EventArgs e)
        => DesktopNavigation.GoOrEmbed("desktoplyric", typeof(DesktopLyricPage));

    private void OnLocalMusicClicked(object? sender, EventArgs e)
        => DesktopNavigation.GoOrEmbed("settings/localmusicsettings", typeof(LocalMusicSettingsPage));

    private void OnRemoteMusicClicked(object? sender, EventArgs e)
        => DesktopNavigation.GoOrEmbed("settings/remotemusicsettings", typeof(RemoteMusicSettingsPage));

    private void OnPluginManagementClicked(object? sender, EventArgs e)
        => DesktopNavigation.GoOrEmbed("settings/pluginmanagement", typeof(PluginManagementPage));

    private void OnAiSettingsClicked(object? sender, EventArgs e)
        => DesktopNavigation.GoOrEmbed("settings/aisettings", typeof(AiSettingsPage));

    private void OnPermissionManagementClicked(object? sender, EventArgs e)
        => DesktopNavigation.GoOrEmbed("settings/permissionmanagement", typeof(PermissionManagementPage));

    private void OnGeneralSettingsClicked(object? sender, EventArgs e)
        => DesktopNavigation.GoOrEmbed("settings/generalsettings", typeof(GeneralSettingsPage));

    private void OnBackupRestoreClicked(object? sender, EventArgs e)
        => DesktopNavigation.GoOrEmbed("settings/backuprestore", typeof(BackupRestorePage));

    private void OnDiagnosticLogClicked(object? sender, EventArgs e)
        => DesktopNavigation.GoOrEmbed("settings/diagnosticlog", typeof(LogPage));

    private void OnAboutClicked(object? sender, EventArgs e)
    {
        _vm.ClearUpdateRedDot();
        DesktopNavigation.GoOrEmbed("settings/about", typeof(AboutPage));
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
