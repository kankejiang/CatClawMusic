using CatClawMusic.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AppTheme = CatClawMusic.Core.Interfaces.AppTheme;
using Microsoft.Maui.Storage;
using System.IO;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 外观设置页 ViewModel：管理深色模式、主题色、启动页选择、自定义背景图与背景透明度等外观相关配置。
/// </summary>
public partial class AppearanceSettingsViewModel : ObservableObject
{
    private readonly IThemeService? _themeService;
    private bool _isLoadingTheme = false;

    /// <summary>是否启用深色模式</summary>
    [ObservableProperty]
    private bool _isDarkMode = false;

    /// <summary>当前选中的主题色（十六进制）</summary>
    [ObservableProperty]
    private string _selectedThemeColor = "#9B7ED8";

    /// <summary>启动页可选项列表</summary>
    [ObservableProperty]
    private List<string> _startupPageOptions = new() { "音乐库", "探索", "播放", "设置" };

    /// <summary>当前选中的启动页索引</summary>
    [ObservableProperty]
    private int _selectedStartupPageIndex = 0;

    /// <summary>是否存在自定义背景图</summary>
    [ObservableProperty]
    private bool _hasCustomBackground = false;

    /// <summary>自定义背景图预览</summary>
    [ObservableProperty]
    private ImageSource? _customBackgroundPreview = null;

    /// <summary>自定义背景透明度（0.0 - 1.0）</summary>
    [ObservableProperty]
    private double _backgroundOpacity = 0.5;

    /// <summary>自定义背景文件名</summary>
    [ObservableProperty]
    private string _customBackgroundName = string.Empty;

    /// <summary>是否启用雾面动态背景（播放页/歌词页）</summary>
    [ObservableProperty]
    private bool _frostedBackgroundEnabled = true;

    /// <summary>可选主题色列表（名称 + 十六进制颜色）</summary>
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

    /// <summary>
    /// 初始化 <see cref="AppearanceSettingsViewModel"/> 实例。
    /// </summary>
    /// <param name="themeService">主题服务，可为空（设计时支持）</param>
    public AppearanceSettingsViewModel(IThemeService? themeService = null)
    {
        _themeService = themeService;
    }

    /// <summary>选择主题色：根据十六进制颜色值切换主题，并同步到主题服务</summary>
    /// <param name="colorHex">主题色十六进制字符串</param>
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

    partial void OnFrostedBackgroundEnabledChanged(bool value)
    {
        if (_isLoadingTheme) return;
        _themeService?.SetFrostedBackgroundEnabled(value);
    }

    /// <summary>选择自定义背景图：通过文件选择器选取图片并应用为应用背景</summary>
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

    /// <summary>清除自定义背景图：移除当前背景并恢复默认外观</summary>
    [RelayCommand]
    public void ClearBackground()
    {
        _themeService?.ClearCustomBackground();
        LoadCurrentTheme();
    }

    /// <summary>使用默认背景：清除自定义图片并恢复主题渐变背景</summary>
    [RelayCommand]
    public void UseDefaultBackground()
    {
        _themeService?.ClearCustomBackground();
        LoadCurrentTheme();
    }

    /// <summary>从主题服务加载当前外观配置，同步到各绑定属性</summary>
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
        FrostedBackgroundEnabled = _themeService.FrostedBackgroundEnabled;
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

    /// <summary>将启动页索引映射为应用主 Tab 的索引</summary>
    /// <param name="startupIndex">启动页索引（0=音乐库, 1=探索, 2=播放, 3=设置）</param>
    /// <returns>主 Tab 索引</returns>
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
