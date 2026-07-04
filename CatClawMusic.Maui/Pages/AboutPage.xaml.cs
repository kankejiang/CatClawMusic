using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>关于页面，用于展示应用程序的版本、作者及相关说明信息。</summary>
public partial class AboutPage : ContentPage
{
    /// <summary>初始化 <see cref="AboutPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="viewModel">关于页面对应的视图模型。</param>
    public AboutPage(AboutViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
