using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

public partial class RemoteMusicSettingsPage : ContentPage
{
    private readonly RemoteMusicSettingsViewModel _vm;

    public RemoteMusicSettingsPage(RemoteMusicSettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try { await _vm.OnAppearingAsync(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RemoteMusicPage] OnAppearing: {ex.Message}"); }
    }
}
