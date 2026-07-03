using CatClawMusic.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AppTheme = CatClawMusic.Core.Interfaces.AppTheme;
using Microsoft.Maui.Storage;
using System.IO;

namespace CatClawMusic.Maui.ViewModels;

public partial class AppearanceSettingsViewModel : ObservableObject
{
    private readonly IThemeService? _themeService;
    private bool _isLoadingTheme = false;

    [ObservableProperty]
    private bool _isDarkMode = false;

    [ObservableProperty]
    private string _selectedThemeColor = "#9B7ED8";

    [ObservableProperty]
    private List<string> _startupPageOptions = new() { "音乐库", "探索", "播放", "设置" };

    [ObservableProperty]
    private int _selectedStartupPageIndex = 0;

    [ObservableProperty]
    private bool _hasCustomBackground = false;

    [ObservableProperty]
    private ImageSource? _customBackgroundPreview = null;

    [ObservableProperty]
    private double _backgroundOpacity = 0.5;

    [ObservableProperty]
    private string _customBackgroundName = string.Empty;

    public static readonly (string Name, string Color)[] ThemeColors = new[]
    {
        ("紫色", "#9B7ED8"),
        ("粉色", "#EC407A"),
        ("蓝色", "#42A5F5"),
        ("绿色", "#66BB6A"),
        ("橙色", "#FF7043"),
        ("红色", "#EF5350"),
        ("青色", "#26A69A"),
        ("黄色", "#FFC107"),
        ("靛蓝", "#5C6BC0"),
        ("青蓝", "#00BCD4"),
    };

    public AppearanceSettingsViewModel(IThemeService? themeService = null)
    {
        _themeService = themeService;
    }

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
                "#EF5350" => AppTheme.Red,
                "#26A69A" => AppTheme.Teal,
                "#FFC107" => AppTheme.Yellow,
                "#5C6BC0" => AppTheme.Indigo,
                "#00BCD4" => AppTheme.Cyan,
                _ => AppTheme.Purple
            };
            _themeService.SetTheme(theme);
        }

        await Task.CompletedTask;
    }

    partial void OnIsDarkModeChanged(bool value)
    {
        if (_themeService != null)
        {
            _themeService.SetDarkModeSetting(value ? DarkModeSetting.Dark : DarkModeSetting.Light);
            _themeService.ApplyTheme();
        }
    }

    partial void OnSelectedStartupPageIndexChanged(int value)
    {
        Preferences.Default.Set("StartupPageIndex", value);
    }

    partial void OnBackgroundOpacityChanged(double value)
    {
        if (_isLoadingTheme) return;
        if (_themeService != null && _themeService.HasCustomBackground)
        {
            _themeService.SetCustomBackgroundOpacity(value);
        }
    }

    [RelayCommand]
    public async Task SelectBackgroundAsync()
    {
        try
        {
            var options = new PickOptions
            {
                FileTypes = FilePickerFileType.Images,
                PickerTitle = "选择背景图片"
            };

            var result = await FilePicker.Default.PickAsync(options);
            if (result == null) return;

            var appDataDir = FileSystem.AppDataDirectory;
            var bgDir = Path.Combine(appDataDir, "backgrounds");
            Directory.CreateDirectory(bgDir);
            var destPath = Path.Combine(bgDir, $"custom_bg{Path.GetExtension(result.FileName)}");

            using (var src = await result.OpenReadAsync())
            using (var dst = File.Create(destPath))
            {
                await src.CopyToAsync(dst);
            }

            _themeService?.SetCustomBackground(destPath, BackgroundOpacity);
            LoadCurrentTheme();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppearanceVM] SelectBackground failed: {ex.Message}");
        }
    }

    [RelayCommand]
    public void ClearBackground()
    {
        _themeService?.ClearCustomBackground();
        LoadCurrentTheme();
    }

    public void LoadCurrentTheme()
    {
        _isLoadingTheme = true;
        if (_themeService == null) { _isLoadingTheme = false; return; }
        var currentTheme = _themeService.CurrentTheme;
        IsDarkMode = _themeService.DarkModeSetting == DarkModeSetting.Dark;
        SelectedThemeColor = currentTheme switch
        {
            AppTheme.Purple => "#9B7ED8",
            AppTheme.Pink => "#EC407A",
            AppTheme.Blue => "#42A5F5",
            AppTheme.Green => "#66BB6A",
            AppTheme.Orange => "#FF7043",
            AppTheme.Red => "#EF5350",
            AppTheme.Teal => "#26A69A",
            AppTheme.Yellow => "#FFC107",
            AppTheme.Indigo => "#5C6BC0",
            AppTheme.Cyan => "#00BCD4",
            _ => "#9B7ED8"
        };
        SelectedStartupPageIndex = Preferences.Default.Get("StartupPageIndex", 2);

        HasCustomBackground = _themeService.HasCustomBackground;
        BackgroundOpacity = _themeService.CustomBackgroundOpacity;
        if (HasCustomBackground && _themeService.CustomBackgroundPath != null)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(_themeService.CustomBackgroundPath);
                CustomBackgroundPreview = ImageSource.FromStream(() => new MemoryStream(bytes));
                CustomBackgroundName = Path.GetFileName(_themeService.CustomBackgroundPath);
            }
            catch
            {
                CustomBackgroundPreview = null;
                CustomBackgroundName = string.Empty;
            }
        }
        else
        {
            CustomBackgroundPreview = null;
            CustomBackgroundName = string.Empty;
        }
        _isLoadingTheme = false;
    }

    public static int MapStartupIndexToTabIndex(int startupIndex)
    {
        return startupIndex switch
        {
            0 => 3,
            1 => 1,
            2 => 0,
            3 => 4,
            _ => 0
        };
    }
}
