using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

public partial class LocalMusicSettingsPage : ContentPage
{
    public LocalMusicSettingsPage(LocalMusicSettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
