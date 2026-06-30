using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

public partial class AiSettingsPage : ContentPage
{
    private readonly AiSettingsViewModel _vm;

    public AiSettingsPage(AiSettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.OnAppearing();
    }
}
