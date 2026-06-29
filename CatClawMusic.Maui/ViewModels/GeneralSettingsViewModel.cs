using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Maui.ViewModels;

public partial class GeneralSettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private List<string> _languageOptions = new() { "简体中文", "English" };

    [ObservableProperty]
    private int _selectedLanguageIndex = 0;

    [ObservableProperty]
    private string _cacheSize = "0 MB";

    public IAsyncRelayCommand ClearCacheCommand { get; }
    public IAsyncRelayCommand ResetSettingsCommand { get; }

    public GeneralSettingsViewModel()
    {
        ClearCacheCommand = new AsyncRelayCommand(ClearCacheAsync);
        ResetSettingsCommand = new AsyncRelayCommand(ResetSettingsAsync);
    }

    private async Task ClearCacheAsync()
    {
        // Clear app cache
        await Task.CompletedTask;
    }

    private async Task ResetSettingsAsync()
    {
        // Reset all settings to default
        await Task.CompletedTask;
    }
}
