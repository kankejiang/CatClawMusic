using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>通用设置页面，用于配置缓存清理等通用选项。</summary>
public partial class GeneralSettingsPage : ContentPage
{
    private readonly GeneralSettingsViewModel _viewModel;

    /// <summary>初始化 <see cref="GeneralSettingsPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="viewModel">通用设置页面对应的视图模型。</param>
    public GeneralSettingsPage(GeneralSettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <summary>当页面显示在屏幕上时触发，刷新当前缓存占用大小。</summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.RefreshCacheSizeAsync();
    }
}
