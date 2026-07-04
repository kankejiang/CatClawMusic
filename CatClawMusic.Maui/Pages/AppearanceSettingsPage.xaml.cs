using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>外观设置页面，用于配置应用的主题、颜色等外观相关选项。</summary>
public partial class AppearanceSettingsPage : ContentPage
{
    private readonly AppearanceSettingsViewModel _viewModel;

    /// <summary>初始化 <see cref="AppearanceSettingsPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="viewModel">外观设置页面对应的视图模型。</param>
    public AppearanceSettingsPage(AppearanceSettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <summary>当页面显示在屏幕上时触发，加载当前应用的主题设置。</summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCurrentTheme();
    }
}
