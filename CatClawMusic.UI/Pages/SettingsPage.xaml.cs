using CatClawMusic.UI.ViewModels;

namespace CatClawMusic.UI.Pages;

public partial class SettingsPage : ContentPage
{
    public SettingsPage()
    {
        InitializeComponent();
        var services = Application.Current?.Handler?.MauiContext?.Services;
        BindingContext = services?.GetRequiredService<SettingsViewModel>();
    }

    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private async void OnWebDavTapped(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("WebDavSettingsPage");
    }

    private async void OnNavidromeTapped(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("NavidromeSettingsPage");
    }

    private void OnCacheSizeChanged(object sender, ValueChangedEventArgs e)
    {
        if (BindingContext is SettingsViewModel vm)
            vm.CacheSizeGB = e.NewValue;
    }
}
