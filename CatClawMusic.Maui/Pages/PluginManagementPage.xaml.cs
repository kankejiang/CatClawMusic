using CatClawMusic.Maui.ViewModels;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.Pages;

/// <summary>插件管理页面，用于管理已安装插件的启用、禁用与配置。</summary>
public partial class PluginManagementPage : ContentPage
{
    private readonly PluginManagementViewModel _vm;

    /// <summary>初始化 <see cref="PluginManagementPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="vm">插件管理页面对应的视图模型。</param>
    public PluginManagementPage(PluginManagementViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    /// <summary>当页面显示在屏幕上时触发，加载并刷新插件列表。</summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try { await _vm.OnAppearingAsync(); }
        catch (Exception ex) { Log.Debug("PluginManagementPage.xaml", $"[PluginPage] OnAppearing: {ex.Message}"); }
        // 后台检查已安装插件的更新（失败静默，不阻塞页面）
        _ = Task.Run(async () =>
        {
            try { await _vm.CheckAllUpdatesAsync(); }
            catch (Exception ex) { Log.Debug("PluginManagementPage.xaml", $"[PluginPage] 检查更新: {ex.Message}"); }
        });
    }

    /// <summary>插件启用开关拨动：按 e.Value 直接设置（不取反，避免与其它入口双触发翻转）。</summary>
    private void OnPluginSwitchToggled(object? sender, ToggledEventArgs e)
    {
        try
        {
            if (sender is Switch sw && sw.BindingContext is PluginItemView item)
                _vm.ApplyEnabled(item, e.Value);
        }
        catch (Exception ex)
        {
            Log.Debug("PluginManagementPage.xaml", $"[PluginPage] Switch toggled: {ex.Message}");
        }
    }
}
