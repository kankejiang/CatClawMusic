using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Maui.ViewModels;

public partial class AppearanceSettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isDarkMode = false;

    [ObservableProperty]
    private string _selectedThemeColor = "#9B7ED8";

    [ObservableProperty]
    private List<string> _startupPageOptions = new() { "音乐库", "探索", "设置" };

    [ObservableProperty]
    private int _selectedStartupPageIndex = 0;

    public IRelayCommand<string> SelectThemeColorCommand { get; }

    public AppearanceSettingsViewModel()
    {
        SelectThemeColorCommand = new RelayCommand<string>(SelectThemeColor);
    }

    private void SelectThemeColor(string? color)
    {
        if (color != null)
        {
            SelectedThemeColor = color;
            // Apply theme color
        }
    }
}
