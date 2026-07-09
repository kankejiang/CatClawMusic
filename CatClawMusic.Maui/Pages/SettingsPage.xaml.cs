using CatClawMusic.Data;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>设置页面，作为设置入口聚合各子设置页面的导航。</summary>
public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _vm;

    /// <summary>初始化 <see cref="SettingsPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="db">音乐数据库访问对象。</param>
    /// <param name="vm">设置页面对应的视图模型。</param>
    public SettingsPage(MusicDatabase db, SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
        _vm.NavigationRequested += OnNavigationRequested;
    }

    /// <summary>当页面显示在屏幕上时触发，加载状态信息并检查应用更新。</summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _vm.LoadStatusCommand.ExecuteAsync(null);
            _vm.CheckForUpdates();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SettingsPage OnAppearing error: {ex.Message}");
        }
    }

    /// <summary>点击深色模式切换按钮时触发，切换深色与浅色主题。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnDarkModeToggleClicked(object? sender, EventArgs e)
    {
        _vm.ToggleDarkModeCommand.Execute(null);
    }

    /// <summary>点击外观设置项时触发，导航到外观设置页面。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnAppearanceSettingsClicked(object? sender, EventArgs e)
        => await NavigateToSettingsAsync("appearancesettings");

    /// <summary>点击桌面歌词设置项时触发，导航到桌面歌词设置页面。</summary>
    private async void OnDesktopLyricClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("desktoplyric");

    /// <summary>点击本地音乐设置项时触发，导航到本地音乐设置页面。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnLocalMusicClicked(object? sender, EventArgs e)
        => await NavigateToSettingsAsync("localmusicsettings");

    /// <summary>点击远程音乐设置项时触发，导航到远程音乐设置页面。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnRemoteMusicClicked(object? sender, EventArgs e)
        => await NavigateToSettingsAsync("remotemusicsettings");

    /// <summary>点击插件管理项时触发，导航到插件管理页面。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnPluginManagementClicked(object? sender, EventArgs e)
        => await NavigateToSettingsAsync("pluginmanagement");

    /// <summary>点击 AI 设置项时触发，导航到 AI 设置页面。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnAiSettingsClicked(object? sender, EventArgs e)
        => await NavigateToSettingsAsync("aisettings");

    /// <summary>点击权限管理项时触发，导航到权限管理页面。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnPermissionManagementClicked(object? sender, EventArgs e)
        => await NavigateToSettingsAsync("permissionmanagement");

    /// <summary>点击通用设置项时触发，导航到通用设置页面。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnGeneralSettingsClicked(object? sender, EventArgs e)
        => await NavigateToSettingsAsync("generalsettings");

    /// <summary>点击备份与恢复项时触发，导航到备份恢复页面。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnBackupRestoreClicked(object? sender, EventArgs e)
        => await NavigateToSettingsAsync("backuprestore");

    /// <summary>点击关于项时触发，清除更新红点并导航到关于页面。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnAboutClicked(object? sender, EventArgs e)
    {
        _vm.ClearUpdateRedDot();
        await NavigateToSettingsAsync("about");
    }

    /// <summary>当视图模型请求导航时触发，若为提示消息则弹出提示对话框。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="page">导航目标页面名称或提示消息标识。</param>
    private void OnNavigationRequested(object? sender, string page)
    {
        if (page.StartsWith("TOAST:"))
        {
            var message = page.Substring("TOAST:".Length);
            _ = DisplayAlert("提示", message, "确定");
        }
    }

    private static Task NavigateToSettingsAsync(string leafRoute)
        => Shell.Current.GoToAsync($"settings/{leafRoute}");
}
