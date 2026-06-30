using CatClawMusic.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AppTheme = CatClawMusic.Core.Interfaces.AppTheme;

namespace CatClawMusic.Maui.ViewModels;

public partial class AppearanceSettingsViewModel : ObservableObject
{
    private readonly IThemeService? _themeService;

    [ObservableProperty]
    private bool _isDarkMode = false;

    [ObservableProperty]
    private string _selectedThemeColor = "#9B7ED8";

    [ObservableProperty]
    private List<string> _startupPageOptions = new() { "音乐库", "探索", "播放", "设置" };

    [ObservableProperty]
    private int _selectedStartupPageIndex = 0;

    /// <summary>主题色选项</summary>
    public static readonly (string Name, string Color)[] ThemeColors = new[]
    {
        ("紫色", "#9B7ED8"),
        ("粉色", "#EC407A"),
        ("蓝色", "#42A5F5"),
        ("绿色", "#66BB6A"),
        ("橙色", "#FF7043"),
    };

    public AppearanceSettingsViewModel(IThemeService? themeService = null)
    {
        _themeService = themeService;
    }

    /// <summary>选择主题颜色并立即应用</summary>
    [RelayCommand]
    public async Task SelectThemeColorAsync(string? colorHex)
    {
        if (string.IsNullOrEmpty(colorHex)) return;
        SelectedThemeColor = colorHex;

        if (_themeService != null)
        {
            var theme = colorHex switch
            {
                "#9B7ED8" => AppTheme.Purple,
                "#EC407A" => AppTheme.Pink,
                "#42A5F5" => AppTheme.Blue,
                "#66BB6A" => AppTheme.Green,
                "#FF7043" => AppTheme.Orange,
                _ => AppTheme.Purple
            };
            _themeService.SetTheme(theme);
        }

        await Task.CompletedTask;
    }

    /// <summary>深色模式变化时自动应用</summary>
    partial void OnIsDarkModeChanged(bool value)
    {
        if (_themeService != null)
        {
            _themeService.SetDarkModeSetting(value ? DarkModeSetting.Dark : DarkModeSetting.Light);
            _themeService.ApplyTheme();
        }
    }

    /// <summary>加载当前主题状态</summary>
    public void LoadCurrentTheme()
    {
        if (_themeService == null) return;
        var currentTheme = _themeService.CurrentTheme;
        IsDarkMode = _themeService.DarkModeSetting == DarkModeSetting.Dark;
        SelectedThemeColor = currentTheme switch
        {
            AppTheme.Purple => "#9B7ED8",
            AppTheme.Pink => "#EC407A",
            AppTheme.Blue => "#42A5F5",
            AppTheme.Green => "#66BB6A",
            AppTheme.Orange => "#FF7043",
            _ => "#9B7ED8"
        };
    }
}
