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

    private async void OnPickFolder(object? sender, EventArgs e)
    {
#if ANDROID
        var path = await Platforms.Android.FolderPicker.PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(path) && BindingContext is SettingsViewModel vm)
        {
            vm.MusicFolder = path;
        }
#endif
    }
}
