using System;
using System.Threading.Tasks;
using CatClawMusic.Data;
using CatClawMusic.Maui.ViewModels;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Maui.Controls;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Pages;

/// <summary>设置页面，作为设置入口聚合各子设置页面的导航。</summary>
public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _vm;
#if ANDROID
    /// <summary>覆盖在 hub 之上的原生 ViewPager2 导航容器：二级页以原生水平滑动进出。</summary>
    private PagerNavigator? _overlay;
#endif

    /// <summary>初始化 <see cref="SettingsPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="db">音乐数据库访问对象。</param>
    /// <param name="vm">设置页面对应的视图模型。</param>
    public SettingsPage(MusicDatabase db, SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
        _vm.NavigationRequested += OnNavigationRequested;
#if ANDROID
        // 在 hub 之上叠加原生 ViewPager2 覆盖层：二级页的进出转场由它承载（GPU 合成，丝滑）。
        _overlay = new PagerNavigator();
        if (this.Content is Grid root)
            root.Children.Add(_overlay);
#endif
    }

    /// <summary>当页面显示在屏幕上时触发，加载状态信息并检查应用更新。</summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
#if ANDROID
        ApplyBottomBarSafeArea();
#endif
        try
        {
            await _vm.LoadStatusCommand.ExecuteAsync(null);
            _vm.CheckForUpdates();
        }
        catch (Exception ex)
        {
            Log.Debug("SettingsPage.xaml", $"SettingsPage OnAppearing error: {ex.Message}");
        }
    }

    /// <summary>点击深色模式切换按钮时触发，切换深色与浅色主题。</summary>
    private void OnDarkModeToggleClicked(object? sender, EventArgs e)
        => _vm.ToggleDarkModeCommand.Execute(null);

    private async void OnAppearanceSettingsClicked(object? sender, EventArgs e)
        => await OpenSubPageAsync(typeof(AppearanceSettingsPage), "settings/appearancesettings");

    private async void OnDesktopLyricClicked(object? sender, EventArgs e)
        => await OpenSubPageAsync(typeof(DesktopLyricPage), "desktoplyric");

    private async void OnLocalMusicClicked(object? sender, EventArgs e)
        => await OpenSubPageAsync(typeof(LocalMusicSettingsPage), "settings/localmusicsettings");

    private async void OnRemoteMusicClicked(object? sender, EventArgs e)
        => await OpenSubPageAsync(typeof(RemoteMusicSettingsPage), "settings/remotemusicsettings");

    private async void OnPluginManagementClicked(object? sender, EventArgs e)
        => await OpenSubPageAsync(typeof(PluginManagementPage), "settings/pluginmanagement");

    private async void OnAiSettingsClicked(object? sender, EventArgs e)
        => await OpenSubPageAsync(typeof(AiSettingsPage), "settings/aisettings");

    private async void OnPermissionManagementClicked(object? sender, EventArgs e)
        => await OpenSubPageAsync(typeof(PermissionManagementPage), "settings/permissionmanagement");

    private async void OnGeneralSettingsClicked(object? sender, EventArgs e)
        => await OpenSubPageAsync(typeof(GeneralSettingsPage), "settings/generalsettings");

    private async void OnBackupRestoreClicked(object? sender, EventArgs e)
        => await OpenSubPageAsync(typeof(BackupRestorePage), "settings/backuprestore");

    private async void OnDiagnosticLogClicked(object? sender, EventArgs e)
        => await OpenSubPageAsync(typeof(LogPage), "settings/diagnosticlog");

    private async void OnAboutClicked(object? sender, EventArgs e)
    {
        _vm.ClearUpdateRedDot();
        await OpenSubPageAsync(typeof(AboutPage), "settings/about");
    }

    /// <summary>当视图模型请求导航时触发，若为提示消息则弹出提示对话框。</summary>
    private void OnNavigationRequested(object? sender, string page)
    {
        if (page.StartsWith("TOAST:"))
        {
            var message = page.Substring("TOAST:".Length);
            _ = DisplayAlert("提示", message, "确定");
        }
    }

    /// <summary>
    /// 打开设置二级页：Android 上推入 overlay 的 <see cref="PagerNavigator"/>（原生 ViewPager2 平滑滑入），
    /// 其余平台退回 Shell 默认导航。
    /// </summary>
    /// <param name="pageType">目标二级页类型（从 DI 解析）。</param>
    /// <param name="fallbackRoute">非 Android 时的 Shell 路由回退。</param>
#if ANDROID
    private async Task OpenSubPageAsync(Type pageType, string fallbackRoute)
    {
        if (_overlay != null
            && this.Handler?.MauiContext?.Services is { } sp
            && sp.GetService(pageType) is ContentPage page)
        {
            _overlay.PushAsync(page);
            return;
        }
        await Shell.Current.GoToAsync(fallbackRoute);
    }
#else
    private Task OpenSubPageAsync(Type pageType, string fallbackRoute)
        => Shell.Current.GoToAsync(fallbackRoute);
#endif

#if ANDROID
    /// <summary>根据系统导航栏高度（安全区底部 inset）调整底部毛玻璃底栏高度，
    /// 让磨砂条完整显示在系统导航条之上、并延伸其背后，保持与主 TabBar 一致。</summary>
    private void ApplyBottomBarSafeArea()
    {
        if (BottomGlassBar == null) return;
        var bottom = CatClawMusic.Maui.SafeAreaHelper.BottomInset;
        if (bottom < 1) bottom = 16;
        BottomGlassBar.HeightRequest = 52 + bottom;
    }
#endif
}
