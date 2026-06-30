using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

public partial class AppearanceSettingsPage : ContentPage
{
    private readonly AppearanceSettingsViewModel _viewModel;

    public AppearanceSettingsPage(AppearanceSettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCurrentTheme();
    }
}
