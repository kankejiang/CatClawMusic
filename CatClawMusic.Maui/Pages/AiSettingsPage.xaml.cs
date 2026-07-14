using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>AI 设置页面，用于配置人工智能相关的选项与参数。</summary>
public partial class AiSettingsPage : ContentPage
{
    private readonly AiSettingsViewModel _vm;

    /// <summary>初始化 <see cref="AiSettingsPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="vm">AI 设置页面对应的视图模型。</param>
    public AiSettingsPage(AiSettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    /// <summary>当页面显示在屏幕上时触发，调用视图模型的初始化逻辑以加载设置数据。</summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        Shell.SetNavBarIsVisible(this, false);
        _vm.OnAppearing();
    }
}
