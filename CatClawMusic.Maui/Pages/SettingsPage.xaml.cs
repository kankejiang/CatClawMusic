using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _vm;
    private readonly MusicDatabase _db;

    public SettingsPage(MusicDatabase db, SettingsViewModel vm)
    {
        InitializeComponent();
        _db = db;
        _vm = vm;
        BindingContext = _vm;

        // Set dark mode icon
        UpdateDarkModeIcon();
        
        // Subscribe to ViewModel events
        _vm.NavigationRequested += OnNavigationRequested;
        // Note: DarkModeChanged event removed - using command instead
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        
        try
        {
            await _vm.LoadStatusCommand.ExecuteAsync(null);
            UpdateDarkModeIcon();
            _vm.CheckForUpdates();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SettingsPage OnAppearing error: {ex.Message}");
        }
    }

    private void UpdateDarkModeIcon()
    {
        try
        {
            var iconSource = _vm.DarkModeIcon;
            DarkModeButton.Source = ImageSource.FromFile(iconSource);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"UpdateDarkModeIcon error: {ex.Message}");
        }
    }

    private void OnDarkModeToggleClicked(object? sender, EventArgs e)
    {
        _vm.ToggleDarkModeCommand.Execute(null);
    }

    private void OnDarkModeChanged(object? sender, EventArgs e)
    {
        UpdateDarkModeIcon();
    }

    private async void OnAppearanceSettingsClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//appearancesettings");
    }

    private async void OnLocalMusicClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//localmusicsettings");
    }

    private async void OnRemoteMusicClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//remotemusicsettings");
    }

    private async void OnPluginManagementClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//pluginmanagement");
    }

    private async void OnAiSettingsClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//aisettings");
    }

    private async void OnPermissionManagementClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//permissionmanagement");
    }

    private async void OnGeneralSettingsClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//generalsettings");
    }

    private async void OnBackupRestoreClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//backuprestore");
    }

    private async void OnAboutClicked(object? sender, EventArgs e)
    {
        // Clear update red dot when navigating to About
        _vm.ClearUpdateRedDot();
        
        await Shell.Current.GoToAsync("//about");
    }

    private void OnNavigationRequested(object? sender, string page)
    {
        // Handle special navigation requests (like toast messages)
        if (page.StartsWith("TOAST:"))
        {
            var message = page.Substring("TOAST:".Length);
            DisplayAlert("提示", message, "确定");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        
        // Unsubscribe events
        _vm.NavigationRequested -= OnNavigationRequested;
    }
}
