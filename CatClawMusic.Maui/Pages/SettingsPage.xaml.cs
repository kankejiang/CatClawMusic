using CatClawMusic.Data;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _vm;

    public SettingsPage(MusicDatabase db, SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
        _vm.NavigationRequested += OnNavigationRequested;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _vm.LoadStatusCommand.ExecuteAsync(null);
            _vm.CheckForUpdates();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SettingsPage OnAppearing error: {ex.Message}");
        }
    }

    private void OnDarkModeToggleClicked(object? sender, EventArgs e)
    {
        _vm.ToggleDarkModeCommand.Execute(null);
    }

    private async void OnAppearanceSettingsClicked(object? sender, EventArgs e)
        => await NavigateToSettingsAsync("appearancesettings");

    private async void OnLocalMusicClicked(object? sender, EventArgs e)
        => await NavigateToSettingsAsync("localmusicsettings");

    private async void OnRemoteMusicClicked(object? sender, EventArgs e)
        => await NavigateToSettingsAsync("remotemusicsettings");

    private async void OnPluginManagementClicked(object? sender, EventArgs e)
        => await NavigateToSettingsAsync("pluginmanagement");

    private async void OnAiSettingsClicked(object? sender, EventArgs e)
        => await NavigateToSettingsAsync("aisettings");

    private async void OnPermissionManagementClicked(object? sender, EventArgs e)
        => await NavigateToSettingsAsync("permissionmanagement");

    private async void OnGeneralSettingsClicked(object? sender, EventArgs e)
        => await NavigateToSettingsAsync("generalsettings");

    private async void OnBackupRestoreClicked(object? sender, EventArgs e)
        => await NavigateToSettingsAsync("backuprestore");

    private async void OnAboutClicked(object? sender, EventArgs e)
    {
        _vm.ClearUpdateRedDot();
        await NavigateToSettingsAsync("about");
    }

    private void OnNavigationRequested(object? sender, string page)
    {
        if (page.StartsWith("TOAST:"))
        {
            var message = page.Substring("TOAST:".Length);
            _ = DisplayAlert("提示", message, "确定");
        }
    }

    private static Task NavigateToSettingsAsync(string leafRoute)
        => Shell.Current.GoToAsync($"settings/{leafRoute}");
}
