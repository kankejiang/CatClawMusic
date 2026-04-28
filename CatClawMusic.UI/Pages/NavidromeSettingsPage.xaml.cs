using CatClawMusic.UI.ViewModels;

namespace CatClawMusic.UI.Pages;

public partial class NavidromeSettingsPage : ContentPage
{
    public NavidromeSettingsPage()
    {
        InitializeComponent();
        var services = Application.Current?.Handler?.MauiContext?.Services;
        BindingContext = services?.GetRequiredService<NavidromeSettingsViewModel>();
    }

    public NavidromeSettingsPage(NavidromeSettingsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
