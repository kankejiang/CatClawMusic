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
    }
}
