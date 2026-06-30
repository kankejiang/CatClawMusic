using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

public partial class PermissionManagementPage : ContentPage
{
    private readonly PermissionManagementViewModel _vm;

    public PermissionManagementPage(PermissionManagementViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try { await _vm.OnAppearingAsync(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PermissionPage] OnAppearing: {ex.Message}"); }
    }
}
