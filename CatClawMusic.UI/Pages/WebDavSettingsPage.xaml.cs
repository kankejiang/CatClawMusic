using CatClawMusic.UI.ViewModels;

namespace CatClawMusic.UI.Pages;

public partial class WebDavSettingsPage : ContentPage
{
    public WebDavSettingsPage()
    {
        InitializeComponent();
        var services = Application.Current?.Handler?.MauiContext?.Services;
        BindingContext = services?.GetRequiredService<WebDavSettingsViewModel>();
    }

    public WebDavSettingsPage(WebDavSettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
