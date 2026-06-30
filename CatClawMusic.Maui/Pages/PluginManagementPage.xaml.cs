using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

public partial class PluginManagementPage : ContentPage
{
    private readonly PluginManagementViewModel _vm;

    public PluginManagementPage(PluginManagementViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try { await _vm.OnAppearingAsync(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PluginPage] OnAppearing: {ex.Message}"); }
    }
}
