using CatClawMusic.UI.ViewModels;

namespace CatClawMusic.UI.Pages;

public partial class SettingsPage : ContentPage
{
    public SettingsPage()
    {
        InitializeComponent();
        BindingContext = new SettingsViewModel();
    }
    
    private void OnCacheSizeChanged(object sender, ValueChangedEventArgs e)
    {
        if (BindingContext is SettingsViewModel vm)
        {
            vm.CacheSizeGB = e.NewValue;
        }
    }
}
